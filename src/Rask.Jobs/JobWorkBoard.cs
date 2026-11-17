using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Background;

/// <summary>
///     The rows behind a user's bell (<see cref="IWorkBoard" />): the jobs they enqueued <c>.Noticed(…)</c> in their tenant
///     that wait, run or failed for good lately, and the ones that ended in the last day — in one read, on the
///     <c>(UserId, ProcessedAt)</c> index. Registered per scope, so it answers for the session that asks.
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

    /// <summary>How many ended jobs the board shows at most, newest first.</summary>
    internal const int EndedShown = 10;

    // Running work is everything that runs; this only bounds a user who enqueued thousands at once.
    private const int MostRows = 100;

    /// <inheritdoc />
    public TimeSpan Every => options.PollInterval;

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkReported>> Mine(string? lane, CancellationToken cancellationToken)
    {
        // Who asks is the session's, read from its own scope: a query's fetch may run where no scope is ambient.
        if (CurrentUser.UserIdIn(scope) is not { } user)
        {
            return [];
        }

        var tenant = Tenant.KeyIn(scope);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var since = now - Recently;
        var max = options.MaxAttempts;

        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);
        var rows = await db.Set<Job>()
            .AsNoTracking()
            .Where(j => j.UserId == user
                && j.Notice != null
                && j.TenantId == tenant
                && (lane == null || j.Lane == lane)
                && ((j.ProcessedAt == null && (j.Attempts < max || j.StartedAt >= since)) || j.ProcessedAt >= since))
            .OrderByDescending(j => j.Id)
            .Take(MostRows)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var work = rows.Select(job => Read(job, max, now)).ToList();
        return [.. work.Where(w => !w.HasEnded), .. work.Where(w => w.HasEnded).OrderByDescending(w => w.At).Take(EndedShown)];
    }

    // The row as its enqueuer sees it — with the latest report the host running it holds, fresher than the row's last poll.
    private WorkReported Read(Job job, int max, DateTime now)
    {
        var state = JobWork.StateOf(job, max, now);
        var at = job.ProcessedAt ?? job.StartedAt ?? job.RunAt;
        var work = JobWork.Of(job, state, at);
        return state == WorkState.Running && options.Signals.Run(job.Id) is { HasEnded: false } here && here.Latest != default
            ? work with { Progress = here.Latest.Progress, Done = here.Latest.Done, Of = here.Latest.Of }
            : work;
    }
}
