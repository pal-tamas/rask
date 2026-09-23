using Rask.Batteries;
using Rask.Testing;

namespace Rask.Cache.Tests;

/// <summary>
/// The fake stands in for the whole battery, so there is no database and no distributed cache to reach.
/// </summary>
public sealed class CacheFakeTests
{
    [Fact]
    public async Task A_value_is_loaded_once_and_served_from_the_cache_after()
    {
        using var cache = Cache.Fake();
        var loads = 0;

        var first = await Cache.Remember("products", () => ++loads).For(10.Minutes);
        var second = await Cache.Remember("products", () => ++loads).For(10.Minutes);

        Assert.Equal(1, first);
        Assert.Equal(1, second);
        cache.Loaded("products").Once();
        cache.Read("products").Twice();
    }

    [Fact]
    public async Task A_value_is_loaded_again_once_its_lifetime_has_passed()
    {
        using var clock = Clock.Fake(at: new DateTimeOffset(2026, 6, 10, 9, 00, 0, TimeSpan.Zero));
        using var cache = Cache.Fake();
        var loads = 0;

        await Cache.Remember("products", () => ++loads).For(10.Minutes);
        clock.Advance(11.Minutes);
        var afterExpiry = await Cache.Remember("products", () => ++loads).For(10.Minutes);

        // Expiry is real and reads the app's clock, so a test proves staleness by moving time rather than
        // by sleeping — which is the difference between a 3 ms test and an 11-minute one.
        Assert.Equal(2, afterExpiry);
        cache.Loaded("products").Twice();
    }

    [Fact]
    public async Task A_sliding_window_is_kept_alive_by_being_read()
    {
        using var clock = Clock.Fake(at: new DateTimeOffset(2026, 6, 10, 9, 00, 0, TimeSpan.Zero));
        using var cache = Cache.Fake();
        var loads = 0;

        await Cache.Remember("session", () => ++loads).Sliding(10.Minutes);
        clock.Advance(8.Minutes);
        await Cache.Remember("session", () => ++loads).Sliding(10.Minutes);
        clock.Advance(8.Minutes);
        var stillThere = await Cache.Remember("session", () => ++loads).Sliding(10.Minutes);

        Assert.Equal(1, stillThere);
        cache.Loaded("session").Once();
    }

    [Fact]
    public async Task A_forgotten_key_is_loaded_afresh()
    {
        using var cache = Cache.Fake();
        var loads = 0;

        await Cache.Remember("products", () => ++loads).For(10.Minutes);
        await Cache.Forget("products");
        var reloaded = await Cache.Remember("products", () => ++loads).For(10.Minutes);

        Assert.Equal(2, reloaded);
        cache.Forgotten("products").Once();
        cache.Loaded("products").Twice();
    }

    [Fact]
    public async Task A_stored_value_is_read_back()
    {
        using var cache = Cache.Fake();

        await Cache.Set("banner", "closed for lunch").For(1.Hour);

        Assert.Equal("closed for lunch", await Cache.Get<string>("banner"));
        cache.Read("banner").Once();
    }

    [Fact]
    public async Task A_failure_names_the_keys_that_were_loaded_instead()
    {
        using var cache = Cache.Fake();

        await Cache.Remember("products", () => 1).For(1.Hour);

        var error = Assert.Throws<CountingException>(() => cache.Loaded("orders").Once());
        Assert.Contains("\"products\"", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disposing_the_fake_puts_the_real_cache_back()
    {
        using (var cache = Cache.Fake())
        {
            await Cache.Remember("products", () => 1).For(1.Hour);
            cache.Loaded("products").Once();
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Cache.Remember("products", () => 1).For(1.Hour));
        Assert.Contains("Inject ICache", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_injected_ICache_can_be_the_fake_too()
    {
        using var cache = Cache.Fake();
        ICache injected = cache;

        await injected.Remember("products", () => 1).For(1.Hour);

        cache.Loaded("products").Once();
    }
}
