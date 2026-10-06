using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Background;

namespace Rask.Dashboard.Panels;

/// <summary>Background jobs — <see cref="Job"/>.</summary>
internal sealed class JobsQueuePanel<TContext>(
    IDbContextFactory<TContext> contextFactory,
    TimeProvider timeProvider,
    IServiceProvider services)
    : QueuePanelBase<TContext, Job>(contextFactory, timeProvider)
    where TContext : DbContext
{
    private readonly JobsOptions? _options = services.GetService<JobsOptions>();

    public override string Slug => "jobs";

    public override string Title => "Jobs";

    public override Ui.IconName Icon => Ui.IconName.Cog6Tooth;

    public override int MaxAttempts => _options?.MaxAttempts ?? 0;

    protected override bool IsRegistered => _options is not null;

    protected override string RunAtProperty => nameof(Job.RunAt);

    protected override Expression<Func<Job, QueueRow>> Projection => j => new QueueRow(
        j.Id, j.Type, j.Payload, j.CreatedAt, j.RunAt, j.ProcessedAt, j.Attempts, j.Error, j.Payload);
}
