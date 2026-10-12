using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.Dashboard;

// Rask:Ops:AllowAnonymousAccess opens nothing outside Development: the console shows job payloads, stored
// mail and log lines, and is for whoever the RaskDashboard policy admits. A host that still asks is told
// once, as it starts, that the setting was not honoured — a silent no would read as a broken deploy.
internal sealed partial class AnonymousAccessNotice(
    OpsOptions options, IHostEnvironment environment, ILogger<AnonymousAccessNotice> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.AllowAnonymousAccess && !environment.IsDevelopment())
        {
            Ignored(log, environment.EnvironmentName);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Rask:Ops:AllowAnonymousAccess is set and is ignored in the {Environment} environment: anonymous "
                  + "access to the dashboard is a Development-only convenience. Define the RaskDashboard "
                  + "authorization policy to say who may open /_rask.")]
    private static partial void Ignored(ILogger log, string environment);
}
