using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Background;

/// <summary>The EF Core mapping for <see cref="RecurringJobState"/>.</summary>
public sealed class RecurringJobStateConfiguration : IEntityTypeConfiguration<RecurringJobState>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<RecurringJobState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        // The name is still what identifies a recurring job, and still what makes two instances racing to
        // create the row resolve to one winner — as a unique index rather than as the primary key.
        builder.HasIndex(x => x.Name).IsUnique();
    }
}
