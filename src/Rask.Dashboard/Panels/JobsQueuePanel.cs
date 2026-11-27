using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Rask.Background;

namespace Rask.Dashboard.Panels;

/// <summary>Background jobs — <see cref="Job"/>.</summary>
internal sealed class JobsQueuePanel<TContext>(
    IDbContextFactory<TContext> contextFactory,
    TimeProvider timeProvider,
    IServiceProvider services)
    : QueuePanelBase<TContext, Job>(contextFactory, timeProvider), IJobsQueuePanel
    where TContext : DbContext
{
    private readonly JobsOptions? _options = services.GetService<JobsOptions>();

    public override string Slug => "jobs";

    public override string Title => "Jobs";

    public override Ui.IconName Icon => Ui.IconName.Cog6Tooth;

    public override int MaxAttempts => _options?.MaxAttempts ?? 0;

    protected override bool IsRegistered => _options is not null;

    protected override Expression<Func<Job, bool>> Processed => j => j.ProcessedAt != null;

    protected override bool ShowsRunning => true;

    protected override Expression<Func<Job, QueueRow>> Projection => j => new QueueRow(
        j.Id, j.Type, j.Payload, j.CreatedAt, j.RunAt, j.ProcessedAt, j.Attempts, j.Error, j.Payload)
    {
        Lane = j.Lane,
        StartedAt = j.StartedAt,
        ClaimedUntil = j.ClaimedUntil,
        Stopped = j.Stopped,
    };

    protected override Expression<Func<Job, bool>> ProcessedBefore(DateTime cutoff) =>
        j => j.ProcessedAt != null && j.ProcessedAt < cutoff;

    protected override Expression<Func<Job, bool>> RunsBy(DateTime now) => j => j.RunAt <= now;

    protected override IOrderedQueryable<Job> InRunOrder(IQueryable<Job> rows) =>
        rows.OrderBy(j => j.RunAt).ThenBy(j => j.Id);

    protected override void Reschedule(UpdateSettersBuilder<Job> setters, DateTime now) =>
        setters.SetProperty(j => j.RunAt, now);

    // Running is what the processor's own lane check calls busy: claimed, unexpired, unprocessed.
    protected override IQueryable<Job> OnlyRunning(IQueryable<Job> rows, DateTime now) =>
        rows.Where(j => j.ProcessedAt == null && j.ClaimedUntil != null && j.ClaimedUntil > now);

    protected override IQueryable<Job> WithoutRunning(IQueryable<Job> rows, DateTime now) =>
        rows.Where(j => j.ProcessedAt != null || j.ClaimedUntil == null || j.ClaimedUntil <= now);

    // Through the queue itself, so the host running the job cancels it at once when it is this one.
    public Task<bool> Stop(Guid id, CancellationToken cancellationToken) =>
        services.GetRequiredService<IJobs>().Stop(id, cancellationToken);
}
