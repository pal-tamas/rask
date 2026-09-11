using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Hosting.Shared;

namespace Rask.Cache;

/// <summary>Registers the database-backed cache into an <see cref="IServiceCollection"/>.</summary>
public static class RaskCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IDistributedCache"/> (as <see cref="RaskDistributedCache{TContext}"/>), the typed
    /// <see cref="ICache"/>, and the background <see cref="CachePurger{TContext}"/>. Map the table with
    /// <c>modelBuilder.AddRaskCache()</c> in <c>OnModelCreating</c> and register your context as an
    /// <see cref="IDbContextFactory{TContext}"/>. <see cref="CacheOptions"/> reads the <c>Rask:Cache</c>
    /// configuration section first and then <paramref name="configure"/>, so code wins. Idempotent.
    /// </summary>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the cache table.</typeparam>
    public static IServiceCollection AddRaskCache<TContext>(this IServiceCollection services, Action<CacheOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRaskOptions<CacheOptions>("Rask:Cache", static (section, o) => section.Bind(o), configure,
            static o => o.Validate());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDistributedCache, RaskDistributedCache<TContext>>();
        services.TryAddSingleton<ICache, Cache>();

        // Before the purger, so an app whose model never mapped CacheEntry fails the boot with the line
        // to type rather than on the first cache read. The purger itself tolerates a missing table — it
        // has to, because a freshly scaffolded app boots before its first migration has run — so it is
        // the wrong place to notice. See BatteryModelCheck: this reads the MODEL, never the database.
        services.AddHostedService<CacheModelCheck<TContext>>();

        // AddHostedService uses TryAddEnumerable, so a repeated call registers only one purger.
        services.AddHostedService<CachePurger<TContext>>();
        return services;
    }

    /// <summary>
    /// Registers the typed <see cref="ICache"/> over an <see cref="IDistributedCache"/> you supply — Redis,
    /// for instance — instead of over the app's own database. Register the backing store first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The database-backed cache is the default and the recommendation: it needs no second piece of
    /// infrastructure to run, secure and back up. Reach for this overload when you already operate Redis for
    /// other reasons, or when several instances need a cache the database shouldn't carry.
    /// </para>
    /// <para>
    /// No <see cref="CacheOptions"/>, deliberately. Both of them — <see cref="CacheOptions.PurgeInterval"/>
    /// and <see cref="CacheOptions.DefaultSlidingExpiration"/> — are implemented by
    /// <see cref="RaskDistributedCache{TContext}"/>, so against another store they would be settings that
    /// silently do nothing. Expiry is the store's own business: Redis evicts on its own schedule, and a
    /// default expiration belongs in its configuration or in the per-call
    /// <see cref="DistributedCacheEntryOptions"/>.
    /// </para>
    /// <example>
    /// <code>
    /// builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379");
    /// builder.Services.AddRaskCache();
    /// </code>
    /// </example>
    /// </remarks>
    public static IServiceCollection AddRaskCache(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // No IDistributedCache and no purger: this overload's whole point is that the store is somebody
        // else's. Nothing here needs a DbContext, so an app using it never maps the CacheEntry table and
        // never runs a migration for it.
        services.TryAddSingleton<ICache, Cache>();
        return services;
    }
}
