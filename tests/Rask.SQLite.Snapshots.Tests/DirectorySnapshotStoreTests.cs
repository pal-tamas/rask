namespace Rask.SQLite.Snapshots.Tests;

public sealed class DirectorySnapshotStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rask-snap-store-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAsync_moves_the_snapshot_into_the_directory()
    {
        var source = Path.Combine(Path.GetTempPath(), $"rask-snap-src-{Guid.NewGuid():N}.db");
        await File.WriteAllTextAsync(source, "snapshot");
        var store = new DirectorySnapshotStore(_dir, "app-*.db");

        await store.SaveAsync(source, "app-1.db", CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_dir, "app-1.db")));
        Assert.False(File.Exists(source));   // moved, not copied
    }

    [Fact]
    public async Task PruneAsync_keeps_only_the_newest_retained()
    {
        Directory.CreateDirectory(_dir);
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 5; i++)
        {
            var path = Path.Combine(_dir, $"app-{i}.db");
            await File.WriteAllTextAsync(path, "x");
            File.SetLastWriteTimeUtc(path, baseTime.AddMinutes(i));   // app-4 is newest
        }

        // A file that does not match the pattern must be left untouched.
        var unrelated = Path.Combine(_dir, "unrelated.txt");
        await File.WriteAllTextAsync(unrelated, "keep me");

        var store = new DirectorySnapshotStore(_dir, "app-*.db");
        await store.PruneAsync(retain: 2, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_dir, "app-4.db")));
        Assert.True(File.Exists(Path.Combine(_dir, "app-3.db")));
        Assert.False(File.Exists(Path.Combine(_dir, "app-2.db")));
        Assert.False(File.Exists(Path.Combine(_dir, "app-0.db")));
        Assert.True(File.Exists(unrelated));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
