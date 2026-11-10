using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Rask.Background;

/// <summary>
///     Looks at the jobs this host runs, once a poll, and stops the ones that must stop: asked to from any host (their row says
///     <see cref="Job.Stopped" />), past their time limit, idle too long, or whose row was marked done or deleted from outside.
///     One read of the rows in flight, by id — so a job enqueued again just like one that ran is never taken for it.
/// </summary>
/// <typeparam name="TContext">The application <see cref="DbContext" /> that owns the jobs table.</typeparam>
internal sealed partial class JobWatch<TContext>(
    IDbContextFactory<TContext> contextFactory,
    TimeProvider timeProvider,
    ILogger logger)
    where TContext : DbContext
{
    /// <summary>How often a job that ignores its stop is said to: once a minute, not once a poll.</summary>
    internal static TimeSpan WarnEvery => TimeSpan.FromMinutes(1);

    /// <summary>Looks at <paramref name="runs" /> once. A failed read stops nothing: the next look tries again.</summary>
    public async Task Look(IReadOnlyCollection<JobRun> runs, CancellationToken cancellationToken)
    {
        if (runs.Count == 0)
        {
            return;
        }

        try
        {
            var rows = await Rows(runs, cancellationToken).ConfigureAwait(false);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            foreach (var run in runs)
            {
                await Look(run, rows.GetValueOrDefault(run.Id), now, cancellationToken).ConfigureAwait(false);
                WarnIfIgnored(run, now);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A database briefly away must not end a job: the jobs run on unwatched until the next look.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            WatchFailed(logger, ex);
        }
    }

    private async Task<Dictionary<long, Row>> Rows(IReadOnlyCollection<JobRun> runs, CancellationToken cancellationToken)
    {
        var ids = runs.Select(run => run.Id).ToList();
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);
        return await db.Set<Job>()
            .AsNoTracking()
            .Where(j => ids.Contains(j.Id))
            .Select(j => new Row(j.Id, j.ProcessedAt, j.Stopped))
            .ToDictionaryAsync(row => row.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task Look(JobRun run, Row? row, DateTime now, CancellationToken cancellationToken)
    {
        if (run.Stopped is not null || run.HasEnded)
        {
            return;
        }

        // Gone, or ended by somebody else: the row is theirs now, so nothing is written over it.
        if (row is null || row.ProcessedAt is not null)
        {
            run.TryStop(JobStop.DoneElsewhere);
            return;
        }

        if (row.Stopped is { } asked)
        {
            run.TryStop(asked);
            return;
        }

        if (run.LimitPassed(now) is { } limit && await Mark(run.Id, limit, cancellationToken).ConfigureAwait(false))
        {
            // Written first, so a Jobs.Stop from any host after this answers false: the first reason stands. When the row
            // refused it, another reason got there first, and the next look reads it.
            run.TryStop(limit);
        }
    }

    // The reason on the row, unless the row already has one or has ended: true when it was this one that was written.
    private async Task<bool> Mark(long id, JobStop why, CancellationToken cancellationToken)
    {
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);
        return await db.Set<Job>()
            .Where(j => j.Id == id && j.ProcessedAt == null && j.Stopped == null)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Stopped, why), cancellationToken)
            .ConfigureAwait(false) > 0;
    }

    // A handler that does not pass Current.Cancellation on keeps running after its stop, and keeps its lane: that is said
    // once a minute, so it is found rather than a lane that seems stuck for no reason.
    private void WarnIfIgnored(JobRun run, DateTime now)
    {
        if (run.StoppedAt is not { } stoppedAt || run.HasEnded || now - stoppedAt < WarnEvery)
        {
            return;
        }

        if (run.WarnedAt is { } warned && now - warned < WarnEvery)
        {
            return;
        }

        run.WarnedAt = now;
        IgnoresStop(logger, run.Id, run.Type, run.Lane, now - stoppedAt);
    }

    private sealed record Row(long Id, DateTime? ProcessedAt, JobStop? Stopped);

    [LoggerMessage(
        EventName = "JobIgnoresStop",
        Level = LogLevel.Warning,
        Message = "Job {Id} ({Type}, lane {Lane}) still runs {Elapsed} after it was stopped; its work does not pass "
            + "Current.Cancellation on, so it holds its lane until it returns.")]
    private static partial void IgnoresStop(ILogger logger, long id, string type, string? lane, TimeSpan elapsed);

    [LoggerMessage(
        EventName = "JobWatchFailed",
        Level = LogLevel.Warning,
        Message = "Could not check the running jobs; they run on unwatched until the next tick.")]
    private static partial void WatchFailed(ILogger logger, Exception exception);
}
