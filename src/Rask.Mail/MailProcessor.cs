using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.Mail;

/// <summary>
/// Polls the <see cref="QueuedMail"/> table on a schedule and delivers each due message through the registered
/// <see cref="IMailSender"/>. At-least-once: a message is sent at least once and, on failure, is retried with
/// exponential backoff up to <see cref="MailOptions.MaxAttempts"/> (after which it is left as a dead letter).
/// Also purges sent messages past <see cref="MailOptions.RetentionPeriod"/>. A failing send — or a transient
/// database error — never crashes the app. Run <b>one processor per app</b> (SQLite is single-writer).
/// </summary>
/// <typeparam name="TContext">The application's <see cref="DbContext"/> that owns the mail table.</typeparam>
public sealed class MailProcessor<TContext>(
    IDbContextFactory<TContext> contextFactory,
    IServiceScopeFactory scopeFactory,
    MailOptions options,
    TimeProvider timeProvider,
    MailMetrics metrics,
    ILogger<MailProcessor<TContext>> logger) : BackgroundService
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
            logger.LogError(ex, "Mail processing cycle failed; retrying on the next poll.");
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var batch = await db.Set<QueuedMail>()
            .Where(m => m.ProcessedAt == null && m.Attempts < options.MaxAttempts && m.RunAt <= now)
            .OrderBy(m => m.RunAt)
            .ThenBy(m => m.Id)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in batch)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var startedAt = timeProvider.GetTimestamp();
            try
            {
                var outgoing = MailSerializer.ToOutgoing(message);
                // A fresh scope per message resolves the sender at its own lifetime, so a custom scoped
                // IMailSender isn't captured by this singleton processor.
                await using var scope = scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<IMailSender>();
                await sender.SendAsync(outgoing, cancellationToken).ConfigureAwait(false);
                message.ProcessedAt = timeProvider.GetUtcNow().UtcDateTime;
                message.Error = null;
                metrics.Sent(timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break; // shutting down — leave this and the rest for the next run
            }
#pragma warning disable CA1031 // A failing send must not stop the drain or crash the app — record + retry with backoff.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Fail(message, ex.Message);
                metrics.Failed();
                if (message.Attempts >= options.MaxAttempts)
                {
                    metrics.DeadLettered();
                }

                logger.LogError(ex, "Email {Id} failed to send (attempt {Attempts}).", message.Id, message.Attempts);
            }

            // Persist THIS message's outcome before moving on (with None so a delivered message is still marked
            // during shutdown). Saving per message bounds an at-least-once re-send to the single message whose
            // save failed — never the whole batch of already-sent ones.
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    // Record a failed attempt and push the next send out by the exponential backoff.
    private void Fail(QueuedMail message, string error)
    {
        message.Attempts++;
        message.Error = error;
        message.RunAt = timeProvider.GetUtcNow().UtcDateTime + options.RetryDelay(message.Attempts);
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
        await db.Set<QueuedMail>()
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // Only pays for the counts while something is collecting the gauges — see MailMetrics' remarks.
    private async Task SampleQueueDepthAsync(CancellationToken cancellationToken)
    {
        if (!metrics.WantsQueueDepth)
        {
            return;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var pending = await db.Set<QueuedMail>()
            .CountAsync(m => m.ProcessedAt == null && m.Attempts < options.MaxAttempts, cancellationToken)
            .ConfigureAwait(false);
        var deadLetters = await db.Set<QueuedMail>()
            .CountAsync(m => m.ProcessedAt == null && m.Attempts >= options.MaxAttempts, cancellationToken)
            .ConfigureAwait(false);

        metrics.ObserveQueueDepth(pending, deadLetters);
    }
}
