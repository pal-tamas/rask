using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Cqrs;

namespace Rask.Jobs;

/// <summary>
/// Polls the <see cref="Job"/> table on a schedule and runs each due job by dispatching it through
/// <c>Rask.Cqrs</c>' <see cref="IDispatcher"/> to its <see cref="ICommandHandler{TCommand}"/>. At-least-once:
/// a job runs at least once and, on failure, is retried with exponential backoff up to
/// <see cref="JobOptions.MaxAttempts"/> (after which it is left as a dead letter). Also enqueues due
/// interval-recurring jobs and purges completed jobs past <see cref="JobOptions.RetentionPeriod"/>. A failing
/// job never crashes the app. Run <b>one processor per app</b> (SQLite is single-writer).
/// </summary>
/// <typeparam name="TContext">The application's <see cref="DbContext"/> that owns the jobs tables.</typeparam>
public sealed class JobProcessor<TContext>(
    IDbContextFactory<TContext> contextFactory,
    IServiceScopeFactory scopeFactory,
    JobOptions options,
    TimeProvider timeProvider,
    JobMetrics metrics,
    ILogger<JobProcessor<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);
    private DateTime _lastPurge;

    /// <summary>Identifies this instance in logs — not persisted; the per-batch token is what rows carry.</summary>
    private readonly Guid _instanceId = Guid.NewGuid();

    /// <summary>The token on the batch currently in flight, so shutdown can hand it back.</summary>
    private Guid _lease;

    /// <summary>
    /// Atomically takes a batch of due jobs for this instance, so a second processor cannot run them too.
    /// </summary>
    /// <remarks>
    /// Three steps, and the middle one is the whole design: a single <c>UPDATE … WHERE</c> whose predicate
    /// re-tests claimability. Every supported provider re-evaluates that predicate against the row version
    /// the winner committed — PostgreSQL through EvaluatePlanQual, SQL Server under its update locks,
    /// SQLite by having one writer — so a row can be claimed by exactly one instance, with no
    /// <c>SKIP LOCKED</c> and no provider-specific SQL.
    ///
    /// <para>The predicate tests <em>lease expiry</em>, not <c>ClaimToken == null</c>. That is what makes
    /// crash recovery free: an instance that dies holding rows never clears its token, so a null test would
    /// hide that work forever and force a second sweeper to exist. Expiry brings it back on its own.</para>
    /// </remarks>
    internal async Task<List<Job>> ClaimAsync(TContext db, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var ids = await db.Set<Job>()
            .Where(j => j.ProcessedAt == null
                && j.Attempts < options.MaxAttempts
                && j.RunAt <= now
                && (j.ClaimedUntil == null || j.ClaimedUntil <= now))
            .OrderBy(j => j.RunAt)
            .ThenBy(j => j.Id)
            .Take(options.BatchSize)
            .Select(j => j.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (ids.Count == 0)
        {
            return [];
        }

        // Ascending, so two instances updating overlapping id sets lock rows in the same order. A deadlock
        // is still possible on a client-server database; the cycle's catch turns it into "retry next poll".
        ids.Sort();

        var token = Guid.NewGuid();
        var until = now + options.LeaseDuration;

        // Attempts is incremented HERE, not on failure. A job that takes the process down with it (OOM, an
        // eviction, a FailFast) never reaches the failure path, so counting only failures would leave it
        // retried by every instance forever — MaxAttempts would never dead-letter it. Claim-time counting
        // makes "attempts started" the meaning, which is the one that bounds a crash loop.
        var claimed = await db.Set<Job>()
            .Where(j => ids.Contains(j.Id)
                && j.ProcessedAt == null
                && (j.ClaimedUntil == null || j.ClaimedUntil <= now))
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(j => j.ClaimToken, token)
                    .SetProperty(j => j.ClaimedUntil, until)
                    .SetProperty(j => j.Attempts, j => j.Attempts + 1),
                cancellationToken)
            .ConfigureAwait(false);

        if (claimed == 0)
        {
            return [];
        }

        _lease = token;

        // Filtered on the id list as well as the token, so this rides the primary key — which is why no
        // index on ClaimToken is needed.
        return await db.Set<Job>()
            .Where(j => ids.Contains(j.Id) && j.ClaimToken == token)
            .OrderBy(j => j.RunAt)
            .ThenBy(j => j.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.PollInterval);
        try
        {
            do
            {
                await RunCycleAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await EnqueueDueRecurringAsync(cancellationToken).ConfigureAwait(false);
            await DrainAsync(cancellationToken).ConfigureAwait(false);
            await PurgeAsync(cancellationToken).ConfigureAwait(false);
            await SampleQueueDepthAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // shutting down — let ExecuteAsync end
        }
#pragma warning disable CA1031 // A transient DB error (e.g. SQLITE_BUSY) must not fault the service and stop the host — retry next poll.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Job processing cycle failed; retrying on the next poll.");
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var batch = await ClaimAsync(db, now, cancellationToken).ConfigureAwait(false);

        if (batch.Count == 0)
        {
            return;
        }

        // The in-flight job gets a bounded grace after SIGTERM instead of being cancelled mid-call. The
        // host token ARMS a deadline on this source rather than cancelling it, so user code keeps a live
        // token for ShutdownGracePeriod past the stop signal. (Same shape as the Litestream executor's
        // graceful stop.) One CTS per batch, not per job: the loop's pre-check below refuses to START
        // anything new the moment the host token fires, so at most ONE job is ever behind this deadline —
        // the extra shutdown time is bounded by a single grace period, not one per remaining job.
        //
        // Declaration order is load-bearing: `graceCts` must be declared before `registration` so
        // disposal runs registration-first. The reverse order lets the callback call CancelAfter on an
        // already-disposed source and throw during shutdown.
        using var graceCts = new CancellationTokenSource();
        using var registration = cancellationToken.Register(() => graceCts.CancelAfter(options.ShutdownGracePeriod));
        var graceToken = graceCts.Token;

        foreach (var job in batch)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var command = JobSerializerRegistry.Deserialize(job.Type, job.Payload);
            if (command is null)
            {
                Fail(job, $"No registered job type '{job.Type}'.");

                // An unregistered type is a failure like any other, and counts toward the dead letter it
                // will become — a renamed job that nobody re-registered is the most ordinary way a
                // production queue starts abandoning work, so it must not be invisible to metrics.
                metrics.Failed(job.Type);
                if (job.Attempts >= options.MaxAttempts)
                {
                    metrics.DeadLettered(job.Type);
                }

                logger.LogError("Job {Id} has an unregistered type '{Type}'.", job.Id, job.Type);
            }
            else
            {
                var startedAt = timeProvider.GetTimestamp();
                try
                {
                    // A fresh scope per job isolates scoped handler dependencies (e.g. a DbContext) between jobs.
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
                    await dispatcher.DispatchAsync(command, graceToken).ConfigureAwait(false);
                    job.ProcessedAt = timeProvider.GetUtcNow().UtcDateTime;
                    job.Error = null;
                    Release(job);
                    metrics.Processed(job.Type, timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Shutdown outlived the grace. Attempts and RunAt are deliberately untouched: a
                    // redeploy is not a failed attempt, and counting it would march never-failing work
                    // toward its dead letter at the cadence you deploy. The row stays immediately eligible
                    // and re-runs whole on restart — metered and logged so a grace period that is too
                    // short for the work is visible rather than silent.
                    //
                    // The filter stays on the HOST token, not the grace token: the grace deadline is only
                    // ever armed by the host token firing, so any grace expiry necessarily satisfies it,
                    // while a handler's own OperationCanceledException still falls through to the generic
                    // catch below and counts as the real failure it is.
                    metrics.Interrupted(job.Type);
                    logger.LogWarning(
                        "Job {Id} ({Type}) was interrupted by shutdown after its {Grace} grace period; it will run "
                        + "again on restart, so its handler must be idempotent.",
                        job.Id, job.Type, options.ShutdownGracePeriod);
                    break; // leave this and the rest for the next run
                }
#pragma warning disable CA1031 // A failing job must not stop the drain or crash the app — record + retry with backoff.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    Fail(job, ex.Message);
                    metrics.Failed(job.Type);
                    if (job.Attempts >= options.MaxAttempts)
                    {
                        metrics.DeadLettered(job.Type);
                    }

                    logger.LogError(ex, "Job {Id} failed (attempt {Attempts}).", job.Id, job.Attempts);
                }
            }

            // Persist THIS job's outcome before moving on (with None so a job that already ran is still
            // marked during shutdown — otherwise it re-runs on restart). Saving per job bounds an
            // at-least-once re-run to the single job whose save failed, never the whole batch: one row
            // deleted or edited underneath the drain would otherwise abort the entire SaveChanges and
            // strip ProcessedAt from every already-executed job in it.
            try
            {
                await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                // ClaimToken is the concurrency token, so this means our lease expired mid-job and another
                // instance has taken the row. Its outcome wins; discard ours rather than stamping over it.
                // The job did run twice — at-least-once was always the contract, and the fix for *this*
                // cause is a longer LeaseDuration. This warning is how you find out you need one.
                logger.LogWarning(
                    "Job {Id} lost its lease mid-run on instance {Instance}; another instance owns it now. "
                    + "Increase JobOptions.LeaseDuration past the time this job takes.",
                    job.Id,
                    _instanceId);

                // Not optional: the context is shared across the batch, so a failed entry left attached is
                // retried by the *next* job's SaveChanges and fails that one too.
                db.Entry(job).State = EntityState.Detached;
            }
        }
    }

    // Record a failed attempt and push the next run out by the exponential backoff. Attempts is NOT
    // incremented here — the claim already counted this attempt, so that a job which kills the process
    // still counts toward MaxAttempts instead of being retried forever.
    private void Fail(Job job, string error)
    {
        job.Error = error;
        job.RunAt = timeProvider.GetUtcNow().UtcDateTime + options.RetryDelay(job.Attempts);
        Release(job);
    }

    // Hand the row back so the next poll can see it without waiting for the lease to expire.
    private static void Release(Job job)
    {
        job.ClaimToken = null;
        job.ClaimedUntil = null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Hands back whatever this instance still holds. Without it a rolling deploy parks up to
    /// <see cref="JobOptions.BatchSize"/> jobs for a whole <see cref="JobOptions.LeaseDuration"/> — they are
    /// not lost, but "deployed and the queue went quiet for five minutes" is not what anyone expects.
    /// Best-effort by design: the lease expiring is the backstop, so a failure here costs latency, not work.
    /// </remarks>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        if (_lease == Guid.Empty)
        {
            return;
        }

        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
            await db.Set<Job>()
                .Where(j => j.ClaimToken == _lease && j.ProcessedAt == null)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(j => j.ClaimToken, (Guid?)null)
                        .SetProperty(j => j.ClaimedUntil, (DateTime?)null),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Shutdown must not throw: the lease expires on its own, so this is an optimisation.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Could not release job leases on shutdown; they expire in {Lease}.", options.LeaseDuration);
        }
    }

    private async Task EnqueueDueRecurringAsync(CancellationToken cancellationToken)
    {
        if (options.Recurring.Count == 0)
        {
            return;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Load every recurring job's state in one query rather than one round-trip per definition.
        var names = options.Recurring.Select(r => r.Name).ToList();
        var states = await db.Set<RecurringJobState>()
            .Where(s => names.Contains(s.Name))
            .ToDictionaryAsync(s => s.Name, cancellationToken)
            .ConfigureAwait(false);

        var changed = false;
        foreach (var definition in options.Recurring)
        {
            if (!states.TryGetValue(definition.Name, out var state))
            {
                state = new RecurringJobState { Name = definition.Name };
                db.Set<RecurringJobState>().Add(state);
            }
            else if (state.LastEnqueuedAt is { } last && now - last < definition.Interval)
            {
                continue; // not due yet
            }

            var (type, payload) = JobSerializerRegistry.Serialize(definition.Factory());
            db.Set<Job>().Add(new Job { Type = type, Payload = payload, RunAt = now, CreatedAt = now });

            // Anchor the next run to the schedule (last + interval), not the (late) poll time, so cadence
            // doesn't drift by a poll interval each cycle. If we fell more than one interval behind (e.g. the
            // app was down), reset to now so we don't burst a run of catch-up jobs.
            state.LastEnqueuedAt = state.LastEnqueuedAt is { } prev && now - prev < definition.Interval * 2
                ? prev + definition.Interval
                : now;
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        if (options.RetentionPeriod <= TimeSpan.Zero)
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (now - _lastPurge < PurgeInterval)
        {
            return;
        }

        _lastPurge = now;
        var cutoff = now - options.RetentionPeriod;
        const int page = 1000;

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Delete in pages until drained, so retention keeps up even with a high completion rate rather than
        // removing only one page per run. Deleted by id through ExecuteDelete rather than by loading and
        // RemoveRange-ing the entities: a tracked delete of a row that vanished underneath the sweep raises
        // DbUpdateConcurrencyException and fails the whole cycle, which is exactly what two processors
        // purging concurrently would do to each other. The outbox's sweep already worked this way.
        while (!cancellationToken.IsCancellationRequested)
        {
            var stale = await db.Set<Job>()
                .Where(j => j.ProcessedAt != null && j.ProcessedAt < cutoff)
                .OrderBy(j => j.Id)
                .Take(page)
                .Select(j => j.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (stale.Count == 0)
            {
                break;
            }

            await db.Set<Job>()
                .Where(j => stale.Contains(j.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (stale.Count < page)
            {
                break;
            }
        }
    }

    // Only pays for the counts while something is collecting the gauges — see JobMetrics' remarks. Two
    // COUNT(*)s on every 5s poll forever, for an app that exports no metrics, would be a tax on the write
    // lock that nobody asked for.
    private async Task SampleQueueDepthAsync(CancellationToken cancellationToken)
    {
        if (!metrics.WantsQueueDepth)
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var pending = await db.Set<Job>()
            .CountAsync(j => j.ProcessedAt == null && j.Attempts < options.MaxAttempts, cancellationToken)
            .ConfigureAwait(false);
        var deadLetters = await db.Set<Job>()
            .CountAsync(j => j.ProcessedAt == null && j.Attempts >= options.MaxAttempts, cancellationToken)
            .ConfigureAwait(false);

        metrics.ObserveQueueDepth(pending, deadLetters);
    }
}
