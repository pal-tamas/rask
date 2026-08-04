namespace Rask.SQLite.Snapshots;

/// <summary>
/// Default <see cref="ISqliteSnapshotStore"/>: writes snapshots into a local directory and prunes the
/// oldest by last-write time. Retention is scoped to files matching this store's search pattern, so a
/// dedicated snapshots directory won't disturb unrelated files.
/// </summary>
public sealed class DirectorySnapshotStore : ISqliteSnapshotStore
{
    private readonly string _directory;
    private readonly string _searchPattern;

    /// <summary>Creates a store over <paramref name="directory"/> managing files matching <paramref name="searchPattern"/>.</summary>
    public DirectorySnapshotStore(string directory, string searchPattern = "*.db")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);
        _directory = directory;
        _searchPattern = searchPattern;
    }

    /// <inheritdoc/>
    public Task SaveAsync(string sourceFilePath, string snapshotName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotName);

        Directory.CreateDirectory(_directory);
        var destination = Path.Combine(_directory, snapshotName);
        File.Move(sourceFilePath, destination, overwrite: true);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SqliteSnapshotInfo>> ListAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_directory))
        {
            return Task.FromResult<IReadOnlyList<SqliteSnapshotInfo>>([]);
        }

        // Ordered by the same key PruneAsync deletes by, so "the ones you can see" and "the ones that survive
        // retention" are the same list in the same order.
        var snapshots = new DirectoryInfo(_directory)
            .EnumerateFiles(_searchPattern)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new SqliteSnapshotInfo(f.Name, f.Length, f.LastWriteTimeUtc))
            .ToArray();

        return Task.FromResult<IReadOnlyList<SqliteSnapshotInfo>>(snapshots);
    }

    /// <inheritdoc/>
    public Task PruneAsync(int retain, CancellationToken cancellationToken)
    {
        if (retain < 1 || !Directory.Exists(_directory))
        {
            return Task.CompletedTask;
        }

        var stale = new DirectoryInfo(_directory)
            .EnumerateFiles(_searchPattern)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(retain);

        foreach (var file in stale)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
                // A snapshot being read/copied right now will be pruned on the next run — not fatal.
            }
        }

        return Task.CompletedTask;
    }
}
