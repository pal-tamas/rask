using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Rask.Data;

namespace Rask.Cache;

/// <summary>
/// A persisted cache entry: an opaque <see cref="Value"/> stored under <see cref="Key"/> until it expires.
/// Written and read by <see cref="RaskDistributedCache{TContext}"/> and swept by <see cref="CachePurger{TContext}"/>.
/// </summary>
/// <remarks>
/// The table carries a surrogate <c>Id</c> and keeps <see cref="Key"/> as a UNIQUE index. Nothing ever looked
/// this row up by primary key — every read is <c>Where(e =&gt; e.Key == key)</c> — and nothing holds a foreign
/// key to it, so the surrogate changes no lookup; it is what lets the row have a read face. An
/// <see cref="Entity{TId}"/> rather than an <see cref="Aggregate{TId}"/>: a soft-delete filter over a cache
/// would hide entries the purge sweep is meant to remove.
/// </remarks>
public sealed class CacheEntry : Entity<Guid>
{
    /// <summary>No form model: a cache entry is written by the cache, never posted.</summary>
    public const ModelWrites Writes = ModelWrites.None;

    /// <summary>The cache key.</summary>
    public string Key { get; internal set; } = "";

    /// <summary>Starts an entry under <paramref name="key" />.</summary>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The bytes to cache.</param>
    /// <param name="expiresAt">When it stops being served (UTC).</param>
    /// <param name="savedAt">When it was written (UTC).</param>
    /// <param name="absoluteExpiration">Its absolute deadline, if it has one.</param>
    /// <param name="slidingSeconds">Its sliding window in seconds, if it has one.</param>
    public static CacheEntry For(
        string key,
        byte[] value,
        DateTime expiresAt,
        DateTime savedAt,
        DateTime? absoluteExpiration = null,
        double? slidingSeconds = null)
    {
        var entry = new CacheEntry
        {
            Id = Guid.CreateVersion7(),
            Key = key,
            Value = value,
            ExpiresAt = expiresAt,
            AbsoluteExpiration = absoluteExpiration,
            SlidingSeconds = slidingSeconds,
        };

        // This package writes its own rows and may be pointed at a DbContext that carries none of Rask's
        // interceptors, so the row times itself rather than hoping somebody else will.
        entry.Stamp(savedAt);
        return entry;
    }

    /// <summary>The cached bytes.</summary>
    public byte[] Value { get; internal set; } = [];

    /// <summary>The absolute time (UTC) the entry expires, or <c>null</c> when it has no absolute deadline.</summary>
    public DateTime? AbsoluteExpiration { get; internal set; }

    /// <summary>The sliding window in seconds — each read within the window pushes <see cref="ExpiresAt"/> forward. <c>null</c> = no sliding.</summary>
    public double? SlidingSeconds { get; internal set; }

    /// <summary>
    /// The effective time (UTC) the entry stops being served: the earlier of the absolute expiry and the last
    /// read plus the sliding window (<see cref="DateTime.MaxValue"/> when the entry never expires). Drives both the
    /// read-time freshness check and the purge sweep.
    /// </summary>
    public DateTime ExpiresAt { get; internal set; }

}

/// <summary>The EF Core mapping for <see cref="CacheEntry"/>.</summary>
public sealed class CacheEntryConfiguration : IEntityTypeConfiguration<CacheEntry>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<CacheEntry> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Key).IsRequired().HasMaxLength(512);
        // The key is still what identifies an entry, and still what a second writer collides with — as a
        // unique index rather than as the primary key.
        entity.HasIndex(x => x.Key).IsUnique();
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
