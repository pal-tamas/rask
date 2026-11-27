using Rask.Batteries;
using Rask.Data;
using Rask.Values;

namespace Rask.Outbox;

/// <summary>
/// A persisted domain event awaiting (or having completed) publication. Written in the same transaction as
/// the change that raised it and drained by the <see cref="OutboxProcessor{TContext}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Names neither <see cref="ISoftDeletes"/> nor <see cref="IVersioned"/>, deliberately. Soft delete would bring a
/// global "not deleted" query filter, which would hide rows from the drain query and turn the dashboard's purge —
/// an <c>ExecuteDelete</c> — into a stamp that leaves them in the table forever. A <c>Version</c> would be a
/// second concurrency token beside <see cref="ClaimToken"/>, and the processor advances state with
/// <c>ExecuteUpdate</c>, which never maintains one.
/// </para>
/// <para>
/// Nothing creates one of these from a form, so no form model is generated: it says no <c>IHasForm</c>.
/// </para>
/// </remarks>
public sealed class OutboxMessage : Aggregate<Guid>, IHasUpdatedAt, IInfrastructure
{
    private OutboxMessage()
    {
    }

    /// <summary>The event's registered type name (see <see cref="OutboxSerializerRegistry"/>).</summary>
    public string Type { get; private set; } = "";

    /// <summary>
    ///     The one durable handler this row runs, as <c>CqrsRegistry</c> names it. Every durable handler of an event
    ///     gets its own row, so each is retried, dead-lettered and counted on its own. <c>null</c> on a row written
    ///     before handlers chose durability, which runs every handler of its event.
    /// </summary>
    public string? Handler { get; private set; }

    /// <summary>The JSON-serialized event payload.</summary>
    public string Payload { get; private set; } = "";

    /// <summary>When the event was enqueued.</summary>
    public Timestamp OccurredAt { get; private set; } = null!;

    /// <summary>When the event was successfully published, or <c>null</c> while it is pending.</summary>
    public Timestamp? ProcessedAt { get; private set; }

    /// <summary>How many publish attempts have been made.</summary>
#pragma warning disable S1144 // EF materializes the column through the setter; code only moves it with ExecuteUpdate
    public int Attempts { get; private set; }
#pragma warning restore S1144

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
    /// When the current claim expires. Null or in the past means the message is claimable — which is
    /// also how a processor that died mid-dispatch releases its work: the lease simply runs out.
    /// </summary>
    public Timestamp? ClaimedUntil { get; private set; }

    /// <summary>
    /// The user the event was raised for — <c>Current.UserId</c> at the time — or <c>null</c> when it was
    /// raised for nobody.
    /// </summary>
    /// <remarks>
    /// The processor re-enters it with <c>Current.UseUser</c> before running the handler, exactly as a job does.
    /// </remarks>
    public Guid? UserId { get; private set; }

    /// <summary>
    /// The tenant the event was raised for — the tenant in flight at the time — or <c>null</c> when it
    /// belongs to nobody.
    /// </summary>
    /// <remarks>
    /// Recorded as data, not as a partition: the processor sees every tenant's events, and
    /// re-enters this one with <c>Tenant.Use</c> before running the handler.
    /// </remarks>
    public Guid? TenantId { get; private set; }

    /// <summary>Enqueues <paramref name="payload" /> for publication.</summary>
    /// <param name="type">The event's registered type name.</param>
    /// <param name="payload">The serialized event.</param>
    /// <param name="handler">The durable handler this row runs (see <see cref="Handler"/>).</param>
    /// <param name="occurredAt">When the event was raised.</param>
    /// <remarks>
    ///     <para>The key is the store's, and is also the processing order, so nothing assigns one here.</para>
    ///     <para>
    ///         The row records the tenant and the user it was enqueued for, so the processor can re-enter
    ///         them before publishing. Null when there is none — a message raised by the host itself belongs to nobody,
    ///         and refusing to write it would be wrong.
    ///     </para>
    /// </remarks>
    public static OutboxMessage For(string type, string payload, string handler, Timestamp occurredAt)
    {
        return new OutboxMessage
        {
            Id = OrderedGuid.New(),
            Type = type,
            Payload = payload,
            Handler = handler,
            OccurredAt = occurredAt,
            UserId = CurrentUser.Id,
            TenantId = Tenant.Key,
        };
    }

    /// <summary>Records a successful publish, clearing any error from an earlier attempt.</summary>
    /// <param name="at">When it was published.</param>
    public void Published(Timestamp at)
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
