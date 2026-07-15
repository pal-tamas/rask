using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
/// Publishes each aggregate's <see cref="IHasDomainEvents.DomainEvents"/> in-process <b>after</b> the change
/// commits, through <c>Rask.Cqrs</c>' <see cref="IDispatcher.PublishAsync{TNotification}"/>. Events are
/// resolved by their runtime type, so a stored <see cref="INotificationHandler{TNotification}"/> reacts with
/// no extra wiring. Handlers run in a fresh DI scope. Not wired when a transactional outbox owns delivery —
/// see <see cref="RaskDataOptions.DispatchDomainEventsInProcess"/>.
/// </summary>
public sealed class DomainEventInterceptor(IServiceScopeFactory scopeFactory) : SaveChangesInterceptor
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    /// <inheritdoc/>
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        DispatchAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc/>
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await DispatchAsync(eventData.Context, cancellationToken).ConfigureAwait(false);
        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task DispatchAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            return;
        }

        // Drain the events off the tracked aggregates first, so a handler that saves again (re-entering this
        // interceptor) doesn't re-publish the same events.
        var events = new List<INotification>();
        foreach (var entry in context.ChangeTracker.Entries<IHasDomainEvents>())
        {
            if (entry.Entity.DomainEvents.Count == 0)
            {
                continue;
            }

            events.AddRange(entry.Entity.DomainEvents);
            entry.Entity.ClearDomainEvents();
        }

        if (events.Count == 0)
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        foreach (var domainEvent in events)
        {
            // PublishAsync resolves handlers by the event's concrete runtime type, so the INotification
            // static type here is fine.
            await dispatcher.PublishAsync(domainEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}
