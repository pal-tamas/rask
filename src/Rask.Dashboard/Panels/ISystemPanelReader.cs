namespace Rask.Dashboard.Panels;

/// <summary>
/// The system reader, without the context type parameter — pages aren't generic, so they resolve this.
/// </summary>
public interface ISystemPanelReader
{
    /// <summary>Whether an <see cref="IDashboardBackupProbe"/> is registered, so the backup card can hide.</summary>
    bool HasBackupProbe { get; }

    /// <summary>Provider, SQLite pragmas where applicable, and database size.</summary>
    Task<DatabaseInfo> Database(CancellationToken cancellationToken);

    /// <summary>The declared recurring schedule joined to when each job last fired.</summary>
    Task<IReadOnlyList<RecurringJobRow>> RecurringJobs(CancellationToken cancellationToken);

    /// <summary>Continuous-replication state, or <c>null</c>.</summary>
    Task<BackupReplicationInfo?> Replication(CancellationToken cancellationToken);

    /// <summary>Stored snapshots, newest first.</summary>
    Task<IReadOnlyList<BackupSnapshotInfo>> Snapshots(CancellationToken cancellationToken);

    /// <summary>Restore-verification state, or <c>null</c> when nothing has verified the backup.</summary>
    Task<BackupVerificationInfo?> Verification(CancellationToken cancellationToken);
}
