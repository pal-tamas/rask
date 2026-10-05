namespace Rask.Dashboard.Panels;

/// <summary>
/// Backup state for the system panel. The dashboard deliberately takes no dependency on
/// <c>Rask.SQLite.Litestream</c> or <c>Rask.SQLite.Snapshots</c>: those pull a native SQLitePCLRaw provider
/// bundle, and the dashboard itself is provider-agnostic — it reads EF entities and works just as well on
/// Postgres. Register an implementation to light up the backup tiles; without one they stay hidden.
/// <para>
/// The data it needs is public API: <c>LitestreamStatus.Current</c> and
/// <c>ISqliteSnapshotStore.List(ct)</c>.
/// </para>
/// </summary>
public interface IDashboardBackupProbe
{
    /// <summary>Continuous-replication state, or <c>null</c> if the app doesn't run any.</summary>
    Task<BackupReplicationInfo?> Replication(CancellationToken cancellationToken);

    /// <summary>Stored snapshots, newest first. Empty when the app takes none.</summary>
    Task<IReadOnlyList<BackupSnapshotInfo>> Snapshots(CancellationToken cancellationToken);

    /// <summary>
    /// Restore-verification state, or <c>null</c> when nothing has verified the backup — which is the
    /// default, since verification is opt-in and costs a real restore. The default implementation returns
    /// <c>null</c>, so an existing probe keeps compiling and simply shows no restorability tile.
    /// </summary>
    Task<BackupVerificationInfo?> Verification(CancellationToken cancellationToken) =>
        Task.FromResult<BackupVerificationInfo?>(null);
}
