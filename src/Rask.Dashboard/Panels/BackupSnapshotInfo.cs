namespace Rask.Dashboard.Panels;

/// <summary>One stored snapshot.</summary>
/// <param name="Name">The snapshot's name.</param>
/// <param name="SizeBytes">Its size on disk.</param>
/// <param name="CreatedAt">When it was taken (UTC).</param>
public sealed record BackupSnapshotInfo(string Name, long SizeBytes, DateTime CreatedAt);
