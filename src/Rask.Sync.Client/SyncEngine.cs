using System.Text;
using System.Text.Json;
using Rask.ObjectStore;

namespace Rask.Sync.Client;

/// <summary>How the engine addresses the bucket and when it gives up.</summary>
public sealed class SyncEngineOptions
{
    /// <summary>The prefix every client's own prefix sits under. Must end with <c>/</c>.</summary>
    public string RootPrefix { get; set; } = "clients/";

    /// <summary>Wall clock, for <see cref="SyncStatus.LastSyncedAt" />. Injected so tests can pin it.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}

/// <summary>
///     Shares a <see cref="SyncState" /> between devices through an object-storage bucket, with no server
///     in between.
/// </summary>
/// <remarks>
///     <para>
///         <b>Each client writes only under its own prefix</b> — <c>{root}{clientId}/ops/</c> — and never
///         touches another's. That single rule is what removes coordination: no two clients ever write the
///         same key, so there is nothing to lock, nothing to retry on conflict, and no lease to renew or
///         leak. Everything else here follows from it.
///     </para>
///     <para>
///         <b>Reading is forward-only.</b> Object keys carry the hybrid logical clock in fixed-width hex,
///         so they sort in the order the operations happened, and a remembered key resumes exactly where
///         the last sync stopped. Peers are found with a grouped listing, so discovery costs one response
///         listing the peers rather than one listing every object they have ever written.
///     </para>
///     <para>
///         <b>Being offline is not an error.</b> Recording an operation never touches the network — it
///         applies locally and queues — so the app works the same with or without connectivity, and the
///         queue drains on the next sync. Only <see cref="SyncStatus.Pending" /> tells the user whether
///         anything would be lost by closing the tab, which is the question that actually matters.
///     </para>
///     <para>Not thread-safe by parallel design, but calls are serialised internally, so concurrent
///     <see cref="SyncAsync" /> calls queue rather than interleave.</para>
/// </remarks>
public sealed class SyncEngine
{
    private readonly HybridLogicalClock _clock;
    private readonly List<SyncConflict> _conflicts = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ISyncStore _local;
    private readonly IObjectStore _objects;
    private readonly SyncEngineOptions _options;
    private readonly List<SyncOp> _queue = [];
    private readonly SyncState _state;
    private bool _loaded;
    private Dictionary<string, string> _watermarks = new(StringComparer.Ordinal);

    /// <summary>Creates an engine for one device.</summary>
    /// <param name="objects">The shared bucket.</param>
    /// <param name="local">Where the pending queue and watermarks survive a reload.</param>
    /// <param name="clock">This device's clock. Its node id is also this client's prefix.</param>
    /// <param name="state">The merged state this engine maintains.</param>
    /// <param name="options">Bucket layout and clock.</param>
    public SyncEngine(
        IObjectStore objects,
        ISyncStore local,
        HybridLogicalClock clock,
        SyncState state,
        SyncEngineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(state);

        _objects = objects;
        _local = local;
        _clock = clock;
        _state = state;
        _options = options ?? new SyncEngineOptions();

        // The client id becomes a path segment. A '/' in it would silently write into — and read from —
        // somebody else's prefix, which is the one rule the whole design depends on.
        if (clock.NodeId.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A client id cannot contain '/' — it is used as a prefix in the bucket. Got '{clock.NodeId}'.",
                nameof(clock));
        }

        if (!_options.RootPrefix.EndsWith('/'))
        {
            throw new ArgumentException($"{nameof(SyncEngineOptions.RootPrefix)} must end with '/'.", nameof(options));
        }

        Status = new SyncStatus(SyncPhase.Idle, 0, 0, 0, null, null);
    }

    /// <summary>This device's identity, and the prefix it owns.</summary>
    public string ClientId => _clock.NodeId;

    /// <summary>Where sync stands right now.</summary>
    public SyncStatus Status { get; private set; }

    /// <summary>Everything merging has discarded since this engine started, newest last.</summary>
    public IReadOnlyList<SyncConflict> Conflicts => _conflicts;

    /// <summary>Raised whenever <see cref="Status" /> changes.</summary>
    public event Action<SyncStatus>? Changed;

