using Microsoft.EntityFrameworkCore;

namespace Rask.Background;

/// <summary>
///     Ends the jobs whose host died on their last attempt. A host that dies mid-job leaves its row claimed until the lease
///     runs out, and the next claim runs it again — but the claim counts the attempt up front, so a job that took its host
///     down on its last one is never claimed again, and nobody ever wrote how it ended: it would read as running for ever,
///     in the bell and to a <c>Jobs.Run</c> awaiting it. The sweep makes it the dead letter it is, with an error saying why.
/// </summary>
/// <remarks>
///     Plain conditional writes, the same on every provider. The rows are marked with the sweep's own token before they are
///     read, so of two hosts sweeping at once only the one whose mark landed says their end.
/// </remarks>
/// <typeparam name="TContext">The application <see cref="DbContext" /> that owns the jobs table.</typeparam>
internal sealed class JobSweep<TContext>(IDbContextFactory<TContext> contextFactory, TimeProvider timeProvider, JobsOptions options)
    where TContext : DbContext
{
    /// <summary>The error a swept job is left with: what the dashboard and an awaiting caller read.</summary>
    internal const string Abandoned =
        "The host running it stopped before it ended (its lease ran out), and it had no attempt left — retry it from the dashboard.";

    /// <summary>Sweeps once: the rows it ended, read after the write, for the caller to log and say.</summary>
    public async Task<IReadOnlyList<Job>> Sweep(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var maxAttempts = options.MaxAttempts;
        var token = Guid.NewGuid();
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);

        // Claimed (a token), out of lease, no attempt left, never ended: only a host that died leaves a row so. A row this
        // write marked has no lease, so a mark left by a sweep that died before its release below is swept again.
        var marked = await db.Set<Job>()
            .Where(j => j.ProcessedAt == null
                && j.Stopped == null
                && j.Attempts >= maxAttempts
                && j.ClaimToken != null
                && (j.ClaimedUntil == null || j.ClaimedUntil <= now))
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(j => j.ClaimToken, token)
                    .SetProperty(j => j.ClaimedUntil, (DateTime?)null)
                    .SetProperty(j => j.Error, Abandoned),
                cancellationToken)
            .ConfigureAwait(false);
        if (marked == 0)
        {
            return [];
        }

        var ended = await db.Set<Job>()
            .AsNoTracking()
            .Where(j => j.ProcessedAt == null && j.ClaimToken == token)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Released: nobody holds a dead letter, and a caller awaiting it reads an error on a row nobody holds as its end.
        await db.Set<Job>()
            .Where(j => j.ProcessedAt == null && j.ClaimToken == token)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.ClaimToken, (Guid?)null), cancellationToken)
            .ConfigureAwait(false);
        return ended;
    }
}
