using Microsoft.EntityFrameworkCore;

namespace Rask.Caching;

/// <summary>Model-building helper for the cache table.</summary>
public static class CacheModelBuilderExtensions
{
    /// <summary>
    /// Maps the <see cref="CacheEntry"/> table. Call from your context's <c>OnModelCreating</c>, then create the
    /// schema with <c>rask db add AddCache &amp;&amp; rask db update</c>.
    /// </summary>
    public static ModelBuilder AddRaskCache(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new CacheEntryConfiguration());
        return modelBuilder;
    }
}
