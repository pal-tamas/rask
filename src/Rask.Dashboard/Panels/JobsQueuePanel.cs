using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Rask.Background;
using Rask.Values;

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

    protected override string RunAtProperty => nameof(Job.RunAt);

    protected override bool ShowsRunning => true;

    // The row's timestamps, as the UTC instants the shared row shape holds (read after the query, in the projection).
    protected override Expression<Func<Job, QueueRow>> Projection => j => new QueueRow(
        j.Id, j.Type, j.Payload, j.CreatedAt, Utc(j.RunAt), UtcOrNull(j.ProcessedAt), j.Attempts, j.Error, j.Payload)
    {
        Lane = j.Lane,
        StartedAt = UtcOrNull(j.StartedAt),
        ClaimedUntil = UtcOrNull(j.ClaimedUntil),
        Stopped = j.Stopped,
    };

    protected override Expression<Func<Job, bool>> Outstanding => j => j.ProcessedAt == null;

    protected override Expression<Func<Job, bool>> ProcessedBefore(DateTime cutoff)
    {
        var before = Timestamp.Create(cutoff);
        return j => j.ProcessedAt != null && j.ProcessedAt < before;
    }

    protected override Expression<Func<Job, bool>> DueBy(DateTime now)
    {
        var at = Timestamp.Create(now);
        return j => j.RunAt <= at;
    }

    protected override IOrderedQueryable<Job> InRunOrder(IQueryable<Job> rows) => rows.OrderBy(j => j.RunAt).ThenBy(j => j.Id);

    protected override void Reschedule(UpdateSettersBuilder<Job> setters, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(setters);
        setters.SetProperty(j => j.RunAt, Timestamp.Create(now));
    }

    // Running is what the processor's own lane check calls busy: claimed, unexpired, unprocessed.
    protected override IQueryable<Job> OnlyRunning(IQueryable<Job> rows, DateTime now)
    {
        var at = Timestamp.Create(now);
        return rows.Where(j => j.ProcessedAt == null && j.ClaimedUntil != null && j.ClaimedUntil > at);
    }

    protected override IQueryable<Job> WithoutRunning(IQueryable<Job> rows, DateTime now)
    {
        var at = Timestamp.Create(now);
        return rows.Where(j => j.ProcessedAt != null || j.ClaimedUntil == null || j.ClaimedUntil <= at);
    }

    // Through the queue itself, so the host running the job cancels it at once when it is this one.
    public Task<bool> Stop(Guid id, CancellationToken cancellationToken) =>
        services.GetRequiredService<IJobs>().Stop(id, cancellationToken);

    private static DateTime Utc(Timestamp timestamp) => timestamp.Instant.UtcDateTime;

    private static DateTime? UtcOrNull(Timestamp? timestamp) => timestamp?.Instant.UtcDateTime;
}
