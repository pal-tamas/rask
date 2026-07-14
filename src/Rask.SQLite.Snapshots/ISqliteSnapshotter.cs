namespace Rask.SQLite.Snapshots;

/// <summary>
/// Takes a single consistent snapshot of the configured SQLite database on demand — the same operation
/// the scheduled background service runs. Inject it to trigger a backup yourself (e.g. before a risky
/// migration).
/// </summary>
public interface ISqliteSnapshotter
{
    /// <summary>
    /// Creates one consistent snapshot via SQLite's Online Backup API, hands it to the configured
    /// <see cref="ISqliteSnapshotStore"/>, prunes old snapshots, and returns the snapshot's name.
    /// </summary>
    Task<string> SnapshotAsync(CancellationToken cancellationToken = default);
}
