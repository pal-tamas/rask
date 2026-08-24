using Microsoft.Extensions.Caching.Distributed;

namespace Rask.Cache.Tests;

[Collection(CacheDbCollection.Name)]
public sealed class TypedCacheTests
{
    public sealed record Widget(int Id, string Name);

    [Fact]
    public async Task Set_then_get_round_trips_a_typed_value()
    {
        await using var harness = new CacheHarness();
        await harness.Cache.SetAsync("w", new Widget(7, "cog"));

        var got = await harness.Cache.GetAsync<Widget>("w");

        Assert.Equal(new Widget(7, "cog"), got);
    }

    [Fact]
    public async Task Get_returns_default_for_a_missing_key()
    {
        await using var harness = new CacheHarness();
        Assert.Null(await harness.Cache.GetAsync<Widget>("absent"));
    }

    [Fact]
    public async Task GetOrCreate_runs_the_factory_once_then_serves_from_the_cache()
    {
        await using var harness = new CacheHarness();
        var calls = 0;

        Task<Widget> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new Widget(1, "made"));
        }

        var first = await harness.Cache.GetOrCreateAsync("w", Factory);
        var second = await harness.Cache.GetOrCreateAsync("w", Factory);

        Assert.Equal(new Widget(1, "made"), first);
        Assert.Equal(new Widget(1, "made"), second);
        Assert.Equal(1, calls); // second call is a cache hit
    }

    [Fact]
    public async Task GetOrCreate_recomputes_after_the_entry_expires()
    {
        await using var harness = new CacheHarness();
        var calls = 0;
        Task<Widget> Factory(CancellationToken _)
        {
            var n = Interlocked.Increment(ref calls);
            return Task.FromResult(new Widget(n, "v"));
        }

        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };
        var first = await harness.Cache.GetOrCreateAsync("w", Factory, options);
        harness.Clock.Advance(TimeSpan.FromMinutes(6));
        var second = await harness.Cache.GetOrCreateAsync("w", Factory, options);

        Assert.Equal(1, first.Id);
        Assert.Equal(2, second.Id); // recomputed after expiry
    }

    [Fact]
    public async Task Remove_clears_a_typed_entry()
    {
        await using var harness = new CacheHarness();
        await harness.Cache.SetAsync("w", new Widget(1, "x"));

        await harness.Cache.RemoveAsync("w");

        Assert.Null(await harness.Cache.GetAsync<Widget>("w"));
    }
}
