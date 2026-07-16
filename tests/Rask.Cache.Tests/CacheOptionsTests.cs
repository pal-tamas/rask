using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cache.Tests;

public sealed class CacheOptionsTests
{
    [Fact]
    public void AddRaskCache_rejects_a_non_positive_purge_interval()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddRaskCache<CacheDbContext>(o => o.PurgeInterval = TimeSpan.Zero));
        Assert.Equal("PurgeInterval", ex.ParamName);
    }

    [Fact]
    public void AddRaskCache_rejects_a_non_positive_default_sliding_expiration()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddRaskCache<CacheDbContext>(o => o.DefaultSlidingExpiration = TimeSpan.FromSeconds(-1)));
        Assert.Equal("DefaultSlidingExpiration", ex.ParamName);
    }

    [Fact]
    public void AddRaskCache_rejects_a_purge_interval_above_the_timer_maximum()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddRaskCache<CacheDbContext>(o => o.PurgeInterval = TimeSpan.FromDays(60)));
        Assert.Equal("PurgeInterval", ex.ParamName);
    }

    [Fact]
    public void AddRaskCache_accepts_the_defaults()
    {
        var services = new ServiceCollection();
        var ex = Record.Exception(() => services.AddRaskCache<CacheDbContext>());
        Assert.Null(ex);
    }
}
