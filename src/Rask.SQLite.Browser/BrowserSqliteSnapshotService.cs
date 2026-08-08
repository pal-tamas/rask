using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.SQLite.Snapshots;

namespace Rask.SQLite.Browser;

/// <summary>
///     Writes the owning tab's database back to IndexedDB on
///     <see cref="BrowserSqliteOptions.SnapshotInterval" />.
/// </summary>
/// <remarks>
///     Separate from <see cref="BrowserSqliteHost" /> because the two want opposite things from the host
///     contract: the restore must block startup, and this must not. Registered after it, so the first tick
///     can never race the restore.
/// </remarks>
internal sealed class BrowserSqliteSnapshotService(
    BrowserSqliteOptions options,
    BrowserSqliteHost host,
    ISqliteSnapshotter snapshotter,
    ILogger<BrowserSqliteSnapshotService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A tab that does not own the database has nothing to persist, and snapshotting from it would be
        // exactly the overwrite the ownership lock exists to prevent.
        if (!host.IsOwner)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.SnapshotInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await snapshotter.SnapshotAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
#pragma warning disable CA1031 // A failed snapshot must not kill the loop — the next tick tries again.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    // Quota exhaustion is the expected cause and it is not transient, so this needs to be
                    // visible rather than swallowed: the app keeps working, but durability has stopped.
                    logger.LogError(ex, "Snapshot of browser SQLite database '{Name}' failed.", options.Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
