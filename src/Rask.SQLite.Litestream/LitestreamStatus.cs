namespace Rask.SQLite.Litestream;

/// <summary>
/// A point-in-time reading of the managed Litestream supervisor.
/// </summary>
/// <param name="IsReplicating">
/// <c>true</c> while <c>litestream replicate</c> is running. This is the headline signal: continuous backup
/// only protects you while this is true.
/// </param>
/// <param name="LastStartedAt">When the current (or most recent) <c>replicate</c> run started, UTC.</param>
/// <param name="LastExitedAt">When the most recent run ended, UTC — <c>null</c> if it has never ended.</param>
/// <param name="RestartCount">
/// How many times the supervisor has restarted replication. Anything above zero means backups have been
/// interrupted at least once; a climbing value means they are flapping.
/// </param>
/// <param name="LastExitCode">The exit code of the most recent run, or <c>null</c> if it failed to launch.</param>
/// <param name="LastError">The failure message from the most recent run, or <c>null</c> if it exited cleanly.</param>
public sealed record LitestreamReplicationStatus(
    bool IsReplicating,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastExitedAt,
    int RestartCount,
    int? LastExitCode,
    string? LastError);

/// <summary>
/// Live state of the managed Litestream supervisor, published as a singleton so an operator surface can answer
/// "is my backup actually running?" — a question the logs alone can only answer by absence.
/// <para>
/// Registered by <see cref="LitestreamServiceCollectionExtensions.AddRaskSqliteLitestream"/>; resolve it and
/// read <see cref="Current"/>. Written only by the supervisor and published atomically, so a reader always sees
/// a coherent snapshot rather than a half-updated one.
/// </para>
/// </summary>
public sealed class LitestreamStatus
{
    private LitestreamReplicationStatus _current = new(false, null, null, 0, null, null);

    /// <summary>The latest snapshot. Never <c>null</c>; before the supervisor starts it reports not replicating.</summary>
    public LitestreamReplicationStatus Current => Volatile.Read(ref _current);

    // The supervisor is the single writer, so read-modify-write needs no CAS; the volatile write is what makes
    // the new snapshot visible to reader threads.
    internal void MarkStarted(DateTimeOffset at) =>
        Publish(_current with { IsReplicating = true, LastStartedAt = at });

    internal void MarkExited(DateTimeOffset at, int exitCode) =>
        Publish(_current with
        {
            IsReplicating = false,
            LastExitedAt = at,
            LastExitCode = exitCode,
            LastError = null,
            RestartCount = _current.RestartCount + 1,
        });

    internal void MarkFailed(DateTimeOffset at, string error) =>
        Publish(_current with
        {
            IsReplicating = false,
            LastExitedAt = at,
            LastExitCode = null,
            LastError = error,
            RestartCount = _current.RestartCount + 1,
        });

    internal void MarkStopped(DateTimeOffset at) =>
        Publish(_current with { IsReplicating = false, LastExitedAt = at });

    private void Publish(LitestreamReplicationStatus next) => Volatile.Write(ref _current, next);
}
