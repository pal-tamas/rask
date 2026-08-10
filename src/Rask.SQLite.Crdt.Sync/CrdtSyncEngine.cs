using Rask.ObjectStore;

namespace Rask.SQLite.Crdt.Sync;

/// <summary>
///     Shares a CRDT SQLite database between devices through an object-storage bucket, with nothing in
///     between.
/// </summary>
/// <remarks>
///     <para>
///         The design rests on one rule: <b>each device writes only under its own prefix</b> —
///         <c>{prefix}{site-id}/changes/</c> — and never touches another's. No two devices ever write the
///         same key, so there is nothing to lock, nothing to retry on conflict, and no lease to renew or
///         to leak if a device disappears mid-write. Everything else follows from it.
///     </para>
///     <para>
///         Keys carry the publishing replica's own <c>db_version</c> range in fixed-width hex, so they
///         sort in the order the changes were made and a remembered key resumes exactly where the last
///         sync stopped. Peers are discovered with a grouped listing, so finding out who exists costs one
///         response listing the <em>devices</em> rather than one listing every object they have written.
///     </para>
///     <para>
///         <b>Offline is the normal case.</b> A local edit is committed to SQLite by <c>SaveChanges</c>
///         before any of this runs, so there is no queue to lose and no "offline mode" to enter — a failed
///         sync leaves the database untouched and the next one publishes the same changes. Re-sending is
///         safe precisely because applying a change twice does nothing.
///     </para>
/// </remarks>
public sealed class CrdtSyncEngine
{
    private readonly ICrdtChangeFeed _feed;
    private readonly CrdtSyncOptions _options;
    private readonly ICrdtSyncStore _state;
    private readonly IObjectStore _store;

    /// <summary>Creates an engine over a bucket, a change feed and somewhere to keep watermarks.</summary>
    public CrdtSyncEngine(
        IObjectStore store,
        ICrdtChangeFeed feed,
        ICrdtSyncStore? state = null,
        CrdtSyncOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(feed);

        _store = store;
        _feed = feed;
        _state = state ?? new InMemoryCrdtSyncStore();
        _options = options ?? new CrdtSyncOptions();
        _options.Validate();
    }

    /// <summary>Raised after every sync attempt, including a failed one.</summary>
    public event Action<CrdtSyncStatus>? Changed;

    /// <summary>The result of the most recent attempt.</summary>
    public CrdtSyncStatus Status { get; private set; } = new(CrdtSyncPhase.Idle, 0, 0, 0);

    /// <summary>Publishes this replica's own changes, then applies everything new from its peers.</summary>
    /// <remarks>
    ///     Push before pull, so a device that is about to learn about a peer's work has already recorded
    ///     its own. The order is not required for correctness — merging is commutative — but it keeps the
    ///     window in which an edit exists only on one device as short as possible.
    /// </remarks>
    public async Task<CrdtSyncStatus> SyncAsync(CancellationToken cancellationToken = default)
    {
        Report(new CrdtSyncStatus(CrdtSyncPhase.Syncing, 0, 0, Status.Peers));

        try
        {
            var published = await PushAsync(cancellationToken).ConfigureAwait(false);
            var (received, peers) = await PullAsync(cancellationToken).ConfigureAwait(false);

            return Report(new CrdtSyncStatus(CrdtSyncPhase.Synced, published, received, peers));
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Anything the store throws — no network, bad credentials, a 500 — is the same answer to the
            // only question the app has: the bucket is not reachable right now. The database is
            // untouched either way.
            return Report(new CrdtSyncStatus(CrdtSyncPhase.Offline, 0, 0, Status.Peers, error.Message));
        }
    }

    /// <summary>Uploads the changes this replica has made since it last published.</summary>
    public async Task<int> PushAsync(CancellationToken cancellationToken = default)
    {
        var site = Hex(await _feed.GetSiteIdAsync(cancellationToken).ConfigureAwait(false));
        var since = await ResolvePublishedVersionAsync(site, cancellationToken).ConfigureAwait(false);

        var changes = await _feed.ReadLocalChangesAsync(since, cancellationToken).ConfigureAwait(false);
        if (changes.Count == 0)
        {
            return 0;
        }

        var uploaded = 0;
        for (var offset = 0; offset < changes.Count; offset += _options.MaxChangesPerObject)
        {
            var batch = changes.Skip(offset).Take(_options.MaxChangesPerObject).ToList();
            var from = batch[0].DbVersion;
            var to = batch[^1].DbVersion;

            await _store.PutAsync(
                    $"{Prefix}{site}/changes/{from:x16}__{to:x16}.json",
                    CrdtChangeCodec.Encode(batch),
                    cancellationToken)
                .ConfigureAwait(false);

            // After each batch rather than at the end: an interrupted push leaves the batches that did
            // land recorded, so the next sync resumes instead of re-uploading them.
            await _state.SetPublishedVersionAsync(to, cancellationToken).ConfigureAwait(false);
            uploaded += batch.Count;
        }

        return uploaded;
    }

