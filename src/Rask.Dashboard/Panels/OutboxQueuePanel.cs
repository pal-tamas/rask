using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Outbox;

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

    public override Ui.IconName Icon => Ui.IconName.Outbox;

    public override int MaxAttempts => _options?.MaxAttempts ?? 0;

    protected override bool IsRegistered => _options is not null;

    // The outbox has no scheduled-run column: an event is eligible the moment it is written, and a retry
    // is immediate rather than backed off. OccurredAt stands in for both so the shared projection holds —
    // which correctly leaves the Delayed count permanently zero for this queue.
    protected override string RunAtProperty => nameof(OutboxMessage.OccurredAt);

    protected override Expression<Func<OutboxMessage, QueueRow>> Projection => m => new QueueRow(
        m.Id, m.Type, m.Payload, m.OccurredAt, m.OccurredAt, m.ProcessedAt, m.Attempts, m.Error, m.Payload);
}
