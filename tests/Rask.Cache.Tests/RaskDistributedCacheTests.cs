using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Rask.Cache.Tests;

public sealed class RaskDistributedCacheTests
{
    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static string Str(byte[] b) => Encoding.UTF8.GetString(b);

    [Fact]
    public async Task Set_then_get_round_trips_the_value()
    {
        await using var harness = new CacheHarness();
        await harness.Distributed.SetAsync("k", Bytes("hello"), new DistributedCacheEntryOptions());

        var got = await harness.Distributed.GetAsync("k");

        Assert.NotNull(got);
        Assert.Equal("hello", Str(got));
    }

    [Fact]
    public async Task Get_returns_null_for_a_missing_key()
    {
        await using var harness = new CacheHarness();
        Assert.Null(await harness.Distributed.GetAsync("absent"));
    }

    [Fact]
    public async Task Set_overwrites_an_existing_key()
    {
        await using var harness = new CacheHarness();
        await harness.Distributed.SetAsync("k", Bytes("one"), new DistributedCacheEntryOptions());
        await harness.Distributed.SetAsync("k", Bytes("two"), new DistributedCacheEntryOptions());

        Assert.Equal("two", Str((await harness.Distributed.GetAsync("k"))!));
        Assert.Equal(1, await harness.CountEntriesAsync());
    }

    [Fact]
    public async Task Absolute_expiration_makes_a_read_a_miss_and_evicts_lazily()
    {
        await using var harness = new CacheHarness();
        await harness.Distributed.SetAsync("k", Bytes("v"),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });

        harness.Clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Null(await harness.Distributed.GetAsync("k"));
        Assert.Equal(0, await harness.CountEntriesAsync()); // lazily evicted on the read
    }

    [Fact]
    public async Task Sliding_expiration_is_renewed_on_each_read()
    {
        await using var harness = new CacheHarness();
        await harness.Distributed.SetAsync("k", Bytes("v"),
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) });

        harness.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.NotNull(await harness.Distributed.GetAsync("k")); // renews to now+10

        harness.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.NotNull(await harness.Distributed.GetAsync("k")); // still fresh only because the read renewed it

        harness.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Null(await harness.Distributed.GetAsync("k")); // no read within the window → expired
    }

    [Fact]
    public async Task Sliding_expiration_is_capped_by_the_absolute_deadline()
    {
        await using var harness = new CacheHarness();
        await harness.Distributed.SetAsync("k", Bytes("v"), new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15),
        });

        // Keep reading within the sliding window; the absolute cap must still expire the entry.
        harness.Clock.Advance(TimeSpan.FromMinutes(8));
        Assert.NotNull(await harness.Distributed.GetAsync("k"));
        harness.Clock.Advance(TimeSpan.FromMinutes(8)); // t16 > absolute t15
        Assert.Null(await harness.Distributed.GetAsync("k"));
    }

    [Fact]
    public async Task Refresh_renews_a_sliding_entry_without_returning_it()
    {
        await using var harness = new CacheHarness();
        await harness.Distributed.SetAsync("k", Bytes("v"),
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) });

        harness.Clock.Advance(TimeSpan.FromMinutes(6));
        await harness.Distributed.RefreshAsync("k"); // renews to now+10

        harness.Clock.Advance(TimeSpan.FromMinutes(8)); // t14 < renewed t16
        Assert.NotNull(await harness.Distributed.GetAsync("k"));
    }

    [Fact]
    public async Task Remove_deletes_the_entry()
    {
        await using var harness = new CacheHarness();
        await harness.Distributed.SetAsync("k", Bytes("v"), new DistributedCacheEntryOptions());

        await harness.Distributed.RemoveAsync("k");

        Assert.Null(await harness.Distributed.GetAsync("k"));
        Assert.Equal(0, await harness.CountEntriesAsync());
    }

    [Fact]
    public async Task An_entry_with_no_expiration_survives_a_long_wait()
    {
        await using var harness = new CacheHarness();
        await harness.Distributed.SetAsync("k", Bytes("v"), new DistributedCacheEntryOptions());

        harness.Clock.Advance(TimeSpan.FromDays(3650));

        Assert.NotNull(await harness.Distributed.GetAsync("k"));
    }

    [Fact]
    public async Task Sync_and_async_apis_are_interchangeable()
    {
        await using var harness = new CacheHarness();
        var cache = harness.Distributed;

        cache.Set("k", Bytes("sync"), new DistributedCacheEntryOptions());
        Assert.Equal("sync", Str((await cache.GetAsync("k"))!));

        await cache.SetAsync("k", Bytes("async"), new DistributedCacheEntryOptions());
        Assert.Equal("async", Str(cache.Get("k")!));

        cache.Remove("k");
        Assert.Null(cache.Get("k"));
    }

    [Fact]
    public async Task DefaultSlidingExpiration_applies_when_no_options_are_given()
    {
        await using var harness = new CacheHarness(o => o.DefaultSlidingExpiration = TimeSpan.FromMinutes(5));
        await harness.Distributed.SetAsync("k", Bytes("v"), new DistributedCacheEntryOptions());

        harness.Clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Null(await harness.Distributed.GetAsync("k"));
    }

    [Fact]
    public async Task DefaultSlidingExpiration_does_not_shorten_an_explicit_absolute_deadline()
    {
        await using var harness = new CacheHarness(o => o.DefaultSlidingExpiration = TimeSpan.FromMinutes(1));
        await harness.Distributed.SetAsync("k", Bytes("v"),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) });

        // Past the 1-minute default sliding window, but well within the explicit 30-minute absolute deadline:
        // the caller's deadline must win, so the entry is still there.
        harness.Clock.Advance(TimeSpan.FromMinutes(5));

        Assert.NotNull(await harness.Distributed.GetAsync("k"));
    }
}
