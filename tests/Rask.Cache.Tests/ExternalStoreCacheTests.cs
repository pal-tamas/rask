using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Cache.Tests;

/// <summary>
/// Backing <see cref="ICache"/> with someone else's <see cref="IDistributedCache"/> — Redis in practice.
/// </summary>
/// <remarks>
/// The store is exercised through the in-memory <c>IDistributedCache</c> rather than a real Redis: what
/// these assert is Rask's registration, and `Cache` only ever talks to the interface. A Redis-specific test
/// would be testing Microsoft's implementation of it.
/// </remarks>
public sealed class ExternalStoreCacheTests
{
    private static ServiceProvider Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task The_typed_cache_works_over_any_distributed_cache()
    {
        await using var provider = Build(services =>
        {
            services.AddDistributedMemoryCache(); // stands in for AddStackExchangeRedisCache
            services.AddRaskCache();
        });

        var cache = provider.GetRequiredService<ICache>();
        await cache.SetAsync("k", new Sample("hello", 42));

        Assert.Equal(new Sample("hello", 42), await cache.GetAsync<Sample>("k"));
    }

    [Fact]
    public async Task GetOrCreate_still_reads_through()
    {
        await using var provider = Build(services =>
        {
            services.AddDistributedMemoryCache();
            services.AddRaskCache();
        });

        var cache = provider.GetRequiredService<ICache>();
        var calls = 0;

        Task<Sample> Factory(CancellationToken _)
        {
            calls++;
            return Task.FromResult(new Sample("made", 1));
        }

        Assert.Equal(new Sample("made", 1), await cache.GetOrAddAsync("k", Factory));
        Assert.Equal(new Sample("made", 1), await cache.GetOrAddAsync("k", Factory));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void No_purger_is_registered()
    {
        // The trap this overload exists to remove. AddRaskCache<TContext> always registers CachePurger,
        // which needs the CacheEntry table — so an app backing ICache with Redis still had to map that
        // table and run a migration for it, or watch the purger throw every five minutes.
        using var provider = Build(services =>
        {
            services.AddDistributedMemoryCache();
            services.AddRaskCache();
        });

        Assert.Empty(provider.GetServices<IHostedService>());
    }

    [Fact]
    public void Nothing_registers_a_distributed_cache_of_its_own()
    {
        // Registering one would override — or be overridden by — the store the app actually chose, and
        // which of those happened would depend on call order.
        var services = new ServiceCollection();
        services.AddRaskCache();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IDistributedCache));
    }

    [Fact]
    public async Task A_store_registered_after_AddRaskCache_still_wins()
    {
        // ICache resolves IDistributedCache lazily, so the order these two lines appear in must not decide
        // which store the app gets.
        await using var provider = Build(services =>
        {
            services.AddRaskCache();
            services.AddDistributedMemoryCache();
        });

        var cache = provider.GetRequiredService<ICache>();
        await cache.SetAsync("k", new Sample("late", 7));

        Assert.Equal(new Sample("late", 7), await cache.GetAsync<Sample>("k"));
    }

    [Fact]
    public void Registering_twice_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddRaskCache();
        services.AddRaskCache();

        Assert.Single(services, d => d.ServiceType == typeof(ICache));
    }

    [Fact]
    public void A_null_service_collection_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddRaskCache());
    }

    private sealed record Sample(string Name, int Count);
}