    /// <summary>
    ///     Applies an operation locally and queues it for upload. Never touches the network, so it behaves
    ///     identically online and off.
    /// </summary>
    public async Task RecordAsync(SyncOp op, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(op);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            _conflicts.AddRange(_state.Apply(op));
            _queue.Add(op);
            await _local.WriteQueueAsync(_queue, cancellationToken).ConfigureAwait(false);

            Publish(Status with { Pending = _queue.Count, Conflicts = _conflicts.Count });
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Uploads anything queued, then reads what peers have written. </summary>
    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            Publish(Status with { Phase = SyncPhase.Syncing, Error = null });

            try
            {
                await PushCoreAsync(cancellationToken).ConfigureAwait(false);
                var peers = await PullCoreAsync(cancellationToken).ConfigureAwait(false);

                Publish(new SyncStatus(
                    SyncPhase.Idle, _queue.Count, _conflicts.Count, peers,
                    _options.TimeProvider.GetUtcNow(), null));
            }
            catch (HttpRequestException)
            {
                // Connectivity, not failure. The queue is intact and goes on the next attempt, so this is
                // reported as a state rather than an error.
                Publish(Status with { Phase = SyncPhase.Offline, Pending = _queue.Count, Error = null });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Publish(Status with { Phase = SyncPhase.Faulted, Pending = _queue.Count, Error = ex.Message });
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The key this engine would write its queue to — exposed for tests and diagnostics.</summary>
    internal string OwnPrefix => $"{_options.RootPrefix}{ClientId}/ops/";

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        _queue.AddRange(await _local.ReadQueueAsync(cancellationToken).ConfigureAwait(false));
        _watermarks = new Dictionary<string, string>(
            await _local.ReadWatermarksAsync(cancellationToken).ConfigureAwait(false), StringComparer.Ordinal);
        _loaded = true;

        // Queued operations were already applied when they were recorded, in a previous session whose
        // state is gone. Re-applying is free — replay is idempotent — and it is what makes the local view
        // correct again after a reload that happened before a sync.
        foreach (var op in _queue)
        {
            _state.Apply(op);
        }

        Publish(Status with { Pending = _queue.Count });
    }

    private async Task PushCoreAsync(CancellationToken cancellationToken)
    {
        if (_queue.Count == 0)
        {
            return;
        }

        var ordered = _queue.OrderBy(op => op.Stamp).ToArray();
        var key = $"{OwnPrefix}{ordered[0].Stamp}__{ordered[^1].Stamp}.json";
        var payload = JsonSerializer.SerializeToUtf8Bytes(ordered, SyncJsonContext.Default.SyncOpArray);

        await _objects.PutAsync(key, payload, cancellationToken).ConfigureAwait(false);

        // Cleared only after the upload succeeds. If it threw, the operations are still queued and the next
        // sync re-sends them — re-sending is harmless because applying twice changes nothing.
        _queue.Clear();
        await _local.WriteQueueAsync(_queue, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> PullCoreAsync(CancellationToken cancellationToken)
    {
        var prefixes = await _objects.ListPrefixesAsync(_options.RootPrefix, cancellationToken).ConfigureAwait(false);
        var peers = 0;
        var advanced = false;

        foreach (var prefix in prefixes)
        {
            var peerId = prefix[_options.RootPrefix.Length..].TrimEnd('/');
            if (peerId.Length == 0 || string.Equals(peerId, ClientId, StringComparison.Ordinal))
            {
                continue;
            }

            peers++;
            var opsPrefix = $"{prefix.TrimEnd('/')}/ops/";
            _watermarks.TryGetValue(opsPrefix, out var watermark);

            var entries = await _objects
                .ListAsync(opsPrefix, watermark, cancellationToken)
                .ConfigureAwait(false);

            foreach (var entry in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                await ApplyObjectAsync(entry.Key, cancellationToken).ConfigureAwait(false);

                // Advanced per object, not per peer: a failure part-way through leaves the watermark on the
                // last object fully applied, so the next sync resumes there instead of re-reading the batch.
                _watermarks[opsPrefix] = entry.Key;
                advanced = true;
            }
        }

        if (advanced)
        {
            await _local.WriteWatermarksAsync(_watermarks, cancellationToken).ConfigureAwait(false);
        }

        return peers;
    }

    private async Task ApplyObjectAsync(string key, CancellationToken cancellationToken)
    {
        await using var stream = await _objects.OpenReadAsync(key, cancellationToken).ConfigureAwait(false);
        if (stream is null)
        {
            // Listed but gone — compaction can remove an object between the listing and the read. Skipping
            // is safe: whatever it held is either already applied or still present in a compacted object.
            return;
        }

        var ops = await JsonSerializer
            .DeserializeAsync(stream, SyncJsonContext.Default.SyncOpArray, cancellationToken)
            .ConfigureAwait(false);

        if (ops is null)
        {
            return;
        }

        foreach (var op in ops)
        {
            // Advance this device's clock past everything it has seen, so anything it records afterwards
            // sorts after it. Without this a reply could carry an earlier stamp than what it replied to.
            _clock.Observe(op.Stamp);
            _conflicts.AddRange(_state.Apply(op));
        }
    }

    private void Publish(SyncStatus status)
    {
        Status = status;
        Changed?.Invoke(status);
    }
}
