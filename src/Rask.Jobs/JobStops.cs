using Microsoft.EntityFrameworkCore;

namespace Rask.Background;

/// <summary>
///     Stops one job by its row id, from any host: a job still waiting ends stopped at once and never runs; a running one is
///     marked, and the host running it cancels it — at once when that is this host, at its next look otherwise. Both are a
///     single conditional write, so of two stops — or a stop and a limit — the first one stands.
/// </summary>
/// <typeparam name="TContext">The application <see cref="DbContext" /> that owns the jobs table.</typeparam>
internal sealed class JobStops<TContext>(IDbContextFactory<TContext> contextFactory, TimeProvider timeProvider, JobsOptions options)
    where TContext : DbContext
{
    public async Task<bool> Stop(long id, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);

        // Waiting — nobody holds it — so it ends here, stopped. The claim's own predicate re-reads the row, so a host
        // claiming it at this moment either takes it first (and this write misses) or finds it processed.
        var waiting = await db.Set<Job>()
            .Where(j => j.Id == id
                && j.ProcessedAt == null
                && j.Stopped == null
                && (j.ClaimedUntil == null || j.ClaimedUntil <= now))
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(j => j.Stopped, JobStop.Asked)
                    .SetProperty(j => j.ProcessedAt, now)
                    .SetProperty(j => j.WaitingKey, (string?)null),
                cancellationToken)
            .ConfigureAwait(false);
        if (waiting > 0)
        {
            options.Signals.Saved(id);
            return true;
        }

        // Running somewhere: marked, for the host that runs it.
        var running = await db.Set<Job>()
            .Where(j => j.Id == id && j.ProcessedAt == null && j.Stopped == null)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Stopped, JobStop.Asked), cancellationToken)
            .ConfigureAwait(false);
        if (running == 0)
        {
            return false; // done, stopped already, or no such job
        }

        options.Signals.Run(id)?.TryStop(JobStop.Asked);
        return true;
    }
}
