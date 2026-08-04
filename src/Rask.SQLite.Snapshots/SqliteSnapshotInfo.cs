namespace Rask.SQLite.Snapshots;

/// <summary>
/// One stored snapshot, as reported by <see cref="ISqliteSnapshotStore.ListAsync"/> — enough to answer
/// "when did we last capture a copy of the database, and how big was it?" without touching the files.
/// </summary>
/// <param name="Name">The snapshot's name, as passed to <see cref="ISqliteSnapshotStore.SaveAsync"/>.</param>
/// <param name="SizeBytes">The stored size in bytes.</param>
/// <param name="CreatedAt">When the snapshot was stored (UTC).</param>
public sealed record SqliteSnapshotInfo(string Name, long SizeBytes, DateTime CreatedAt);
