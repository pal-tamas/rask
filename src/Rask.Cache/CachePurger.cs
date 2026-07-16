using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.Cache;

/// <summary>
/// Sweeps expired <see cref="CacheEntry"/> rows out of the table on a schedule (<see cref="CacheOptions.PurgeInterval"/>).
/// Reads renew and lazily evict entries, so this sweep is a bulk backstop for entries that are simply never read
/// again. A transient database error never crashes the app. Run <b>one purger per app</b> (SQLite is single-writer).
/// </summary>
/// <typeparam name="TContext">The application's <see cref="DbContext"/> that owns the cache table.</typeparam>
public sealed class CachePurger<TContext>(
    IDbContextFactory<TContext> contextFactory,
    CacheOptions options,
    TimeProvider timeProvider,
    ILogger<CachePurger<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.PurgeInterval);
        try
        {
            do
            {
                await PurgeAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await db.Set<CacheEntry>()
                .Where(e => e.ExpiresAt <= now)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // shutting down — let ExecuteAsync end
        }
#pragma warning disable CA1031 // A transient DB error (e.g. SQLITE_BUSY) must not fault the service and stop the host — retry next sweep.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Cache purge sweep failed; retrying on the next interval.");
        }
    }
}
