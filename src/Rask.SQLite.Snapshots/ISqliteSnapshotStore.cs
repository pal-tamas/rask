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
}
