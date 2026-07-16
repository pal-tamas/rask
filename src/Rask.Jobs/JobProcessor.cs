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
    ILogger<JobProcessor<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);
    private DateTime _lastPurge;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.PollInterval);
        try
        {
            do
            {
                await EnqueueDueRecurringAsync(stoppingToken).ConfigureAwait(false);
                await DrainAsync(stoppingToken).ConfigureAwait(false);
                await PurgeAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var batch = await db.Set<Job>()
            .Where(j => j.ProcessedAt == null && j.Attempts < options.MaxAttempts && j.RunAt <= now)
            .OrderBy(j => j.RunAt)
            .ThenBy(j => j.Id)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (batch.Count == 0)
        {
            return;
        }

        foreach (var job in batch)
        {
            var command = JobSerializerRegistry.Deserialize(job.Type, job.Payload);
            if (command is null)
            {
                Fail(job, $"No registered job type '{job.Type}'.");
                logger.LogError("Job {Id} has an unregistered type '{Type}'.", job.Id, job.Type);
                continue;
            }

            try
            {
                // A fresh scope per job isolates scoped handler dependencies (e.g. a DbContext) between jobs.
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
                await dispatcher.DispatchAsync(command, cancellationToken).ConfigureAwait(false);
                job.ProcessedAt = timeProvider.GetUtcNow().UtcDateTime;
                job.Error = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break; // shutting down — leave the rest for the next run
            }
#pragma warning disable CA1031 // A failing job must not stop the drain or crash the app — record + retry with backoff.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Fail(job, ex.Message);
                logger.LogError(ex, "Job {Id} failed (attempt {Attempts}).", job.Id, job.Attempts);
            }
        }

        // Persist with None: jobs in this batch already ran (their side effects happened), so their
        // ProcessedAt must be written even when the host is stopping — otherwise they'd re-run on restart.
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    // Record a failed attempt and push the next run out by the exponential backoff.
    private void Fail(Job job, string error)
    {
        job.Attempts++;
        job.Error = error;
        job.RunAt = timeProvider.GetUtcNow().UtcDateTime + options.RetryDelay(job.Attempts);
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
        // removing only one page per run.
        while (!cancellationToken.IsCancellationRequested)
        {
            var stale = await db.Set<Job>()
                .Where(j => j.ProcessedAt != null && j.ProcessedAt < cutoff)
                .OrderBy(j => j.Id)
                .Take(page)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (stale.Count == 0)
            {
                break;
            }

            db.Set<Job>().RemoveRange(stale);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (stale.Count < page)
            {
                break;
            }
        }
    }
}
