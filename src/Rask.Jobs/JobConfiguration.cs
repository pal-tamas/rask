using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Background;

/// <summary>The EF Core mapping for <see cref="Job"/>.</summary>
public sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).IsRequired().HasMaxLength(512);
        builder.Property(x => x.Payload).IsRequired();
        // Drives the "due, oldest first" claim query. ClaimedUntil is deliberately NOT in the index: in a
        // healthy queue almost every candidate row is unclaimed, so it costs nothing as a residual filter,
        // and a filtered index would need provider-specific SQL. The claim's read-back rides the primary
        // key (it filters on the same id list), so ClaimToken needs no index either.
        builder.HasIndex(x => new { x.ProcessedAt, x.RunAt, x.Id });
        // Fences the completion write: EF appends `AND ClaimToken = @original` to every tracked update, so
        // an instance whose lease expired mid-job gets a concurrency exception instead of overwriting the
        // outcome of whichever instance now owns the row.
        builder.Property(x => x.ClaimToken).IsConcurrencyToken();

        // Mapped EXPLICITLY, and the table is deliberately not Tenancy.PerTenant. A partitioned table takes a
        // query filter, and a filter here would hide other tenants' rows from the drain — the runner has to
        // see everybody's work. So the tenant is data on the row, not a partition of the table, and
        // ApplyRaskConventions leaves a tenant somebody mapped themselves alone.
        builder.Property(x => x.TenantId);
    }
}
