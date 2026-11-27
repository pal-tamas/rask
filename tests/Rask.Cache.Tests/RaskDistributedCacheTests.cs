using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Rask.Caching.Tests;

public sealed class RaskDistributedCacheTests
{
    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static string Str(byte[] b) => Encoding.UTF8.GetString(b);

    [Fact]
    public async Task Set_then_get_round_trips_the_value()
    {
        await using var harness = new CacheHarness();
        await harness.Distributed.SetAsync("k", Bytes("hello"), new DistributedCacheEntryOptions(), TestContext.Current.CancellationToken);

        var got = await harness.Distributed.GetAsync("k", TestContext.Current.CancellationToken);

        Assert.NotNull(got);
        Assert.Equal("hello", Str(got));
    }

    [Fact]
    public async Task Deadlines_either_side_of_the_autumn_hour_expire_as_the_utc_instants_they_are()
    {
        // In Budapest both deadlines read 02:30 on the wall: only their instants tell them apart, and 01:00Z falls between.
        using var zone = AmbientClock.UseZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest"));
        await using var harness = new CacheHarness(start: new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero));
        await harness.Distributed.SetAsync("earlier", Bytes("a"),
            new DistributedCacheEntryOptions { AbsoluteExpiration = new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero) },
            TestContext.Current.CancellationToken);
        await harness.Distributed.SetAsync("later", Bytes("b"),
            new DistributedCacheEntryOptions { AbsoluteExpiration = new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero) },
            TestContext.Current.CancellationToken);
        await harness.Distributed.SetAsync("kept", Bytes("c"), new DistributedCacheEntryOptions(), TestContext.Current.CancellationToken);

        harness.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Null(await harness.Distributed.GetAsync("earlier", TestContext.Current.CancellationToken));
        Assert.Equal("b", Str((await harness.Distributed.GetAsync("later", TestContext.Current.CancellationToken))!));
        Assert.Equal("c", Str((await harness.Distributed.GetAsync("kept", TestContext.Current.CancellationToken))!));
        await using var db = harness.NewContext();
        var later = await db.Set<CacheEntry>().SingleAsync(e => e.Key == "later", TestContext.Current.CancellationToken);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero), later.ExpiresAt.Instant);
        Assert.Equal(later.ExpiresAt, later.AbsoluteExpiration);
        Assert.Equal(CacheEntry.Never, (await db.Set<CacheEntry>().SingleAsync(e => e.Key == "kept", TestContext.Current.CancellationToken)).ExpiresAt);
        var stored = await db.Database
            .SqlQueryRaw<string>("SELECT \"ExpiresAt\" AS \"Value\" FROM \"CacheEntry\" ORDER BY \"ExpiresAt\"")
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["2026-10-25 01:30:00", "9999-12-31 23:59:59.9999999"], stored);
    }

    [Fact]
    public async Task Get_returns_null_for_a_missing_key()
    {
        await using var harness = new CacheHarness();

        Assert.Null(await harness.Distributed.GetAsync("absent", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Set_overwrites_an_existing_key()
    {
        await using var harness = new CacheHarness();

        await harness.Distributed.SetAsync("k", Bytes("one"), new DistributedCacheEntryOptions(), TestContext.Current.CancellationToken);
        await harness.Distributed.SetAsync("k", Bytes("two"), new DistributedCacheEntryOptions(), TestContext.Current.CancellationToken);

        Assert.Equal("two", Str((await harness.Distributed.GetAsync("k", TestContext.Current.CancellationToken))!));
        Assert.Equal(1, await harness.CountEntriesAsync());
    }

    [Fact]
    public async Task Absolute_expiration_makes_a_read_a_miss_and_evicts_lazily()
    {
        await using var harness = new CacheHarness();
        await harness.Distributed.SetAsync("k", Bytes("v"),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) }, TestContext.Current.CancellationToken);

        harness.Clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Null(await harness.Distributed.GetAsync("k", TestContext.Current.CancellationToken));
        Assert.Equal(0, await harness.CountEntriesAsync()); // lazily evicted on the read
    }

    [Fact]
    public async Task Sliding_expiration_is_renewed_on_each_read()
    {
        await using var harness = new CacheHarness();
        await harness.Distributed.SetAsync("k", Bytes("v"),
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) }, TestContext.Current.CancellationToken);

        harness.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.NotNull(await harness.Distributed.GetAsync("k", TestContext.Current.CancellationToken)); // renews to now+10

        harness.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.NotNull(await harness.Distributed.GetAsync("k", TestContext.Current.CancellationToken)); // still fresh only because the read renewed it

        harness.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Null(await harness.Distributed.GetAsync("k", TestContext.Current.CancellationToken)); // no read within the window → expired
    }

    [Fact]
    public async Task Sliding_expiration_is_capped_by_the_absolute_deadline()
    {
        await using var harness = new CacheHarness();
        await harness.Distributed.SetAsync("k", Bytes("v"), new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15),
        }, TestContext.Current.CancellationToken);

        // Keep reading within the sliding window; the absolute cap must still expire the entry.
        harness.Clock.Advance(TimeSpan.FromMinutes(8));
        Assert.NotNull(await harness.Distributed.GetAsync("k", TestContext.Current.CancellationToken));

        harness.Clock.Advance(TimeSpan.FromMinutes(8)); // t16 > absolute t15
        Assert.Null(await harness.Distributed.GetAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Refresh_renews_a_sliding_entry_without_returning_it()
    {
        await using var harness = new CacheHarness();
        await harness.Distributed.SetAsync("k", Bytes("v"),
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) }, TestContext.Current.CancellationToken);

        harness.Clock.Advance(TimeSpan.FromMinutes(6));
        await harness.Distributed.RefreshAsync("k", TestContext.Current.CancellationToken); // renews to now+10

        harness.Clock.Advance(TimeSpan.FromMinutes(8)); // t14 < renewed t16
        Assert.NotNull(await harness.Distributed.GetAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Remove_deletes_the_entry()
    {
        await using var harness = new CacheHarness();
        await harness.Distributed.SetAsync("k", Bytes("v"), new DistributedCacheEntryOptions(), TestContext.Current.CancellationToken);

        await harness.Distributed.RemoveAsync("k", TestContext.Current.CancellationToken);

        Assert.Null(await harness.Distributed.GetAsync("k", TestContext.Current.CancellationToken));
        Assert.Equal(0, await harness.CountEntriesAsync());
    }

    [Fact]
    public async Task An_entry_with_no_expiration_survives_a_long_wait()
    {
        await using var harness = new CacheHarness();
        await harness.Distributed.SetAsync("k", Bytes("v"), new DistributedCacheEntryOptions(), TestContext.Current.CancellationToken);

        harness.Clock.Advance(TimeSpan.FromDays(3650));

        Assert.NotNull(await harness.Distributed.GetAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Sync_and_async_apis_are_interchangeable()
    {
        await using var harness = new CacheHarness();
        var cache = harness.Distributed;

        cache.Set("k", Bytes("sync"), new DistributedCacheEntryOptions());
        Assert.Equal("sync", Str((await cache.GetAsync("k", TestContext.Current.CancellationToken))!));

        await cache.SetAsync("k", Bytes("async"), new DistributedCacheEntryOptions(), TestContext.Current.CancellationToken);
        Assert.Equal("async", Str(cache.Get("k")!));

        cache.Remove("k");
        Assert.Null(cache.Get("k"));
    }

    [Fact]
    public async Task DefaultSlidingExpiration_applies_when_no_options_are_given()
    {
        await using var harness = new CacheHarness(o => o.DefaultSlidingExpiration = TimeSpan.FromMinutes(5));
        await harness.Distributed.SetAsync("k", Bytes("v"), new DistributedCacheEntryOptions(), TestContext.Current.CancellationToken);

        harness.Clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Null(await harness.Distributed.GetAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DefaultSlidingExpiration_does_not_shorten_an_explicit_absolute_deadline()
    {
        await using var harness = new CacheHarness(o => o.DefaultSlidingExpiration = TimeSpan.FromMinutes(1));
        await harness.Distributed.SetAsync("k", Bytes("v"),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) }, TestContext.Current.CancellationToken);

        // Past the 1-minute default sliding window, but well within the explicit 30-minute absolute deadline:
        // the caller's deadline must win, so the entry is still there.
        harness.Clock.Advance(TimeSpan.FromMinutes(5));

        Assert.NotNull(await harness.Distributed.GetAsync("k", TestContext.Current.CancellationToken));
    }
}
