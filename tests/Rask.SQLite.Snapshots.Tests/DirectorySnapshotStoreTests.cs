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
    public async Task ListAsync_reports_matching_snapshots_newest_first()
    {
        Directory.CreateDirectory(_dir);
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 3; i++)
        {
            var path = Path.Combine(_dir, $"app-{i}.db");
            await File.WriteAllTextAsync(path, new string('x', i + 1));
            File.SetLastWriteTimeUtc(path, baseTime.AddMinutes(i));   // app-2 is newest
        }

        // Same pattern scoping as PruneAsync: what you can see is what retention manages.
        await File.WriteAllTextAsync(Path.Combine(_dir, "unrelated.txt"), "ignore me");

        var store = new DirectorySnapshotStore(_dir, "app-*.db");
        var snapshots = await store.ListAsync(CancellationToken.None);

        Assert.Equal(["app-2.db", "app-1.db", "app-0.db"], snapshots.Select(s => s.Name));
        Assert.Equal(3, snapshots[0].SizeBytes);
        Assert.Equal(baseTime.AddMinutes(2), snapshots[0].CreatedAt);
    }

    [Fact]
    public async Task ListAsync_is_empty_when_the_directory_does_not_exist()
    {
        var store = new DirectorySnapshotStore(_dir, "app-*.db");
        Assert.Empty(await store.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_defaults_to_empty_for_a_store_that_does_not_implement_it()
    {
        // The default interface method keeps stores written before ListAsync existed compiling.
        ISqliteSnapshotStore store = new NonListingStore();
        Assert.Empty(await store.ListAsync(CancellationToken.None));
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

    // A custom store of the shape that existed before ListAsync was added — it must still compile.
    private sealed class NonListingStore : ISqliteSnapshotStore
    {
        public Task SaveAsync(string sourceFilePath, string snapshotName, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task PruneAsync(int retain, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
