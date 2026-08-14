namespace Rask.SQLite.Crdt.Sync;

/// <summary>How the last sync went, in the terms a UI actually needs.</summary>
/// <remarks>
///     There is deliberately no conflict count. Merging is per column and automatic, so unlike a
///     last-writer-wins op log there is nothing a user could be asked to resolve and nothing that was
///     silently discarded on their behalf. Reporting a conflict here would be reporting a decision that
///     was never made.
/// </remarks>
/// <param name="Phase">Whether the last attempt reached the bucket.</param>
/// <param name="Published">Changes uploaded by the last sync.</param>
/// <param name="Received">Changes applied from peers by the last sync.</param>
/// <param name="Peers">Peers seen in the bucket, this replica excluded.</param>
/// <param name="Error">Why the last attempt failed, when it did.</param>
public sealed record CrdtSyncStatus(
    CrdtSyncPhase Phase,
    int Published,
    int Received,
    int Peers,
    string? Error = null);

/// <summary>Whether the last sync reached the bucket.</summary>
public enum CrdtSyncPhase
{
    /// <summary>Nothing has been attempted yet.</summary>
    Idle = 0,

    /// <summary>A sync is running.</summary>
    Syncing = 1,

    /// <summary>The last sync completed; this replica and the bucket agree.</summary>
    Synced = 2,

    /// <summary>
    ///     The bucket could not be reached. Deliberately not a failure: local edits are already committed
    ///     to SQLite and the next sync sends them, so showing this as an error trains people to ignore it.
    /// </summary>
    Offline = 3,
}
