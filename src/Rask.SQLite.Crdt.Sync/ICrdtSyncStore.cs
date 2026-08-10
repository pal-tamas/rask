namespace Rask.SQLite.Crdt.Sync;

/// <summary>
///     Remembers how far this replica has got: what it last published, and the last object it read from
///     each peer.
/// </summary>
/// <remarks>
///     <para>
///         <b>This state is a cache, not a record.</b> The SQLite database is itself the durable store —
///         an edit is committed locally before any sync is attempted — so losing everything here costs
///         re-uploading and re-reading, and never costs data. That is a materially different bargain from
///         a queue-based sync, where losing the queue loses a user's offline edits, and it is why an
///         in-memory implementation is a legitimate choice rather than a test double.
///     </para>
///     <para>
///         Peers are tracked by the <b>last object key</b> read, not by a version. A change's
///         <c>db_version</c> is assigned by whichever database it is read from, so a peer's version is
///         meaningless here; the key ordering in the bucket is the only portable watermark.
///     </para>
/// </remarks>
public interface ICrdtSyncStore
{
    /// <summary>
    ///     The local <c>db_version</c> covered by the last successful upload, or <c>null</c> if this
    ///     replica has never published.
    /// </summary>
    /// <remarks>
    ///     <c>null</c> is answered from the bucket rather than assumed to mean "nothing published" — a
    ///     reinstalled device with the same database would otherwise re-upload its whole history.
    /// </remarks>
    Task<long?> GetPublishedVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Records the local version covered by an upload that has completed.</summary>
    Task SetPublishedVersionAsync(long version, CancellationToken cancellationToken = default);

    /// <summary>The last object key read from <paramref name="peer" />, or <c>null</c> for a new peer.</summary>
    Task<string?> GetPeerWatermarkAsync(string peer, CancellationToken cancellationToken = default);

    /// <summary>Records the last object key successfully read from <paramref name="peer" />.</summary>
    Task SetPeerWatermarkAsync(string peer, string key, CancellationToken cancellationToken = default);
}

/// <summary>
///     Keeps the sync state for the lifetime of the process. Safe to lose — see
///     <see cref="ICrdtSyncStore" /> — so it is a reasonable default rather than only a test double.
/// </summary>
public sealed class InMemoryCrdtSyncStore : ICrdtSyncStore
{
    private readonly Dictionary<string, string> _watermarks = new(StringComparer.Ordinal);
    private long? _published;

    /// <inheritdoc />
    public Task<long?> GetPublishedVersionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_published);

    /// <inheritdoc />
    public Task SetPublishedVersionAsync(long version, CancellationToken cancellationToken = default)
    {
        _published = version;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> GetPeerWatermarkAsync(string peer, CancellationToken cancellationToken = default) =>
        Task.FromResult(_watermarks.GetValueOrDefault(peer));

    /// <inheritdoc />
    public Task SetPeerWatermarkAsync(string peer, string key, CancellationToken cancellationToken = default)
    {
        _watermarks[peer] = key;
        return Task.CompletedTask;
    }
}
