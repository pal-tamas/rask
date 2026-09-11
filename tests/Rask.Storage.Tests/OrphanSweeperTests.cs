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
        await harness.Files.SaveAsync(new MemoryStream(Samples.Png()), "kept.png");
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
    public async Task An_empty_table_deletes_nothing()
    {
        // An app pointed at a fresh database sees every object as an orphan; the numbers alone would not stop it
        // when the store holds 100 objects or fewer.
        await using var harness = new StorageHarness();
        var orphan = WriteOrphan(harness, Guid.NewGuid());
        harness.Clock.Advance(TimeSpan.FromHours(25));

        var result = await harness.Sweeper.SweepAsync(default);

        Assert.True(result.Tripped);
        Assert.Equal(0, result.Deleted);
        Assert.True(File.Exists(orphan));
    }

    [Fact]
    public async Task A_mass_deletion_trips_the_breaker_and_deletes_nothing()
    {
        await using var harness = new StorageHarness();
        await harness.Files.SaveAsync(new MemoryStream(Samples.Png()), "kept.png");
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
    public async Task Stale_spool_files_are_removed_even_when_the_sweep_deletes_nothing_else()
    {
        await using var harness = new StorageHarness();
        var spool = harness.Runtime.Backend.CreateSpoolPath();
        await File.WriteAllBytesAsync(spool, [1, 2, 3]);
        File.SetLastWriteTimeUtc(spool, DateTime.UtcNow.AddHours(-2));
        WriteOrphan(harness, Guid.NewGuid()); // with an empty table, the sweep itself refuses
        harness.Clock.Advance(TimeSpan.FromHours(25));

        var result = await harness.Sweeper.SweepAsync(default);

        Assert.True(result.Tripped);
        Assert.False(File.Exists(spool));
    }

    [Fact]
    public async Task The_sweep_stays_under_its_prefix()
    {
        await using var harness = new StorageHarness(o => o.Prefix = "app");
        await harness.Files.SaveAsync(new MemoryStream(Samples.Png()), "kept.png");
        var outside = Path.Combine(harness.Root, KeyLayout.KeyOf("", Guid.NewGuid(), isPublic: false));
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        await File.WriteAllBytesAsync(outside, [1]);
        var inside = WriteOrphan(harness, Guid.NewGuid());
        harness.Clock.Advance(TimeSpan.FromHours(25));

        var result = await harness.Sweeper.SweepAsync(default);

        Assert.Equal(1, result.Deleted);
        Assert.False(File.Exists(inside));
        Assert.True(File.Exists(outside));
    }

    [Theory]
    [InlineData(StorageProvider.Disk, "", true)]
    [InlineData(StorageProvider.Disk, "app/", true)]
    [InlineData(StorageProvider.S3, "", false)]
    [InlineData(StorageProvider.S3, "app/", true)]
    [InlineData(StorageProvider.Azure, "", false)]
    [InlineData(StorageProvider.Azure, "app/", true)]
    public void A_bucket_needs_a_prefix_before_the_sweep_deletes_from_it(StorageProvider provider, string prefix, bool mayDelete) =>
        Assert.Equal(mayDelete, SweepPolicy.MayDelete(provider, prefix));

    private static string WriteOrphan(StorageHarness harness, Guid id)
    {
        var path = Path.Combine(harness.Root, KeyLayout.KeyOf(harness.Runtime.Options.Prefix, id, isPublic: false));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }
}
