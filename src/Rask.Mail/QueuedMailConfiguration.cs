using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Mailing;

/// <summary>The EF Core mapping for <see cref="QueuedMail"/>.</summary>
public sealed class QueuedMailConfiguration : IEntityTypeConfiguration<QueuedMail>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<QueuedMail> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.From).IsRequired();
        builder.Property(x => x.To).IsRequired();
        builder.Property(x => x.Subject).IsRequired();
        // Drives the "due, oldest first" claim query. ClaimedUntil stays out of it: in a healthy queue
        // almost every candidate row is unclaimed, so it costs nothing as a residual filter, and a
        // filtered index would need provider-specific SQL.
        builder.HasIndex(x => new { x.ProcessedAt, x.RunAt, x.Id });
        // Fences the completion write — see Job.ClaimToken for why.
        builder.Property(x => x.ClaimToken).IsConcurrencyToken();

        // Mapped EXPLICITLY, and the table is deliberately not Tenancy.PerTenant. A partitioned table takes a
        // query filter, and a filter here would hide other tenants' rows from the drain — the runner has to
        // see everybody's work. So the tenant is data on the row, not a partition of the table, and
        // ApplyRaskConventions leaves a tenant somebody mapped themselves alone.
        builder.Property(x => x.TenantId);
    }
}
