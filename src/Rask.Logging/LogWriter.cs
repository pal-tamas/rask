using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.Logging;

/// <summary>
/// Drains <see cref="LogChannel"/> into <see cref="ILogs"/> on a timer, and enforces retention.
/// <para>
/// The flush is on a timer rather than per entry because the interval <i>is</i> the coalescing window: a
/// chatty second becomes one transaction instead of hundreds. It is also the worst-case delay before a
/// logged line is queryable, which is why the default is short.
/// </para>
/// </summary>
internal sealed class LogWriter(
    LogChannel channel,
    ILogs store,
    RaskLoggingOptions options,
    LogMetrics metrics,
    TimeProvider timeProvider,
    ILogger<LogWriter> logger) : BackgroundService
{
    /// <summary>How often a store that keeps failing is reported again after its first failure.</summary>
    internal static readonly TimeSpan FailureReminderInterval = TimeSpan.FromMinutes(1);

    private DateTimeOffset _lastPurge;

    // Only the ExecuteAsync loop touches these, one cycle at a time.
    private int _failedCycles;
    private DateTimeOffset _lastFailureReport;

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // A final drain on its own budget rather than the host's token, which is already cancelled by the
        // time we get here. The lines written in the seconds before a shutdown are the ones most worth
        // keeping — but a store that cannot be reached must not stall a host that is trying to stop, so the
        // drain is bounded by ShutdownDrainTimeout rather than run to completion.
        if (options.ShutdownDrainTimeout > TimeSpan.Zero)
        {
            using var drain = new CancellationTokenSource(options.ShutdownDrainTimeout);
            try
            {
                await FlushAsync(drain.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning(
                    "The log store did not drain within {Timeout}; buffered entries were lost.",
                    options.ShutdownDrainTimeout);
            }
#pragma warning disable CA1031 // Teardown must not throw: a failing store cannot be allowed to fault shutdown.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "The log store failed its shutdown drain; buffered entries were lost.");
            }
        }

        // Only now: completing the channel first would drop every line the rest of the shutdown sequence
        // logs, which is precisely the part worth having.
        channel.Complete();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.FlushInterval);

        do
        {
            await RunCycleAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await FlushAsync(cancellationToken).ConfigureAwait(false) && _failedCycles > 0)
            {
                // Only a write proves the store works again. A retention sweep or a count can succeed against a
                // store that refuses inserts — a full disk still takes deletes — and taking one for a recovery
                // would announce it falsely and restart the run, so the next failure read as a first one.
                logger.LogInformation(
                    "The log store is writable again after {FailedFlushes} failed flushes.", _failedCycles);
                _failedCycles = 0;
            }

            await PurgeAsync(cancellationToken).ConfigureAwait(false);
            await SampleStoredAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down — StopAsync owns the final drain.
        }
#pragma warning disable CA1031 // A transient store failure must never fault the host; retry on the next tick.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ReportFailure(ex);
        }
    }

    /// <summary>
    /// Reports a failed cycle: the first of a run, then at most once per <see cref="FailureReminderInterval"/>.
    /// </summary>
    /// <remarks>
    /// A store that stays broken — a log table whose migration has not run yet, an unwritable disk — fails every
    /// flush, and an error with a stack trace every second buries the console it is meant to warn. The drops are
    /// still counted on every cycle; only the report is throttled. Safe to log at all because this category is
    /// always excluded from capture, so the failure reaches the other sinks without feeding the store that failed.
    /// </remarks>
    private void ReportFailure(Exception ex)
    {
        _failedCycles++;
        var now = timeProvider.GetUtcNow();

        if (_failedCycles == 1)
        {
            logger.LogError(
                ex,
                "A log store flush failed; retrying every {FlushInterval}, and reporting again at most once a minute while it keeps failing.",
                options.FlushInterval);
        }
        else if (now - _lastFailureReport >= FailureReminderInterval)
        {
            logger.LogError(
                ex,
                "The log store is still failing: {FailedFlushes} flushes in a row, their entries counted on rask.logs.dropped.",
                _failedCycles);
        }
        else
        {
            return;
        }

        _lastFailureReport = now;
    }

    /// <summary>Drains the buffer into the store. Returns whether anything was written.</summary>
    private async Task<bool> FlushAsync(CancellationToken cancellationToken)
    {
        var wrote = false;

        // One list, refilled: the flush runs every second for the life of the process, and a fresh
        // BatchSize-capacity list per cycle would be pure garbage.
        var batch = new List<LogRecord>(options.BatchSize);

        while (!cancellationToken.IsCancellationRequested)
        {
            batch.Clear();
            while (batch.Count < options.BatchSize && channel.TryRead(out var record))
            {
                batch.Add(record);
            }

            if (batch.Count == 0)
            {
                return wrote;
            }

            try
            {
                await store.AppendAsync(batch, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The batch is already out of the buffer, so a failed write loses it. Counting it as
                // dropped keeps rask.logs.dropped the single honest answer to "is the log I am reading
                // complete?" — re-queuing instead would reorder entries and, against a store that stays
                // broken, spin the same batch forever.
                metrics.Dropped(batch.Count);
                throw;
            }

            metrics.Written(batch.Count);
            wrote = true;
        }

        return wrote;
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        if (options.Retention <= TimeSpan.Zero && options.MaxRows <= 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (_lastPurge != default && now - _lastPurge < options.PurgeInterval)
        {
            return;
        }

        _lastPurge = now;
        var removed = await store.PurgeAsync(options.Retention, options.MaxRows, cancellationToken)
            .ConfigureAwait(false);
        metrics.Purged(removed);
    }

    private async Task SampleStoredAsync(CancellationToken cancellationToken)
    {
        if (!metrics.WantsStoredCount)
        {
            return;
        }

        metrics.ObserveStored(await store.CountAsync(cancellationToken).ConfigureAwait(false));
    }
}
