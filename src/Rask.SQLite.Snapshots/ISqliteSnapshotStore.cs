namespace Rask.SQLite.Snapshots;

/// <summary>
/// Persists completed snapshot files and enforces retention. The default is
/// <see cref="DirectorySnapshotStore"/> (a local directory); register your own implementation before
/// <see cref="SqliteSnapshotsServiceCollectionExtensions.AddRaskSqliteSnapshots"/> to send snapshots
/// elsewhere (e.g. object storage).
/// </summary>
public interface ISqliteSnapshotStore
{
    /// <summary>
    /// Stores the completed snapshot at <paramref name="sourceFilePath"/> under
    /// <paramref name="snapshotName"/> (move, copy, or upload). The source file may be consumed.
    /// </summary>
    Task SaveAsync(string sourceFilePath, string snapshotName, CancellationToken cancellationToken);

    /// <summary>Keeps the <paramref name="retain"/> most recent snapshots and removes the rest.</summary>
    Task PruneAsync(int retain, CancellationToken cancellationToken);

    /// <summary>
    /// The snapshots this store currently holds, newest first — what an operator or an ops dashboard reads to
    /// confirm that backups are actually being taken.
    /// <para>
    /// The default returns an empty list, so a store written before this method existed still compiles. Override
    /// it if your store can enumerate: callers cannot tell "no snapshots yet" from "this store doesn't list".
    /// </para>
    /// </summary>
    Task<IReadOnlyList<SqliteSnapshotInfo>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SqliteSnapshotInfo>>([]);
}
