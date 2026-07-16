using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.Cache;

/// <summary>Registers the database-backed cache into an <see cref="IServiceCollection"/>.</summary>
public static class RaskCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IDistributedCache"/> (as <see cref="RaskDistributedCache{TContext}"/>), the typed
    /// <see cref="ICache"/>, and the background <see cref="CachePurger{TContext}"/>. Map the table with
    /// <c>modelBuilder.AddRaskCache()</c> in <c>OnModelCreating</c> and register your context as an
    /// <see cref="IDbContextFactory{TContext}"/>. Idempotent.
    /// </summary>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the cache table.</typeparam>
    public static IServiceCollection AddRaskCache<TContext>(this IServiceCollection services, Action<CacheOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new CacheOptions();
        configure?.Invoke(options);
        options.Validate();

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDistributedCache, RaskDistributedCache<TContext>>();
        services.TryAddSingleton<ICache, Cache>();

        // AddHostedService uses TryAddEnumerable, so a repeated call registers only one purger.
        services.AddHostedService<CachePurger<TContext>>();
        return services;
    }
}
