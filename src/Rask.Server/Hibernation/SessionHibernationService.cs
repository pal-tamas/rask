using Microsoft.Extensions.Hosting;
using Rask.Core.Diagnostics;

namespace Rask.Server.Hibernation;

/// <summary>Runs <see cref="SessionHibernation" />'s round every <see cref="SessionHibernationOptions.Every" />.</summary>
/// <remarks>Nothing at all on a host that seals no resume records: there is nothing a hidden tab could come back from.</remarks>
internal sealed class SessionHibernationService(SessionHibernation hibernation, TimeProvider? clock = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!hibernation.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(hibernation.Options.Every, clock ?? TimeProvider.System);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await hibernation.AskDueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One round that failed is not the last: the next tick asks again.
                RaskDiagnostics.Report(RaskLogLevel.Warning, "Rask.Live", "Rask Live: a round of hibernation failed", ex);
            }
        }
    }
}
