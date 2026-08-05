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

        // The in-flight send gets a bounded grace after SIGTERM instead of being cancelled mid-call. The
        // host token ARMS a deadline on this source rather than cancelling it, so the SMTP conversation
        // keeps a live token for ShutdownGracePeriod past the stop signal. (Same shape as the Litestream
        // executor's graceful stop.) One CTS per batch, not per message: the loop's pre-check below
        // refuses to START anything new the moment the host token fires, so at most ONE send is ever
        // behind this deadline.
        //
        // This matters more here than in Jobs/Outbox. Delivery and the row update are not one
        // transaction, so a send cancelled during the SMTP DATA phase may already have been accepted by
        // the server while the row still reads unsent — and the next boot re-sends it. The grace period
        // cannot close that window (nothing local can), it makes it rare.
        //
        // Declaration order is load-bearing: `graceCts` must be declared before `registration` so
        // disposal runs registration-first. The reverse order lets the callback call CancelAfter on an
        // already-disposed source and throw during shutdown.
        using var graceCts = new CancellationTokenSource();
        using var registration = cancellationToken.Register(() => graceCts.CancelAfter(options.ShutdownGracePeriod));
        var graceToken = graceCts.Token;

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
                await sender.SendAsync(outgoing, graceToken).ConfigureAwait(false);
                message.ProcessedAt = timeProvider.GetUtcNow().UtcDateTime;
                message.Error = null;
                metrics.Sent(timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown outlived the grace. Attempts is deliberately untouched: a redeploy is not a
                // failed attempt, and counting it would march never-failing mail toward its dead letter at
                // the cadence you deploy. The row stays immediately eligible and is re-sent on restart —
                // and because delivery may already have happened, this counter is the direct answer to
                // "did that deploy duplicate any mail?".
                //
                // The filter stays on the HOST token, not the grace token: the grace deadline is only ever
                // armed by the host token firing, so any grace expiry necessarily satisfies it, while a
                // sender's own OperationCanceledException still falls through to the generic catch below
                // and counts as the real failure it is.
                metrics.Interrupted();
                logger.LogWarning(
                    "Email {Id} was interrupted by shutdown after its {Grace} grace period. It will be sent again on "
                    + "restart — and it may already have been delivered, since a cancelled SMTP conversation can be "
                    + "accepted by the server before the row is marked.",
                    message.Id, options.ShutdownGracePeriod);
                break; // leave this and the rest for the next run
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
