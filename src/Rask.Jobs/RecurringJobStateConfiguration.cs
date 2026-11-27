using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Data;
using Rask.Values;

namespace Rask.Background;

/// <summary>The EF Core mapping for <see cref="RecurringJobState"/>.</summary>
public sealed class RecurringJobStateConfiguration : IEntityTypeConfiguration<RecurringJobState>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<RecurringJobState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // A battery's table keeps its names whatever the app's TableNames and ColumnNames say.
        KeptNames.Keep(builder.Metadata.ClrType);
        // Not versioned and never soft-deleted, mapped with or without ApplyRaskConventions.
        builder.Ignore(x => x.Version);
        builder.Ignore(x => x.DeletedAt);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        ValueObjectColumn.Map(builder.Property(x => x.LastEnqueuedAt), static (Timestamp t) => t.Value);
        // The name is still what identifies a recurring job, and still what makes two instances racing to
        // create the row resolve to one winner — as a unique index rather than as the primary key.
        builder.HasIndex(x => x.Name).IsUnique();
    }
}
