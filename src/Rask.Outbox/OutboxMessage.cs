using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Outbox;

/// <summary>
/// A persisted domain event awaiting (or having completed) publication. Written in the same transaction as
/// the change that raised it and drained by the <see cref="OutboxProcessor{TContext}"/>.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Database-generated, monotonically increasing key — also the processing order.</summary>
    public long Id { get; set; }

    /// <summary>The event's registered type name (see <see cref="OutboxSerializerRegistry"/>).</summary>
    public string Type { get; set; } = "";

    /// <summary>The JSON-serialized event payload.</summary>
    public string Payload { get; set; } = "";

    /// <summary>When the event was enqueued (UTC).</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>When the event was successfully published (UTC), or <c>null</c> while it is pending.</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>How many publish attempts have been made.</summary>
    public int Attempts { get; set; }

    /// <summary>The last failure message, if any.</summary>
    public string? Error { get; set; }

    /// <summary>
    /// The processor instance currently holding this message, or <c>null</c> when nobody does.
    /// </summary>
    /// <remarks>
    /// Also the optimistic-concurrency token, which is what stops an instance whose lease expired
    /// mid-dispatch from stamping its outcome over the row another instance has since taken.
    /// </remarks>
    public Guid? ClaimToken { get; set; }

    /// <summary>
    /// When the current claim expires (UTC). Null or in the past means the message is claimable — which is
    /// also how a processor that died mid-dispatch releases its work: the lease simply runs out.
    /// </summary>
    public DateTime? ClaimedUntil { get; set; }
}

/// <summary>The EF Core mapping for <see cref="OutboxMessage"/>.</summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<OutboxMessage> entity)
    {
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Type).IsRequired().HasMaxLength(512);
        entity.Property(x => x.Payload).IsRequired();
        // Drives the "oldest unprocessed first" poll query. ClaimedUntil stays out of it: in a healthy
        // outbox almost every candidate row is unclaimed, so it costs nothing as a residual filter, and a
        // filtered index would need provider-specific SQL.
        entity.HasIndex(x => new { x.ProcessedAt, x.Id });
        // Fences the completion write — see Job.ClaimToken for why.
        entity.Property(x => x.ClaimToken).IsConcurrencyToken();
    }
}

/// <summary>Model-building helper for the outbox table.</summary>
public static class OutboxModelBuilderExtensions
{
    /// <summary>Maps the <see cref="OutboxMessage"/> table. Call from your context's <c>OnModelCreating</c>.</summary>
    public static ModelBuilder AddRaskOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        return modelBuilder;
    }
}
