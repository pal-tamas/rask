namespace Rask.Dashboard.Panels;

/// <summary>
/// The system reader, without the context type parameter — pages aren't generic, so they resolve this.
/// </summary>
public interface ISystemPanelReader
{
    /// <summary>Whether an <see cref="IDashboardBackupProbe"/> is registered, so the backup card can hide.</summary>
    bool HasBackupProbe { get; }

    /// <summary>Provider, SQLite pragmas where applicable, and database size.</summary>
    Task<DatabaseInfo> DatabaseAsync(CancellationToken cancellationToken);

    /// <summary>The declared recurring schedule joined to when each job last fired.</summary>
    Task<IReadOnlyList<RecurringJobRow>> RecurringJobsAsync(CancellationToken cancellationToken);

    /// <summary>Continuous-replication state, or <c>null</c>.</summary>
    Task<BackupReplicationInfo?> ReplicationAsync(CancellationToken cancellationToken);

    /// <summary>Stored snapshots, newest first.</summary>
    Task<IReadOnlyList<BackupSnapshotInfo>> SnapshotsAsync(CancellationToken cancellationToken);

    /// <summary>Restore-verification state, or <c>null</c> when nothing has verified the backup.</summary>
    Task<BackupVerificationInfo?> VerificationAsync(CancellationToken cancellationToken);
}
