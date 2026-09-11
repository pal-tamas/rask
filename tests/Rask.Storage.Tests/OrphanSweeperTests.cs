namespace Rask.Storage.Tests;

[Collection(StorageDbCollection.Name)]
public sealed class OrphanSweeperTests
{
    [Fact]
    public async Task Bytes_with_no_row_are_removed_once_past_the_grace_period()
    {
        await using var harness = new StorageHarness();
        var kept = await harness.Files.SaveAsync(new MemoryStream(Samples.Png()), "kept.png");
        var orphan = WriteOrphan(harness, Guid.NewGuid());
        harness.Clock.Advance(TimeSpan.FromHours(25));

        var result = await harness.Sweeper.SweepAsync(default);

        Assert.Equal(1, result.Deleted);
        Assert.False(File.Exists(orphan));
        Assert.True(File.Exists(Path.Combine(harness.Root, kept.Key)));
    }

    [Fact]
    public async Task A_young_orphan_is_kept()
    {
        await using var harness = new StorageHarness();
        var orphan = WriteOrphan(harness, Guid.NewGuid());
        harness.Clock.Advance(TimeSpan.FromHours(1));

        var result = await harness.Sweeper.SweepAsync(default);

        Assert.Equal(0, result.Deleted);
        Assert.True(File.Exists(orphan));
    }

    [Fact]
    public async Task Objects_outside_the_key_layout_are_never_touched()
    {
        await using var harness = new StorageHarness();
        var foreign = Path.Combine(harness.Root, "backups", "app.db");
        Directory.CreateDirectory(Path.GetDirectoryName(foreign)!);
        await File.WriteAllBytesAsync(foreign, [1]);
        harness.Clock.Advance(TimeSpan.FromDays(30));

        await harness.Sweeper.SweepAsync(default);

        Assert.True(File.Exists(foreign));
    }

    [Fact]
    public async Task A_failed_database_query_deletes_nothing()
    {
        await using var harness = new StorageHarness(createSchema: false);
        var orphan = WriteOrphan(harness, Guid.NewGuid());
        harness.Clock.Advance(TimeSpan.FromHours(25));

        await Assert.ThrowsAnyAsync<Exception>(() => harness.Sweeper.SweepAsync(default));

        Assert.True(File.Exists(orphan));
    }

    [Fact]
    public async Task A_mass_deletion_trips_the_breaker_and_deletes_nothing()
    {
        await using var harness = new StorageHarness();
        var orphans = Enumerable.Range(0, OrphanSweeper<StorageDbContext>.BreakerFloor + 1)
            .Select(_ => WriteOrphan(harness, Guid.NewGuid()))
            .ToList();
        harness.Clock.Advance(TimeSpan.FromHours(25));

        var result = await harness.Sweeper.SweepAsync(default);

        Assert.True(result.Tripped);
        Assert.Equal(0, result.Deleted);
        Assert.All(orphans, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task Stale_spool_files_from_a_crashed_save_are_removed()
    {
        await using var harness = new StorageHarness();
        var spool = harness.Runtime.Backend.CreateSpoolPath();
        await File.WriteAllBytesAsync(spool, [1, 2, 3]);
        File.SetLastWriteTimeUtc(spool, DateTime.UtcNow.AddHours(-2));
        harness.Clock.Advance(TimeSpan.FromHours(25));

        await harness.Sweeper.SweepAsync(default);

        Assert.False(File.Exists(spool));
    }

    [Fact]
    public async Task The_sweep_stays_under_its_prefix()
    {
        await using var harness = new StorageHarness(o => o.Prefix = "app");
        var outside = Path.Combine(harness.Root, KeyLayout.KeyOf("", Guid.NewGuid()));
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        await File.WriteAllBytesAsync(outside, [1]);
        var inside = WriteOrphan(harness, Guid.NewGuid());
        harness.Clock.Advance(TimeSpan.FromHours(25));

        var result = await harness.Sweeper.SweepAsync(default);

        Assert.Equal(1, result.Deleted);
        Assert.False(File.Exists(inside));
        Assert.True(File.Exists(outside));
    }

    private static string WriteOrphan(StorageHarness harness, Guid id)
    {
        var path = Path.Combine(harness.Root, KeyLayout.KeyOf(harness.Runtime.Options.Prefix, id));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }
}
