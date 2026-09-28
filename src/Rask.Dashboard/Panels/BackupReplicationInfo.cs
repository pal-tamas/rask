namespace Rask.Dashboard.Panels;

/// <summary>Continuous-backup liveness, as the dashboard displays it.</summary>
/// <param name="IsReplicating">Whether replication is running right now.</param>
/// <param name="LastStartedAt">When the current or most recent run started.</param>
/// <param name="RestartCount">How many times it has restarted — climbing means flapping.</param>
/// <param name="LastError">The most recent failure, if any.</param>
public sealed record BackupReplicationInfo(
    bool IsReplicating, DateTimeOffset? LastStartedAt, int RestartCount, string? LastError);
