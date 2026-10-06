using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;
using Rask.Data;

namespace Rask.Outbox;

/// <summary>
/// Before each save, writes one <see cref="OutboxMessage"/> per <see cref="IDurableHandler{TEvent}"/> of every event the
/// tracked entities raised, on the same <see cref="DbContext"/> — so the rows commit in the same transaction as the
/// change that raised them (atomic; a rolled-back change writes none). Once they commit, the
/// <see cref="OutboxProcessor{TContext}"/> is woken to run them.
/// </summary>
/// <remarks>
/// It leaves the events on the entities: Rask.Data's <see cref="DomainEventInterceptor"/> publishes the same events to
/// their in-memory handlers after the commit and clears them then, so the two read the same list whichever runs first.
/// Each event it stored is marked, so that publish does not store it a second time.
/// </remarks>
public sealed class OutboxInterceptor : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly TimeProvider _timeProvider;
    private readonly OutboxSignal _signal;
    private readonly IServiceScopeFactory _scopes;

    // The contexts whose save wrote rows, until those rows are committed and the processor woken.
    private readonly ConditionalWeakTable<DbContext, object> _wrote = new();

    internal OutboxInterceptor(TimeProvider timeProvider, OutboxSignal signal, IServiceScopeFactory scopes)
    {
        _timeProvider = timeProvider;
        _signal = signal;
        _scopes = scopes;
    }

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

    /// <inheritdoc/>
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        WakeIfCommitted(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        WakeIfCommitted(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc/>
    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData.Context);

    /// <inheritdoc/>
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    // A save inside the caller's own transaction commits later than SavedChanges: wake then, not before, or the
    // processor looks, finds rows it cannot see yet, and waits a whole poll.
    void IDbTransactionInterceptor.TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        Wake(eventData.Context);

    Task IDbTransactionInterceptor.TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken)
    {
        Wake(eventData.Context);
        return Task.CompletedTask;
    }

    void IDbTransactionInterceptor.TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        Forget(eventData.Context);

    Task IDbTransactionInterceptor.TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    private void Enqueue(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var rows = new List<OutboxMessage>();
        foreach (var entity in context.ChangeTracker.Entries<IHasDomainEvents>().Select(static entry => entry.Entity))
        {
            foreach (var e in entity.DomainEvents)
            {
                var handlers = CqrsRegistry.DurableHandlersOf(e.GetType());
                if (handlers.Count > 0)
                {
                    // A test's fake takes the event in place of the rows.
                    if (Cqrs.Outbox.Faked.Value is { } fake)
                    {
                        fake.Record(e, handlers, _scopes);
                    }
                    else
                    {
                        rows.AddRange(Rows(e, handlers, now));
                    }

                    // So the publish after the commit runs only the in-memory handlers.
                    DurableEvents.MarkStored(e);
                }
            }
        }

        // Added after the walk: adding rows mutates the ChangeTracker being enumerated.
        if (rows.Count > 0)
        {
            context.AddRange(rows);
            _wrote.AddOrUpdate(context, _wrote);
        }
    }

    /// <summary>One row per durable handler of <paramref name="e"/>.</summary>
    internal static List<OutboxMessage> Rows(IEvent e, IReadOnlyList<string> handlers, DateTime now)
    {
        var (type, payload) = OutboxSerializerRegistry.Serialize(e);
        return [.. handlers.Select(handler => OutboxMessage.For(type, payload, handler, now))];
    }

    private void WakeIfCommitted(DbContext? context)
    {
        if (context?.Database.CurrentTransaction is null)
        {
            Wake(context);
        }
    }

    private void Wake(DbContext? context)
    {
        if (context is not null && _wrote.Remove(context))
        {
            _signal.Wake();
        }
    }

    private void Forget(DbContext? context)
    {
        if (context is not null)
        {
            _wrote.Remove(context);
        }
    }
}
