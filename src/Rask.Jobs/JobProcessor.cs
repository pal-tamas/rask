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

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
        var changed = false;

        foreach (var definition in options.Recurring)
        {
            var state = await db.Set<RecurringJobState>()
                .FirstOrDefaultAsync(s => s.Name == definition.Name, cancellationToken)
                .ConfigureAwait(false);

            if (state is null)
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
            state.LastEnqueuedAt = now;
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

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var stale = await db.Set<Job>()
            .Where(j => j.ProcessedAt != null && j.ProcessedAt < cutoff)
            .OrderBy(j => j.Id)
            .Take(1000)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (stale.Count == 0)
        {
            return;
        }

        db.Set<Job>().RemoveRange(stale);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
