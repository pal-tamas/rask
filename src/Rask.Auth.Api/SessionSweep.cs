using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.Auth;

/// <summary>Removes the rows of sessions that ended or expired more than a day ago, once an hour.</summary>
internal sealed partial class SessionSweep<TContext>(
    IDbContextFactory<TContext> contexts,
    TimeProvider clock,
    ILogger<SessionSweep<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    // A day's grace after a session ends or expires, so a device list can still say "signed out yesterday".
    private static TimeSpan Grace => TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);

        do
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A database that is not migrated yet, or briefly unreachable: try again next hour.
                SweepFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>One pass. The grace holds for an ended session too: its row is soft-deleted, not gone.</summary>
    internal async Task SweepAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - Grace;

        var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            await db.Set<Session>()
                .IgnoreQueryFilters()
                .Where(s => s.DeletedAt < cutoff || s.ExpiresAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The session sweep could not run this time.")]
    private static partial void SweepFailed(ILogger logger, Exception exception);
}
