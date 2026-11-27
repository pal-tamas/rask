using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Rask.Outbox;
using Rask.Values;

namespace Rask.Dashboard.Panels;

/// <summary>Domain events awaiting publication — <see cref="OutboxMessage"/>.</summary>
internal sealed class OutboxQueuePanel<TContext>(
    IDbContextFactory<TContext> contextFactory,
    TimeProvider timeProvider,
    IServiceProvider services)
    : QueuePanelBase<TContext, OutboxMessage>(contextFactory, timeProvider)
    where TContext : DbContext
{
    private readonly OutboxOptions? _options = services.GetService<OutboxOptions>();

    public override string Slug => "outbox";

    public override string Title => "Outbox";

    public override Ui.IconName Icon => Ui.IconName.PaperAirplane;

    public override int MaxAttempts => _options?.MaxAttempts ?? 0;

    protected override bool IsRegistered => _options is not null;

    protected override Expression<Func<OutboxMessage, bool>> Processed => m => m.ProcessedAt != null;

    // The row shows its moments as the UTC instants they hold.
    protected override Expression<Func<OutboxMessage, QueueRow>> Projection => m => new QueueRow(
        m.Id,
        m.Type,
        m.Payload,
        m.OccurredAt.Instant.UtcDateTime,
        m.OccurredAt.Instant.UtcDateTime,
        m.ProcessedAt == null ? null : m.ProcessedAt.Instant.UtcDateTime,
        m.Attempts,
        m.Error,
        m.Payload);

    protected override Expression<Func<OutboxMessage, bool>> ProcessedBefore(DateTime cutoff)
    {
        var before = Timestamp.Create(cutoff);
        return m => m.ProcessedAt != null && m.ProcessedAt < before;
    }

    // The outbox has no scheduled-run column: an event is eligible the moment it is written, and a retry
    // is immediate rather than backed off. OccurredAt stands in for both so the shared projection holds —
    // which correctly leaves the Delayed count permanently zero for this queue.
    protected override Expression<Func<OutboxMessage, bool>> RunsBy(DateTime now)
    {
        var at = Timestamp.Create(now);
        return m => m.OccurredAt <= at;
    }

    protected override IOrderedQueryable<OutboxMessage> InRunOrder(IQueryable<OutboxMessage> rows) =>
        rows.OrderBy(m => m.OccurredAt).ThenBy(m => m.Id);

    // OccurredAt is when the event happened, which a retry does not change: rewritten, the dashboard and the purge's
    // retention would date a months-old event to the moment someone pressed Retry.
    protected override void Reschedule(UpdateSettersBuilder<OutboxMessage> setters, DateTime now)
    {
    }
}
