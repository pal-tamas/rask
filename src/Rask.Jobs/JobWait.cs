using Microsoft.EntityFrameworkCore;

namespace Rask.Background;

/// <summary>
///     Waits for an awaited job's row to say how it ended, on whichever host ran it: its answer, or its failure as a
///     <see cref="JobFailedException"/>. Reads the row once a poll, and at once when this process's processor says it saved
///     the outcome. Giving up — the caller's token, or <c>.Within(…)</c> — stops the waiting and leaves the job alone.
/// </summary>
/// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the jobs table.</typeparam>
internal sealed class JobWait<TContext>(IDbContextFactory<TContext> contextFactory, JobsOptions options, TimeProvider timeProvider)
    where TContext : DbContext
{
    /// <summary>The answer of job <paramref name="id"/> as its row keeps it (JSON, or null for a job that answers nothing).</summary>
    public async Task<string?> For(long id, string type, TimeSpan? within, CancellationToken cancellationToken)
    {
        using var deadline = within is { } wait ? new CancellationTokenSource(wait, timeProvider) : null;
        using var linked = deadline is null ? null : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = linked?.Token ?? cancellationToken;
        try
        {
            while (true)
            {
                // Listened for before the read, so an outcome saved between the read and the wait still wakes it.
                var saved = options.Signals.Finished(id);
                if (await Outcome(id, type, token).ConfigureAwait(false) is { Ended: true } outcome)
                {
                    return outcome.Result;
                }

                await Task.WhenAny(saved, Task.Delay(options.PollInterval, timeProvider, token)).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException ex) when (deadline is { IsCancellationRequested: true } && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Job {id} ({type}) did not finish within {within}. It still runs, and keeps its answer on its row; nobody awaits it now.",
                ex);
        }
        finally
        {
            options.Signals.Forget(id);
        }
    }

    private async Task<(bool Ended, string? Result)> Outcome(long id, string type, CancellationToken cancellationToken)
    {
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);
        var row = await db.Set<Job>()
            .AsNoTracking()
            .Where(j => j.Id == id)
            .Select(j => new { j.ProcessedAt, j.Result, j.Error, j.ClaimToken })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            throw new InvalidOperationException($"Job {id} ({type}) was deleted before it finished, so nothing will answer it.");
        }

        if (row.ProcessedAt is not null)
        {
            return (true, row.Result);
        }

        // An awaited job is tried once: an error on a row nobody holds is how it ended. While it is claimed, an error
        // left by an earlier attempt (a dashboard retry) is not the answer yet.
        if (row is { Error: { } error, ClaimToken: null })
        {
            throw new JobFailedException(error) { JobType = type };
        }

        return (false, null);
    }
}
