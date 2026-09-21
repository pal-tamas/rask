using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Data;

namespace Rask.Outbox;

/// <summary>
/// A persisted domain event awaiting (or having completed) publication. Written in the same transaction as
/// the change that raised it and drained by the <see cref="OutboxProcessor{TContext}"/>.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="Entity{TId}"/> rather than an <see cref="Aggregate{TId}"/>, deliberately. An aggregate would
/// bring <c>DeletedAt</c> and a global "not deleted" query filter, which would hide rows from the drain query
/// and turn the dashboard's purge — an <c>ExecuteDelete</c> — into a stamp that leaves them in the table
/// forever. It would also bring <c>Version</c> as a second concurrency token beside <see cref="ClaimToken"/>,
/// and the processor advances state with <c>ExecuteUpdate</c>, which never maintains one.
/// </para>
/// <para>
/// Nothing creates one of these from a form, so no form model is generated — see <see cref="Writes"/>.
/// </para>
/// </remarks>
public sealed class OutboxMessage : Entity<long>
{
    /// <summary>No form model: an outbox row is written by the interceptor, never posted.</summary>
    public const ModelWrites Writes = ModelWrites.None;

    /// <summary>
    ///     No <c>CreatedAt</c>: <see cref="OccurredAt" /> is this row's creation time and says something more
    ///     specific — when the domain event was raised.
    /// </summary>
    public const Timestamps Stamps = Timestamps.Updated;

    private OutboxMessage()
    {
    }

    /// <summary>The event's registered type name (see <see cref="OutboxSerializerRegistry"/>).</summary>
    public string Type { get; private set; } = "";

    /// <summary>The JSON-serialized event payload.</summary>
    public string Payload { get; private set; } = "";

    /// <summary>When the event was enqueued (UTC).</summary>
    public DateTime OccurredAt { get; private set; }

    /// <summary>When the event was successfully published (UTC), or <c>null</c> while it is pending.</summary>
    public DateTime? ProcessedAt { get; private set; }

    /// <summary>How many publish attempts have been made.</summary>
    public int Attempts { get; private set; }

    /// <summary>The last failure message, if any.</summary>
    public string? Error { get; private set; }

    /// <summary>
    /// The processor instance currently holding this message, or <c>null</c> when nobody does.
    /// </summary>
    /// <remarks>
    /// Also the optimistic-concurrency token, which is what stops an instance whose lease expired
    /// mid-dispatch from stamping its outcome over the row another instance has since taken.
    /// </remarks>
    public Guid? ClaimToken { get; private set; }

    /// <summary>
    /// When the current claim expires (UTC). Null or in the past means the message is claimable — which is
    /// also how a processor that died mid-dispatch releases its work: the lease simply runs out.
    /// </summary>
    public DateTime? ClaimedUntil { get; private set; }

    /// <summary>Enqueues <paramref name="payload" /> for publication.</summary>
    /// <param name="type">The event's registered type name.</param>
    /// <param name="payload">The serialized event.</param>
    /// <param name="occurredAt">When the event was raised (UTC).</param>
    /// <remarks>
    ///     <para>The key is the store's, and is also the processing order, so nothing assigns one here.</para>
    ///     <para>
    ///         The row records the tenant it was enqueued for, so the processor can re-enter it before
    ///         publishing. Null when there is none — a message raised by the host itself belongs to nobody,
    ///         and refusing to write it would be wrong.
    ///     </para>
    /// </remarks>
    public static OutboxMessage For(string type, string payload, DateTime occurredAt)
    {
        var message = new OutboxMessage { Type = type, Payload = payload, OccurredAt = occurredAt };
        message.RecordTenant(Current.Tenant);
        return message;
    }

    /// <summary>Records a successful publish, clearing any error from an earlier attempt.</summary>
    /// <param name="at">When it was published (UTC).</param>
    public void Published(DateTime at)
    {
        ProcessedAt = at;
        Error = null;
    }

    /// <summary>Records why an attempt failed. The row stays pending and is retried.</summary>
    /// <param name="error">The failure message.</param>
    public void Failed(string error) => Error = error;

    /// <summary>Drops the claim, so another processor (or this one, later) may take the row.</summary>
    public void Release()
    {
        ClaimToken = null;
        ClaimedUntil = null;
    }
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

        // Mapped EXPLICITLY, and the table is deliberately not Tenancy.PerTenant. A partitioned table takes a
        // query filter, and a filter here would hide other tenants' rows from the drain — the processor has
        // to see everybody's work. So the tenant is data on the row, not a partition of the table, and
        // ApplyRaskConventions leaves a tenant somebody mapped themselves alone.
        entity.Property(x => x.TenantId);
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
