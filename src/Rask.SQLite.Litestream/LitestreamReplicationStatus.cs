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
