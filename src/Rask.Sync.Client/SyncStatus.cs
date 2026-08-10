namespace Rask.Sync.Client;

/// <summary>What the engine is doing, and whether the user's edits have left the device.</summary>
public enum SyncPhase
{
    /// <summary>Nothing in flight. Combined with <see cref="SyncStatus.Pending" /> of zero, everything is uploaded.</summary>
    Idle,

    /// <summary>A sync is running.</summary>
    Syncing,

    /// <summary>The last attempt could not reach the store. Edits are queued and will go on the next sync.</summary>
    Offline,

    /// <summary>The last attempt failed for a reason that is not connectivity — see <see cref="SyncStatus.Error" />.</summary>
    Faulted,
}

/// <summary>
///     A snapshot of where sync stands, for a UI to render.
/// </summary>
/// <remarks>
///     <para>
///         The two numbers people actually need are <see cref="Pending" /> and <see cref="Conflicts" />.
///         Pending answers "if I close this tab now, do I lose anything" — the question every offline-first
///         app has to answer honestly and most answer with a spinner. Conflicts answers "did syncing throw
///         away something I typed", which nothing can answer unless the engine keeps count.
///     </para>
///     <para>
///         <see cref="SyncPhase.Offline" /> is deliberately not an error. Being offline is the normal
///         operating mode of the thing this exists for, and showing it as a failure trains people to
///         ignore it.
///     </para>
/// </remarks>
/// <param name="Phase">What the engine is doing.</param>
/// <param name="Pending">Operations recorded locally but not yet uploaded.</param>
/// <param name="Conflicts">Merges that discarded another device's value since the engine started.</param>
/// <param name="Peers">Other clients seen in the bucket at the last sync.</param>
/// <param name="LastSyncedAt">When a sync last completed, or <c>null</c> if none has.</param>
/// <param name="Error">Why the last attempt faulted, or <c>null</c>.</param>
public sealed record SyncStatus(
    SyncPhase Phase,
    int Pending,
    int Conflicts,
    int Peers,
    DateTimeOffset? LastSyncedAt,
    string? Error)
{
    /// <summary>Nothing is waiting to be uploaded and the last attempt succeeded.</summary>
    public bool IsUpToDate => Phase == SyncPhase.Idle && Pending == 0;

    /// <summary>Work exists that has not left this device.</summary>
    public bool HasUnsyncedWork => Pending > 0;
}
