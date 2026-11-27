using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Rask.Mailing;
using Rask.Values;

namespace Rask.Dashboard.Panels;

/// <summary>Queued email — <see cref="QueuedMail"/>.</summary>
internal sealed class MailQueuePanel<TContext>(
    IDbContextFactory<TContext> contextFactory,
    TimeProvider timeProvider,
    IServiceProvider services)
    : QueuePanelBase<TContext, QueuedMail>(contextFactory, timeProvider)
    where TContext : DbContext
{
    private readonly MailOptions? _options = services.GetService<MailOptions>();

    public override string Slug => "mail";

    public override string Title => "Mail";

    public override Ui.IconName Icon => Ui.IconName.Envelope;

    public override int MaxAttempts => _options?.MaxAttempts ?? 0;

    protected override bool IsRegistered => _options is not null;

    protected override Expression<Func<QueuedMail, bool>> Processed => m => m.ProcessedAt != null;

    // For mail the useful "type" is the subject and the useful summary is who it went to — the two things
    // you scan a mail log for. Recipients are stored as a JSON array; the detail page deserializes it
    // properly, the list just shows the raw string. The row shows its moments as the UTC instants they hold.
    protected override Expression<Func<QueuedMail, QueueRow>> Projection => m => new QueueRow(
        m.Id,
        m.Subject,
        m.To,
        m.CreatedAt.Instant.UtcDateTime,
        m.RunAt.Instant.UtcDateTime,
        m.ProcessedAt == null ? null : m.ProcessedAt.Instant.UtcDateTime,
        m.Attempts,
        m.Error,
        m.To);

    protected override Expression<Func<QueuedMail, bool>> ProcessedBefore(DateTime cutoff)
    {
        var before = Timestamp.Create(cutoff);
        return m => m.ProcessedAt != null && m.ProcessedAt < before;
    }

    protected override Expression<Func<QueuedMail, bool>> RunsBy(DateTime now)
    {
        var at = Timestamp.Create(now);
        return m => m.RunAt <= at;
    }

    protected override IOrderedQueryable<QueuedMail> InRunOrder(IQueryable<QueuedMail> rows) =>
        rows.OrderBy(m => m.RunAt).ThenBy(m => m.Id);

    protected override void Reschedule(UpdateSettersBuilder<QueuedMail> setters, DateTime now) =>
        setters.SetProperty(m => m.RunAt, Timestamp.Create(now));
}
