using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Cqrs;

namespace Rask.Outbox;

/// <summary>
/// Polls the <see cref="OutboxMessage"/> table on a schedule and publishes each unprocessed message through
/// <c>Rask.Cqrs</c>' <see cref="IDispatcher"/>, marking it processed (or recording the failure + attempt
/// count). At-least-once: a message publishes at least once and is retried up to
/// <see cref="OutboxOptions.MaxAttempts"/>. A publish failure never crashes the app.
/// </summary>
/// <typeparam name="TContext">The application's <see cref="DbContext"/> that owns the outbox table.</typeparam>
public sealed class OutboxProcessor<TContext>(
    IDbContextFactory<TContext> contextFactory,
    IServiceScopeFactory scopeFactory,
    OutboxOptions options,
    TimeProvider timeProvider,
    OutboxMetrics metrics,
    ILogger<OutboxProcessor<TContext>> logger) : BackgroundService
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
            logger.LogError(ex, "Outbox processing cycle failed; retrying on the next poll.");
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var batch = await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null && m.Attempts < options.MaxAttempts)
            .OrderBy(m => m.Id)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (batch.Count == 0)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        foreach (var message in batch)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var notification = OutboxSerializerRegistry.Deserialize(message.Type, message.Payload);
            if (notification is null)
            {
                message.Attempts++;
                message.Error = $"No registered outbox event type '{message.Type}'.";

                // An unregistered type is a failure like any other, and counts toward the dead letter it
                // will become — a renamed event that nobody re-registered is the most ordinary way a
                // production outbox starts abandoning work, so it must not be invisible to metrics.
                metrics.Failed(message.Type);
                if (message.Attempts >= options.MaxAttempts)
                {
                    metrics.DeadLettered(message.Type);
                }

                logger.LogError("Outbox message {Id} has an unregistered type '{Type}'.", message.Id, message.Type);
            }
            else
            {
                var startedAt = timeProvider.GetTimestamp();
                try
                {
                    await dispatcher.PublishAsync(notification, cancellationToken).ConfigureAwait(false);
                    message.ProcessedAt = timeProvider.GetUtcNow().UtcDateTime;
                    message.Error = null;
                    metrics.Processed(message.Type, timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break; // shutting down — leave this and the rest for the next run
                }
#pragma warning disable CA1031 // A failing handler must not stop the drain or crash the app — record + retry.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    message.Attempts++;
                    message.Error = ex.Message;
                    metrics.Failed(message.Type);
                    if (message.Attempts >= options.MaxAttempts)
                    {
                        metrics.DeadLettered(message.Type);
                    }

                    logger.LogError(ex, "Outbox message {Id} failed to publish (attempt {Attempts}).", message.Id, message.Attempts);
                }
            }

            // Persist THIS message's outcome before moving on (with None so an event that already published is
            // still marked during shutdown). Saving per message bounds an at-least-once re-publish to the single
            // message whose save failed, never the whole batch: one row deleted or edited underneath the drain
            // would otherwise abort the entire SaveChanges and re-publish every event already delivered from it.
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    // Retention. Published messages are history; a busy app writes them faster than anyone reads them, and
    // without this the table is the only pillar's table that grows without bound for the life of the app.
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

        // Paged, and deleted by id rather than by predicate. Two reasons, both about the first run on an
        // app that has been writing events for months with no retention at all:
        //   * one unbounded DELETE would hold SQLite's single write lock for the length of the whole sweep,
        //     stalling every request behind it. A page at a time keeps each lock short.
        //   * the delete is a set-based ExecuteDelete over ids, not tracked entities, so a row vanishing
        //     underneath the sweep can't raise a concurrency exception the way a RemoveRange would.
        // Looping until drained (rather than one page per hour) is what lets retention actually catch up.
        while (!cancellationToken.IsCancellationRequested)
        {
            var stale = await db.Set<OutboxMessage>()
                .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
                .OrderBy(m => m.Id)
                .Take(page)
                .Select(m => m.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (stale.Count == 0)
            {
                break;
            }

            await db.Set<OutboxMessage>()
                .Where(m => stale.Contains(m.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (stale.Count < page)
            {
                break;
            }
        }
    }

    // Only pays for the counts while something is collecting the gauges — see OutboxMetrics' remarks.
    private async Task SampleQueueDepthAsync(CancellationToken cancellationToken)
    {
        if (!metrics.WantsQueueDepth)
        {
            return;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var pending = await db.Set<OutboxMessage>()
            .CountAsync(m => m.ProcessedAt == null && m.Attempts < options.MaxAttempts, cancellationToken)
            .ConfigureAwait(false);
        var deadLetters = await db.Set<OutboxMessage>()
            .CountAsync(m => m.ProcessedAt == null && m.Attempts >= options.MaxAttempts, cancellationToken)
            .ConfigureAwait(false);

        metrics.ObserveQueueDepth(pending, deadLetters);
    }
}
