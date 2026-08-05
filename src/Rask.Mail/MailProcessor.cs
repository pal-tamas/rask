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

    /// <summary>Identifies this instance in logs — not persisted; the per-batch token is what rows carry.</summary>
    private readonly Guid _instanceId = Guid.NewGuid();

    /// <summary>The token on the batch currently in flight, so shutdown can hand it back.</summary>
    private Guid _lease;

    /// <summary>
    /// Atomically takes a batch of due emails for this instance, so a second processor cannot send them
    /// too. See <c>JobProcessor.ClaimAsync</c> for why the compare-and-swap is portable and why the
    /// predicate tests lease expiry rather than <c>ClaimToken == null</c>.
    /// </summary>
    internal async Task<List<QueuedMail>> ClaimAsync(TContext db, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var ids = await db.Set<QueuedMail>()
            .Where(m => m.ProcessedAt == null
                && m.Attempts < options.MaxAttempts
                && m.RunAt <= now
                && (m.ClaimedUntil == null || m.ClaimedUntil <= now))
            .OrderBy(m => m.RunAt)
            .ThenBy(m => m.Id)
            .Take(options.BatchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (ids.Count == 0)
        {
            return [];
        }

        ids.Sort();

        var token = Guid.NewGuid();
        var until = now + options.LeaseDuration;

        // Attempts is incremented HERE, not on failure — a send that takes the process down never reaches
        // the failure path, and counting only failures would retry it forever.
        var claimed = await db.Set<QueuedMail>()
            .Where(m => ids.Contains(m.Id)
                && m.ProcessedAt == null
                && (m.ClaimedUntil == null || m.ClaimedUntil <= now))
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.ClaimToken, token)
                    .SetProperty(m => m.ClaimedUntil, until)
                    .SetProperty(m => m.Attempts, m => m.Attempts + 1),
                cancellationToken)
            .ConfigureAwait(false);

        if (claimed == 0)
        {
            return [];
        }

        _lease = token;

        return await db.Set<QueuedMail>()
            .Where(m => ids.Contains(m.Id) && m.ClaimToken == token)
            .OrderBy(m => m.RunAt)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>Hands back whatever this instance still holds — see <c>JobProcessor.StopAsync</c>.</remarks>
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
            await db.Set<QueuedMail>()
                .Where(m => m.ClaimToken == _lease && m.ProcessedAt == null)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(m => m.ClaimToken, (Guid?)null)
                        .SetProperty(m => m.ClaimedUntil, (DateTime?)null),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Shutdown must not throw: the lease expires on its own, so this is an optimisation.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Could not release mail leases on shutdown; they expire in {Lease}.", options.LeaseDuration);
        }
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
            if (IsMissingLeaseColumn(ex))
            {
                // The generic message would send someone reading a stack trace instead of running two
                // commands. This failure is also invisible without it: the exception is swallowed here,
                // so the app looks healthy while logging the same error every poll, forever.
                logger.LogError(
                    ex,
                    "Rask.Mail added lease columns (ClaimToken, ClaimedUntil) that this database does not have. "
                    + "Run: rask db add AddMailLeases && rask db update. See docs/{Doc}.",
                    "databases.md#running-more-than-one-instance");
                return;
            }

            logger.LogError(ex, "Mail processing cycle failed; retrying on the next poll.");
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var batch = await ClaimAsync(db, now, cancellationToken).ConfigureAwait(false);

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
                Release(message);
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
            try
            {
                await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                // ClaimToken is the concurrency token, so this means our lease expired mid-send and another
                // instance owns the row now. Its outcome wins; discard ours. The recipient did get the email
                // twice — at-least-once was always the contract, and the fix for *this* cause is a longer
                // LeaseDuration. This warning is how you find out you need one.
                logger.LogWarning(
                    "Email {Id} lost its lease mid-send on instance {Instance}; another instance owns it now. "
                    + "Increase MailOptions.LeaseDuration past the time a send takes.",
                    message.Id,
                    _instanceId);

                // The context is shared across the batch: a failed entry left attached is retried by the
                // next message's SaveChanges and fails that one too.
                db.Entry(message).State = EntityState.Detached;
            }
        }
    }

    // Record a failed attempt and push the next send out by the exponential backoff. Attempts is NOT
    // incremented here — the claim already counted this attempt, so a send that kills the process still
    // counts toward MaxAttempts instead of being retried forever.
    private void Fail(QueuedMail message, string error)
    {
        message.Error = error;
        message.RunAt = timeProvider.GetUtcNow().UtcDateTime + options.RetryDelay(message.Attempts);
        Release(message);
    }

    // Hand the row back so the next poll can see it without waiting for the lease to expire.
    private static void Release(QueuedMail message)
    {
        message.ClaimToken = null;
        message.ClaimedUntil = null;
    }

    /// <summary>
    /// True when the failure is "the lease columns aren't in the database" — i.e. the package was upgraded
    /// but the migration was never applied.
    /// </summary>
    /// <remarks>
    /// Matched on the message because every provider words it differently and none of them has a shared
    /// error code for "no such column". A false positive costs a wrong-but-adjacent log line; a false
    /// negative is just the generic message, so erring toward matching is safe here.
    /// </remarks>
    private static bool IsMissingLeaseColumn(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is System.Data.Common.DbException
                && (e.Message.Contains(nameof(QueuedMail.ClaimToken), StringComparison.OrdinalIgnoreCase)
                    || e.Message.Contains(nameof(QueuedMail.ClaimedUntil), StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
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

        // Paged rather than one unbounded DELETE. The single statement was correct — a set-based delete is
        // idempotent, so concurrent sweeps can't corrupt each other — but on the first run of an app that
        // has been sending for months it holds SQLite's one write lock for the whole sweep, stalling every
        // request behind it. A page at a time keeps each lock short; looping until drained keeps retention
        // able to catch up. Same shape as the jobs and outbox sweeps.
        while (!cancellationToken.IsCancellationRequested)
        {
            var stale = await db.Set<QueuedMail>()
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

            await db.Set<QueuedMail>()
                .Where(m => stale.Contains(m.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (stale.Count < page)
            {
                break;
            }
        }
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
