using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Cache;

/// <summary>
/// A persisted cache entry: an opaque <see cref="Value"/> stored under <see cref="Key"/> until it expires.
/// Written and read by <see cref="RaskDistributedCache{TContext}"/> and swept by <see cref="CachePurger{TContext}"/>.
/// </summary>
public sealed class CacheEntry
{
    /// <summary>The cache key (primary key).</summary>
    public string Key { get; set; } = "";

    /// <summary>The cached bytes.</summary>
    public byte[] Value { get; set; } = [];

    /// <summary>The absolute time (UTC) the entry expires, or <c>null</c> when it has no absolute deadline.</summary>
    public DateTime? AbsoluteExpiration { get; set; }

    /// <summary>The sliding window in seconds — each read within the window pushes <see cref="ExpiresAt"/> forward. <c>null</c> = no sliding.</summary>
    public double? SlidingSeconds { get; set; }

    /// <summary>
    /// The effective time (UTC) the entry stops being served: the earlier of the absolute expiry and the last
    /// read plus the sliding window (<see cref="DateTime.MaxValue"/> when the entry never expires). Drives both the
    /// read-time freshness check and the purge sweep.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>When the entry was written (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>The EF Core mapping for <see cref="CacheEntry"/>.</summary>
public sealed class CacheEntryConfiguration : IEntityTypeConfiguration<CacheEntry>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<CacheEntry> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.HasKey(x => x.Key);
        entity.Property(x => x.Key).HasMaxLength(512);
        entity.Property(x => x.Value).IsRequired();
        // Drives the purge sweep and the "is this row still fresh?" read filter.
        entity.HasIndex(x => x.ExpiresAt);
    }
}

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
