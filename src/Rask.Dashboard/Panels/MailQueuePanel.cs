using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Mailing;

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

    // For mail the useful "type" is the subject and the useful summary is who it went to — the two things
    // you scan a mail log for. Recipients are stored as a JSON array; the detail page deserializes it
    // properly, the list just shows the raw string.
    protected override string RunAtProperty => nameof(QueuedMail.RunAt);

    protected override Expression<Func<QueuedMail, QueueRow>> Projection => m => new QueueRow(
        m.Id, m.Subject, m.To, m.CreatedAt, m.RunAt, m.ProcessedAt, m.Attempts, m.Error, m.To);
}
