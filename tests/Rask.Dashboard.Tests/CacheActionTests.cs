using Microsoft.EntityFrameworkCore;
using Rask.Cache;

namespace Rask.Dashboard.Tests;

/// <summary>
/// Cache actions are correctness-safe by nature — a miss is a recompute, not a lost fact — so what these
/// pin is scope: evicting one key must not take the others with it.
/// </summary>
public sealed class CacheActionTests
{
    [Fact]
    public async Task Evict_removes_exactly_one_key()
    {
        await using var h = new DashboardHarness(Batteries.Cache);
        await SeedAsync(h, "a", "b", "c");

        Assert.Equal(1, await h.Get<ICachePanelReader>().EvictAsync("b", CancellationToken.None));

        await using var db = h.NewContext();
        Assert.Equal(["a", "c"], await db.Set<CacheEntry>().Select(e => e.Key).OrderBy(k => k).ToListAsync());
    }

    [Fact]
    public async Task Evicting_a_missing_key_reports_zero_rather_than_throwing()
    {
        await using var h = new DashboardHarness(Batteries.Cache);
        Assert.Equal(0, await h.Get<ICachePanelReader>().EvictAsync("nope", CancellationToken.None));
    }

    [Fact]
    public async Task Flush_drops_everything_and_reports_the_count()
    {
        await using var h = new DashboardHarness(Batteries.Cache);
        await SeedAsync(h, "a", "b", "c");

        Assert.Equal(3, await h.Get<ICachePanelReader>().FlushAsync(CancellationToken.None));
        Assert.Equal(0, (await h.Get<ICachePanelReader>().StatsAsync(CancellationToken.None)).Entries);
    }

    [Fact]
    public async Task Stats_report_entries_bytes_and_the_expired_backlog()
    {
        await using var h = new DashboardHarness(Batteries.Cache);
        var now = h.Clock.GetUtcNow().UtcDateTime;

        await using (var db = h.NewContext())
        {
            db.Set<CacheEntry>().AddRange(
                new CacheEntry { Key = "fresh", Value = new byte[10], ExpiresAt = now.AddHours(1), CreatedAt = now },
                // Past its expiry but still a row: the purge sweep runs on its own schedule, and the gap
                // between "stops being served" and "actually deleted" is worth showing.
                new CacheEntry { Key = "stale", Value = new byte[5], ExpiresAt = now.AddHours(-1), CreatedAt = now });
            await db.SaveChangesAsync();
        }

        var stats = await h.Get<ICachePanelReader>().StatsAsync(CancellationToken.None);

        Assert.Equal(2, stats.Entries);
        Assert.Equal(15, stats.Bytes);
        Assert.Equal(1, stats.Expired);
    }

    [Fact]
    public async Task An_empty_cache_reports_zero_bytes_rather_than_failing()
    {
        // SUM over no rows is NULL in SQL; a plain Sum() would throw on the nullable-to-long conversion.
        await using var h = new DashboardHarness(Batteries.Cache);
        Assert.Equal(0, (await h.Get<ICachePanelReader>().StatsAsync(CancellationToken.None)).Bytes);
    }

    [Fact]
    public async Task Actions_on_an_unmapped_cache_are_no_ops()
    {
        await using var h = new DashboardHarness(registered: Batteries.All, mapped: Batteries.Jobs);
        var cache = h.Get<ICachePanelReader>();

        Assert.False(cache.IsAvailable);
        Assert.Equal(0, await cache.EvictAsync("a", CancellationToken.None));
        Assert.Equal(0, await cache.FlushAsync(CancellationToken.None));
    }

    private static async Task SeedAsync(DashboardHarness harness, params string[] keys)
    {
        var now = harness.Clock.GetUtcNow().UtcDateTime;
        await using var db = harness.NewContext();
        db.Set<CacheEntry>().AddRange(keys.Select(k => new CacheEntry
        {
            Key = k,
            Value = [1, 2, 3],
            ExpiresAt = now.AddHours(1),
            CreatedAt = now,
        }));
        await db.SaveChangesAsync();
    }
}