    /// <summary>Applies everything peers have published since this replica last read from them.</summary>
    public async Task<(int Received, int Peers)> PullAsync(CancellationToken cancellationToken = default)
    {
        var me = Hex(await _feed.GetSiteIdAsync(cancellationToken).ConfigureAwait(false));
        var prefixes = await _store.ListPrefixesAsync(Prefix, cancellationToken).ConfigureAwait(false);

        var received = 0;
        var peers = 0;

        foreach (var prefix in prefixes)
        {
            var peer = prefix.TrimEnd('/')[Prefix.Length..];
            if (peer.Length == 0 || string.Equals(peer, me, StringComparison.Ordinal))
            {
                continue;
            }

            peers++;
            received += await PullPeerAsync(peer, cancellationToken).ConfigureAwait(false);
        }

        return (received, peers);
    }

    private async Task<int> PullPeerAsync(string peer, CancellationToken cancellationToken)
    {
        var folder = $"{Prefix}{peer}/changes/";
        var watermark = await _state.GetPeerWatermarkAsync(peer, cancellationToken).ConfigureAwait(false);
        var objects = await _store.ListAsync(folder, watermark, cancellationToken).ConfigureAwait(false);

        var received = 0;
        foreach (var entry in objects)
        {
            var content = await _store.OpenReadAsync(entry.Key, cancellationToken).ConfigureAwait(false);
            if (content is null)
            {
                // Listed but gone: a peer compacted between the listing and the read. Not an error — the
                // changes it held are either already applied or still described by whatever replaced it.
                continue;
            }

            byte[] bytes;
            await using (content)
            {
                using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                bytes = buffer.ToArray();
            }

            var changes = CrdtChangeCodec.Decode(bytes);
            await _feed.ApplyChangesAsync(changes, cancellationToken).ConfigureAwait(false);
            received += changes.Count;

            // Only after the changes are committed locally. Advancing first would silently skip an
            // object if applying it failed, and skipped changes never come back — the peer has no reason
            // to publish them again.
            await _state.SetPeerWatermarkAsync(peer, entry.Key, cancellationToken).ConfigureAwait(false);
        }

        return received;
    }

    /// <summary>
    ///     What this replica has already published, asked of the bucket when local state does not know.
    /// </summary>
    /// <remarks>
    ///     A reinstall, a cleared cache or an in-memory store all lose the local answer while the database
    ///     keeps its full history — so trusting <c>null</c> to mean "never published" would re-upload
    ///     everything on every fresh start. The bucket already holds the answer in its key ordering.
    /// </remarks>
    private async Task<long> ResolvePublishedVersionAsync(string site, CancellationToken cancellationToken)
    {
        var known = await _state.GetPublishedVersionAsync(cancellationToken).ConfigureAwait(false);
        if (known is { } version)
        {
            return version;
        }

        var mine = await _store.ListAsync($"{Prefix}{site}/changes/", null, cancellationToken)
            .ConfigureAwait(false);
        if (mine.Count == 0)
        {
            return -1;
        }

        var last = mine[^1].Key;
        var resolved = ParseUpperBound(last);
        await _state.SetPublishedVersionAsync(resolved, cancellationToken).ConfigureAwait(false);
        return resolved;
    }

    /// <summary>The upper <c>db_version</c> a key covers — the second half of <c>{from}__{to}.json</c>.</summary>
    internal static long ParseUpperBound(string key)
    {
        var name = key[(key.LastIndexOf('/') + 1)..];
        var separator = name.IndexOf("__", StringComparison.Ordinal);

        if (separator < 0 || !name.EndsWith(".json", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{key}' is not a change object written by this package ({{from}}__{{to}}.json).");
        }

        var upper = name[(separator + 2)..^".json".Length];
        return long.Parse(upper, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private string Prefix => _options.Normalized;

    private static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();

    private CrdtSyncStatus Report(CrdtSyncStatus status)
    {
        Status = status;
        Changed?.Invoke(status);
        return status;
    }
}
