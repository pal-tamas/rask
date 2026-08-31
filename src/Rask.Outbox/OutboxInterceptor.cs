using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rask.Data;

namespace Rask.Outbox;

/// <summary>
/// Before each save, drains every tracked entity's <see cref="IOutboxEvent"/> domain events into
/// <see cref="OutboxMessage"/> rows on the same <see cref="DbContext"/> — so the events commit in the same
/// transaction as the change that raised them (atomic; a rolled-back change writes no messages). The
/// background <see cref="OutboxProcessor{TContext}"/> publishes them afterwards.
/// </summary>
/// <remarks>
/// Registered by <see cref="RaskOutboxServiceCollectionExtensions.AddRaskOutbox{TContext}"/>, which also
/// claims domain-event delivery for the outbox — so Rask.Data's in-process publisher stands down on its
/// own and events are not delivered twice. Nothing to disable by hand.
/// </remarks>
public sealed class OutboxInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Enqueue(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Enqueue(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Enqueue(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Materialize first — adding OutboxMessage rows below mutates the ChangeTracker.
        foreach (var entry in context.ChangeTracker.Entries<IHasDomainEvents>().ToList())
        {
            var events = entry.Entity.DomainEvents.OfType<IOutboxEvent>().ToList();
            if (events.Count == 0)
            {
                continue;
            }

            foreach (var domainEvent in events)
            {
                var (type, payload) = OutboxSerializerRegistry.Serialize(domainEvent);
                context.Add(new OutboxMessage { Type = type, Payload = payload, OccurredAt = now });
            }

            // The outbox owns these events now; clear them so the in-process publisher (if any) skips them.
            entry.Entity.ClearDomainEvents();
        }
    }
}
