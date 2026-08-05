using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.SQLite.Litestream;

/// <summary>
/// Hosted service that runs <c>litestream replicate</c> for the lifetime of the application, streaming
/// the WAL to the configured replica. It is deliberately resilient: if the backup process exits or
/// crashes it is logged at <see cref="LogLevel.Critical"/> and <b>restarted</b> after a backing-off delay
/// (so a transient failure doesn't stop backups for good), and a failure is never propagated, so a backup
/// problem cannot take the web app down with it.
/// </summary>
internal sealed class LitestreamReplicationService : BackgroundService
{
    private static readonly TimeSpan MaxRestartDelay = TimeSpan.FromMinutes(1);

    private readonly LitestreamOptions _options;
    private readonly ILitestreamExecutor _executor;
    private readonly LitestreamStatus _status;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LitestreamReplicationService> _logger;

    public LitestreamReplicationService(
        LitestreamOptions options,
        ILitestreamExecutor executor,
        LitestreamStatus status,
        TimeProvider timeProvider,
        ILogger<LitestreamReplicationService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _executor = executor;
        _status = status;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var restartDelay = _options.RestartDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Starting Litestream replication.");
            _status.MarkStarted(_timeProvider.GetUtcNow());

            try
            {
                var exitCode = await _executor.RunAsync(LitestreamCommand.Replicate(_options), stoppingToken)
                    .ConfigureAwait(false);

                if (stoppingToken.IsCancellationRequested)
                {
                    _status.MarkStopped(_timeProvider.GetUtcNow());
                    break;
                }

                // `replicate` runs until cancelled; returning on its own means the backup stream stopped.
                _status.MarkExited(_timeProvider.GetUtcNow(), exitCode);
                _logger.LogCritical(
                    "Litestream replication exited unexpectedly with code {ExitCode}; restarting in {Delay}.",
                    exitCode, restartDelay);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown — the host is stopping and we cancelled the process.
                _status.MarkStopped(_timeProvider.GetUtcNow());
                break;
            }
#pragma warning disable CA1031 // A backup sidecar failing must never crash the app it protects — log and retry.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _status.MarkFailed(_timeProvider.GetUtcNow(), ex.Message);
                _logger.LogCritical(ex, "Litestream replication could not run; restarting in {Delay}.", restartDelay);
            }

            try
            {
                await Task.Delay(restartDelay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Exponential backoff, capped, so a persistently failing replica (bad URL/credentials) doesn't
            // spin hot or flood the logs.
            restartDelay = restartDelay <= TimeSpan.Zero
                ? _options.RestartDelay
                : TimeSpan.FromTicks(Math.Min(restartDelay.Ticks * 2, MaxRestartDelay.Ticks));
        }
    }
}
