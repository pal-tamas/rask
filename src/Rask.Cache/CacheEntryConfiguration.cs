using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Caching;

/// <summary>The EF Core mapping for <see cref="CacheEntry"/>.</summary>
public sealed class CacheEntryConfiguration : IEntityTypeConfiguration<CacheEntry>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<CacheEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Key).IsRequired().HasMaxLength(512);
        // The key is still what identifies an entry, and still what a second writer collides with — as a
        // unique index rather than as the primary key.
        builder.HasIndex(x => x.Key).IsUnique();
        builder.Property(x => x.Value).IsRequired();
        // Drives the purge sweep and the "is this row still fresh?" read filter.
        builder.HasIndex(x => x.ExpiresAt);
    }
}
