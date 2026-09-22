using System.Text.Json.Serialization;

namespace Rask.Cache.Tests;

[Collection(CacheDbCollection.Name)]
public sealed class TypedCacheTests
{
    public sealed record Widget(int Id, string Name);

    [Fact]
    public async Task Set_then_Get_round_trips_a_typed_value()
    {
        await using var harness = new CacheHarness();

        await harness.Cache.Set("w", new Widget(7, "cog"));

        Assert.Equal(new Widget(7, "cog"), await harness.Cache.Get<Widget>("w"));
    }

    [Fact]
    public async Task Get_is_default_for_a_missing_key()
    {
        await using var harness = new CacheHarness();

        Assert.Null(await harness.Cache.Get<Widget>("absent"));
    }

    [Fact]
    public async Task Remember_loads_once_then_serves_from_the_cache()
    {
        await using var harness = new CacheHarness();
        var loads = 0;
        Task<Widget> Load() => Task.FromResult(new Widget(Interlocked.Increment(ref loads), "made"));

        var first = await harness.Cache.Remember("w", Load);
        var second = await harness.Cache.Remember("w", Load);

        Assert.Equal(new Widget(1, "made"), first);
        Assert.Equal(first, second);
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task Remember_For_loads_again_once_the_time_is_up()
    {
        await using var harness = new CacheHarness();
        var loads = 0;
        Widget Load() => new(Interlocked.Increment(ref loads), "v");

        var first = await harness.Cache.Remember("w", Load).For(5.Minutes);

        harness.Clock.Advance(6.Minutes);
        var second = await harness.Cache.Remember("w", Load).For(5.Minutes);

        Assert.Equal(1, first.Id);
        Assert.Equal(2, second.Id);
    }

    [Fact]
    public async Task Sliding_keeps_a_value_that_is_read_and_drops_one_that_is_not()
    {
        await using var harness = new CacheHarness();
        await harness.Cache.Set("w", new Widget(1, "kept")).Sliding(10.Minutes);

        harness.Clock.Advance(8.Minutes);
        Assert.NotNull(await harness.Cache.Get<Widget>("w"));   // the read renews it

        harness.Clock.Advance(8.Minutes);
        Assert.NotNull(await harness.Cache.Get<Widget>("w"));

        harness.Clock.Advance(11.Minutes);

        Assert.Null(await harness.Cache.Get<Widget>("w"));
    }

    [Fact]
    public async Task Until_drops_the_value_at_that_moment()
    {
        await using var harness = new CacheHarness();
        await harness.Cache.Set("w", new Widget(1, "x")).Until(harness.Clock.GetUtcNow() + 1.Hour);

        harness.Clock.Advance(59.Minutes);
        Assert.NotNull(await harness.Cache.Get<Widget>("w"));

        harness.Clock.Advance(2.Minutes);

        Assert.Null(await harness.Cache.Get<Widget>("w"));
    }

    [Fact]
    public async Task Forget_clears_a_typed_entry()
    {
        await using var harness = new CacheHarness();
        await harness.Cache.Set("w", new Widget(1, "x"));

        await harness.Cache.Forget("w");

        Assert.Null(await harness.Cache.Get<Widget>("w"));
    }

    [Fact]
    public async Task A_lifetime_step_refuses_a_duration_that_is_not_positive()
    {
        await using var harness = new CacheHarness();

        var e = Assert.Throws<ArgumentOutOfRangeException>(() => harness.Cache.Remember("w", () => 1).For(TimeSpan.Zero));

        Assert.Contains("10.Minutes", e.Message);
    }

    [Fact]
    public async Task The_static_Cache_reaches_the_cache_of_the_work_in_progress()
    {
        await using var harness = new CacheHarness();
        using var work = Ambient.Enter(harness.Services);

        var remembered = await Cache.Remember("w", () => new Widget(3, "static")).For(1.Hour);
        await Cache.Set("x", new Widget(4, "set"));

        Assert.Equal(remembered, await harness.Cache.Get<Widget>("w"));
        Assert.Equal(new Widget(4, "set"), await Cache.Get<Widget>("x"));

        await Cache.Forget("x");

        Assert.Null(await Cache.Get<Widget>("x"));
    }

    [Fact]
    public async Task Outside_any_work_the_static_Cache_names_the_constructor_to_inject()
    {
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Cache.Get<Widget>("w"));

        Assert.Contains("Inject ICache in the constructor", e.Message);
    }

    [Fact]
    public async Task Nothing_runs_until_it_is_awaited()
    {
        await using var harness = new CacheHarness();
        var loads = 0;

        _ = harness.Cache.Remember("w", () => ++loads).For(1.Hour);

        Assert.Equal(0, loads);
        Assert.Null(await harness.Cache.Get<int?>("w"));
    }

    [Fact]
    public async Task A_registered_Json_context_serializes_without_reflection()
    {
        await using var harness = new CacheHarness(o => o.Json = WidgetJson.Default);

        await harness.Cache.Set("w", new Widget(9, "ctx"));

        Assert.Equal(new Widget(9, "ctx"), await harness.Cache.Get<Widget>("w"));
    }

    [Fact]
    public async Task A_type_the_registered_context_lacks_says_which_attribute_to_add()
    {
        await using var harness = new CacheHarness(o => o.Json = WidgetJson.Default);

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Cache.Set("d", DateOnly.MinValue).AsTask());

        Assert.Contains("[JsonSerializable(typeof(DateOnly))]", e.Message);
    }
}

[JsonSerializable(typeof(TypedCacheTests.Widget))]
internal sealed partial class WidgetJson : JsonSerializerContext;
