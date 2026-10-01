using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Outbox;

/// <summary>The EF Core mapping for <see cref="OutboxMessage"/>.</summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).IsRequired().HasMaxLength(512);
        builder.Property(x => x.Handler).HasMaxLength(1024);
        builder.Property(x => x.Payload).IsRequired();
        // Drives the "oldest unprocessed first" poll query. ClaimedUntil stays out of it: in a healthy
        // outbox almost every candidate row is unclaimed, so it costs nothing as a residual filter, and a
        // filtered index would need provider-specific SQL.
        builder.HasIndex(x => new { x.ProcessedAt, x.Id });
        // Fences the completion write — see Job.ClaimToken for why.
        builder.Property(x => x.ClaimToken).IsConcurrencyToken();

        // Mapped EXPLICITLY, and the table is deliberately not Tenancy.PerTenant. A partitioned table takes a
        // query filter, and a filter here would hide other tenants' rows from the drain — the processor has
        // to see everybody's work. So the tenant is data on the row, not a partition of the table, and
        // ApplyRaskConventions leaves a tenant somebody mapped themselves alone.
        builder.Property(x => x.TenantId);
    }
}
