using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.SQLite.Snapshots;

/// <summary>
/// Hosted service that takes a snapshot every <see cref="SqliteSnapshotOptions.Interval"/> (and once at
/// startup when <see cref="SqliteSnapshotOptions.SnapshotOnStartup"/> is set). A snapshot failure is
/// logged and the schedule continues — a backup problem never crashes the app.
/// </summary>
internal sealed class SqliteSnapshotService : BackgroundService
{
    private readonly SqliteSnapshotOptions _options;
    private readonly ISqliteSnapshotter _snapshotter;
    private readonly ILogger<SqliteSnapshotService> _logger;

    public SqliteSnapshotService(
        SqliteSnapshotOptions options,
        ISqliteSnapshotter snapshotter,
        ILogger<SqliteSnapshotService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(snapshotter);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _snapshotter = snapshotter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.SnapshotOnStartup)
        {
            await TrySnapshotAsync(stoppingToken).ConfigureAwait(false);
        }

        using var timer = new PeriodicTimer(_options.Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TrySnapshotAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task TrySnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _snapshotter.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down mid-snapshot — not an error.
        }
#pragma warning disable CA1031 // A failed backup must not stop the schedule or crash the app — log and continue.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "SQLite snapshot failed; will retry on the next interval.");
        }
    }
}
