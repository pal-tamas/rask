using Microsoft.Extensions.Hosting;
using Rask.Core;

namespace Rask.Server;

/// <summary>
///     Hands a started app to the test that is running its <c>Program.cs</c> — after every other hosted service has
///     started, so migrations and seeding are done — together with the app's own way to stop.
/// </summary>
internal sealed class CapturedHandOff(AppCapture.Capture capture, IServiceProvider services, IHostApplicationLifetime lifetime)
    : IHostedLifecycleService
{
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        capture.Hand(services, lifetime.StopApplication);
        return Task.CompletedTask;
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
