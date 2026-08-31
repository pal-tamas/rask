using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
/// Publishes each entity's <see cref="IHasDomainEvents.DomainEvents"/> in-process <b>after</b> the change
/// commits, through <c>Rask.Cqrs</c>' <see cref="IDispatcher.PublishAsync{TNotification}"/>. Events are
/// resolved by their runtime type, so a stored <see cref="INotificationHandler{TNotification}"/> reacts with
/// no extra wiring. Handlers run in a fresh DI scope. Stands down automatically when a transactional
/// outbox owns delivery — see <see cref="RaskDataOptions.DispatchDomainEventsInProcess"/>.
/// </summary>
/// <remarks>
/// Events are drained off the tracked entities in <c>SavingChanges</c> (before a delete detaches its
/// entity) and published in <c>SavedChanges</c> (after the change commits). A failed save discards them, so a
/// rolled-back change never fires its events.
/// </remarks>
public sealed class DomainEventInterceptor : SaveChangesInterceptor
{
    private readonly IServiceScopeFactory _scopeFactory;

    // Resolved once, when the container builds this singleton — which is the whole point. Registration
    // order in Program.cs cannot reach it, so AddRaskData before or after AddRaskOutbox behaves the same.
    private readonly bool _standDown;

    // Events collected pre-save, keyed by the context whose SaveChanges is in flight (a context runs one
    // save at a time, so a per-context slot is safe; the weak table never keeps a context alive).
    private readonly ConditionalWeakTable<DbContext, List<INotification>> _pending = new();

    /// <summary>
    /// Creates the interceptor, deciding from the built container whether anything else already owns
    /// domain-event delivery.
    /// </summary>
    /// <param name="scopeFactory">Used to run each handler in a fresh scope.</param>
    /// <param name="options">
    /// Rask.Data's options. <see cref="RaskDataOptions.DispatchDomainEventsInProcess"/> overrides the
    /// automatic answer in both directions when it is not <c>null</c>.
    /// </param>
    /// <param name="deliveryOwners">
    /// Everything that has claimed ownership of delivery (<c>Rask.Outbox</c> registers one). A non-empty
    /// sequence makes this interceptor a no-op, so it cannot drain events the outbox has not copied yet.
    /// </param>
    public DomainEventInterceptor(
        IServiceScopeFactory scopeFactory,
        RaskDataOptions options,
        IEnumerable<IDomainEventDeliveryOwner> deliveryOwners)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(deliveryOwners);

        _scopeFactory = scopeFactory;
        _standDown = options.DispatchDomainEventsInProcess is { } dispatch
            ? !dispatch
            : deliveryOwners.Any();
    }

    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc/>
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        PublishAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc/>
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await PublishAsync(eventData.Context, cancellationToken).ConfigureAwait(false);
        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Discard(eventData.Context);

    /// <inheritdoc/>
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Discard(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void Collect(DbContext? context)
    {
        // The only guard that matters, and it has to be here rather than at the publish step: collecting
        // is what CLEARS the events off the entities. An outbox copies them in its own interceptor, so
        // draining them here would empty the outbox rather than merely double-deliver. Nothing is
        // collected, so nothing is pending, so the publish path is a no-op too.
        if (_standDown || context is null)
        {
            return;
        }

        // Drain the events off the tracked entities now — a Deleted entity is detached once the save
        // completes, so collecting after SaveChanges would lose its events.
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

        if (events.Count > 0)
        {
            _pending.AddOrUpdate(context, events);
        }
    }

    private async Task PublishAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null || !_pending.TryGetValue(context, out var events))
        {
            return;
        }

        _pending.Remove(context);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        foreach (var domainEvent in events)
        {
            // PublishAsync resolves handlers by the event's concrete runtime type, so the INotification
            // static type here is fine.
            await dispatcher.PublishAsync(domainEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Discard(DbContext? context)
    {
        if (context is not null)
        {
            _pending.Remove(context);
        }
    }
}
