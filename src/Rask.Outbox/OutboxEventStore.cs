using Microsoft.EntityFrameworkCore;
using Rask.Cqrs;

namespace Rask.Outbox;

/// <summary>
/// Stores an event published straight through the dispatcher — not raised on an aggregate, so there is no save to ride
/// in — for its durable handlers: its rows are written in a small transaction of their own, then the processor is woken.
/// Crash-safe and retried from that moment, just not atomic with any other change, since there is none.
/// </summary>
internal sealed class OutboxEventStore<TContext>(
    IDbContextFactory<TContext> contexts, TimeProvider timeProvider, OutboxSignal signal) : IDurableEventStore
    where TContext : DbContext
{
    public async Task Store(IEvent e, IReadOnlyList<string> handlers, CancellationToken cancellationToken)
    {
        var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            db.AddRange(OutboxInterceptor.Rows(e, handlers, timeProvider.GetUtcNow().UtcDateTime));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        signal.Wake();
    }
}
