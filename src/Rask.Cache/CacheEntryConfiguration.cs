using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Data;

namespace Rask.Caching;

/// <summary>The EF Core mapping for <see cref="CacheEntry"/>.</summary>
public sealed class CacheEntryConfiguration : IEntityTypeConfiguration<CacheEntry>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<CacheEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // A battery's table keeps its names whatever the app's TableNames and ColumnNames say.
        KeptNames.Keep(builder.Metadata.ClrType);
        // Not versioned and never soft-deleted, mapped with or without ApplyRaskConventions.
        builder.Ignore(x => x.Version);
        builder.Ignore(x => x.DeletedAt);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Key).IsRequired().HasMaxLength(512);
        // The key is still what identifies an entry, and still what a second writer collides with — as a
        // unique index rather than as the primary key.
        builder.HasIndex(x => x.Key).IsUnique();
        builder.Property(x => x.Value).IsRequired();
        // Rask's own moments: each stored as the UTC instant it holds, on any context.
        TimestampColumn.Map(builder.Property(x => x.ExpiresAt));
        TimestampColumn.Map(builder.Property(x => x.AbsoluteExpiration));
        // Drives the purge sweep and the "is this row still fresh?" read filter.
        builder.HasIndex(x => x.ExpiresAt);
    }
}
