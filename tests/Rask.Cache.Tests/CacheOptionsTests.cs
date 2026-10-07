using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Rask.Caching.Tests;

// A bad value is refused when the options are built — at host start through ValidateOnStart, or on the first resolve in
// these bare containers — and the message names both the section and the setting.
public sealed class CacheOptionsTests
{
    [Fact]
    public void AddRaskCache_rejects_a_non_positive_purge_interval() =>
        AssertRejected(o => o.SweepInterval = TimeSpan.Zero, "SweepInterval");

    [Fact]
    public void AddRaskCache_rejects_a_non_positive_default_sliding_expiration() =>
        AssertRejected(o => o.DefaultSlidingExpiration = TimeSpan.FromSeconds(-1), "DefaultSlidingExpiration");

    [Fact]
    public void AddRaskCache_rejects_a_purge_interval_above_the_timer_maximum() =>
        AssertRejected(o => o.SweepInterval = TimeSpan.FromDays(60), "SweepInterval");

    private static void AssertRejected(Action<CacheOptions> configure, string setting)
    {
        var services = new ServiceCollection();
        services.AddRaskCache<CacheDbContext>(configure);
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<CacheOptions>());
        Assert.Contains($"Rask:Cache:{setting}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddRaskCache_accepts_the_defaults()
    {
        var services = new ServiceCollection();

        var ex = Record.Exception(() => services.AddRaskCache<CacheDbContext>());

        Assert.Null(ex);
    }
}
