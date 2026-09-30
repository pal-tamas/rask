using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
/// Publishes each entity's <see cref="IHasDomainEvents.DomainEvents"/> <b>after</b> the change commits, through
/// <c>Rask.Cqrs</c>' <see cref="IDispatcher.Publish{TEvent}"/>: every <see cref="IEventHandler{TEvent}"/> runs in
/// memory, in a fresh DI scope. A durable handler (<see cref="IDurableHandler{TEvent}"/>) is not run here — the
/// outbox wrote its row in the save's own transaction, and runs it from there.
/// </summary>
/// <remarks>
/// Events are read off the tracked entities in <c>SavingChanges</c> (before a delete detaches its entity) and
/// published in <c>SavedChanges</c> (after the change commits). They are cleared only then, so the outbox's
/// interceptor, reading the same events in the same <c>SavingChanges</c>, sees them whichever of the two runs
/// first. A failed save discards them, so a rolled-back change never fires its events.
/// </remarks>
public sealed class DomainEventInterceptor(IServiceScopeFactory scopeFactory) : SaveChangesInterceptor
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    // What each context's in-flight save raised, keyed by that context (one save at a time per context, so one slot
    // is safe; the weak table never keeps a context alive). The entities are kept too, because a deleted one is
    // detached by the time SavedChanges runs and could no longer be found to clear.
    private readonly ConditionalWeakTable<DbContext, Pending> _pending = new();

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
        if (context is null)
        {
            return;
        }

        var pending = new Pending();
        foreach (var entity in context.ChangeTracker.Entries<IHasDomainEvents>().Select(static entry => entry.Entity))
        {
            if (entity.DomainEvents.Count == 0)
            {
                continue;
            }

            pending.Entities.Add(entity);
            pending.Events.AddRange(entity.DomainEvents);
        }

        if (pending.Events.Count > 0)
        {
            _pending.AddOrUpdate(context, pending);
        }
    }

    private async Task PublishAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending))
        {
            return;
        }

        _pending.Remove(context);
        pending.Clear();

        var scope = _scopeFactory.CreateAsyncScope();
        await using var scopeScope = scope.ConfigureAwait(false);

        // No AddRaskCqrs, no handlers: there is nothing an event could reach.
        if (scope.ServiceProvider.GetService<IDispatcher>() is not { } dispatcher)
        {
            return;
        }

        foreach (var e in pending.Events)
        {
            // Publish resolves handlers by the event's concrete runtime type, so the IEvent static type is fine.
            await dispatcher.Publish(e, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Discard(DbContext? context)
    {
        if (context is not null && _pending.TryGetValue(context, out var pending))
        {
            _pending.Remove(context);
            pending.Clear();
        }
    }

    private sealed class Pending
    {
        public List<IHasDomainEvents> Entities { get; } = [];

        public List<IEvent> Events { get; } = [];

        public void Clear()
        {
            foreach (var entity in Entities)
            {
                entity.ClearDomainEvents();
            }
        }
    }
}
