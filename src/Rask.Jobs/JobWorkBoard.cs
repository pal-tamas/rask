using Microsoft.EntityFrameworkCore;
using Rask.Values;

namespace Rask.Background;

/// <summary>
///     The rows behind a user's bell (<see cref="IWorkBoard" />): in their tenant, the jobs they enqueued <c>.Noticed(…)</c> and
///     the ones a colleague enqueued <c>.ByTenant()</c> of a kind whose handler's <c>[Authorize]</c> admits them — that wait,
///     run or failed for good lately, and the ones that ended in the last day — in one read, on the <c>(UserId, ProcessedAt)</c>
///     and <c>(TenantId, ProcessedAt)</c> indexes. Registered per scope, so it answers for the session that asks.
/// </summary>
/// <typeparam name="TContext">The application <see cref="DbContext" /> that owns the jobs table.</typeparam>
internal sealed class JobWorkBoard<TContext>(
    IDbContextFactory<TContext> contextFactory,
    IServiceProvider scope,
    JobsOptions options,
    TimeProvider timeProvider) : IWorkBoard
    where TContext : DbContext
{
    /// <summary>How long ended work stays on the board.</summary>
    internal static TimeSpan Recently => TimeSpan.FromDays(1);

    /// <summary>How many ended pieces of work the board shows at most, newest first: a batch counts once.</summary>
    internal const int EndedShown = WorkShown.EndedShown;

    // Running work is everything that runs; this only bounds a tenant that enqueued thousands at once.
    private const int MostRows = 100;

    /// <inheritdoc />
    public TimeSpan Every => options.PollInterval;

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkReported>> Mine(string? lane, CancellationToken cancellationToken)
    {
        // Who asks is the session's, read from its own scope: a query's fetch may run where no scope is ambient.
        if (await WorkAccess.Viewer(scope, cancellationToken).ConfigureAwait(false) is not { } viewer)
        {
            return [];
        }

        var user = viewer.User;
        var tenant = viewer.Tenant;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var since = Timestamp.Create(now - Recently);
        var max = options.MaxAttempts;

        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);
        var rows = await db.Set<Job>()
            .AsNoTracking()
            .Where(j => j.Notice != null
                && j.TenantId == tenant
                && (j.UserId == user || (tenant != null && j.Shared && j.UserId != null))
                && (lane == null || j.Lane == lane)
                && ((j.ProcessedAt == null && (j.Attempts < max || j.StartedAt >= since)) || j.ProcessedAt >= since))
            .OrderByDescending(j => j.Id)
            .Take(MostRows)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var work = rows.Select(job => Read(job, max, now) with { Mine = job.UserId == user }).Where(viewer.May).ToList();
        return WorkShown.Of(work);
    }

    // The row as its reader sees it — with the latest report the host running it holds, fresher than the row's last poll.
    private WorkReported Read(Job job, int max, DateTime now)
    {
        var state = JobWork.StateOf(job, max, now);
        var at = job.ProcessedAt ?? job.StartedAt ?? job.RunAt;
        var work = JobWork.Of(job, state, at.Instant.UtcDateTime);
        return state == WorkState.Running && options.Signals.Run(job.Id) is { HasEnded: false } here && here.Latest != default
            ? work with { Progress = here.Latest.Progress, Done = here.Latest.Done, Of = here.Latest.Of }
            : work;
    }
}
