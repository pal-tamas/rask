namespace Rask.Sync.Client;

/// <summary>
///     Where a client keeps the two pieces of state it must not lose across a reload: operations it has
///     recorded but not yet uploaded, and how far it has read each peer.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately tiny, and deliberately not tied to a browser. Losing the queue loses a user's
///         offline edits outright; losing the watermarks costs only re-reading objects, because replay is
///         idempotent. That asymmetry is worth knowing when choosing an implementation — the queue is the
///         part that has to be durable.
///     </para>
///     <para>
///         An OPFS-backed implementation belongs in an app or sample rather than here: <c>Rask.Core</c> is
///         not a published package, so a package that depends on it cannot be restored from a feed.
///         <see cref="InMemorySyncStore" /> ships for tests and for a server-side replica.
///     </para>
/// </remarks>
public interface ISyncStore
{
    /// <summary>Operations recorded locally and not yet uploaded, oldest first.</summary>
    ValueTask<IReadOnlyList<SyncOp>> ReadQueueAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the pending queue. Called after every record and after a successful upload.</summary>
    ValueTask WriteQueueAsync(IReadOnlyList<SyncOp> queue, CancellationToken cancellationToken = default);

    /// <summary>The last object key read from each peer, keyed by that peer's prefix.</summary>
    ValueTask<IReadOnlyDictionary<string, string>> ReadWatermarksAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the watermarks.</summary>
    ValueTask WriteWatermarksAsync(
        IReadOnlyDictionary<string, string> watermarks, CancellationToken cancellationToken = default);
}

/// <summary>
///     An <see cref="ISyncStore" /> that keeps everything in memory. Loses the queue when the process
///     ends, so it is for tests and for a replica that can afford to re-fetch — not for a client holding a
///     user's offline edits.
/// </summary>
public sealed class InMemorySyncStore : ISyncStore
{
    private List<SyncOp> _queue = [];
    private Dictionary<string, string> _watermarks = [];

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<SyncOp>> ReadQueueAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<SyncOp>>([.. _queue]);

    /// <inheritdoc />
    public ValueTask WriteQueueAsync(IReadOnlyList<SyncOp> queue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queue);
        _queue = [.. queue];
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, string>> ReadWatermarksAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(_watermarks, StringComparer.Ordinal));

    /// <inheritdoc />
    public ValueTask WriteWatermarksAsync(
        IReadOnlyDictionary<string, string> watermarks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(watermarks);
        _watermarks = new Dictionary<string, string>(watermarks, StringComparer.Ordinal);
        return ValueTask.CompletedTask;
    }
}
