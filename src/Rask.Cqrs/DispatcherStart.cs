using Microsoft.Extensions.Hosting;

namespace Rask.Cqrs;

/// <summary>
///     Hands <see cref="Dispatcher" /> the root provider when a host starts, before anything has been dispatched.
/// </summary>
/// <remarks>
///     <see cref="DispatcherRoot" /> covers every container, but only once something resolves a dispatcher. On a
///     server that could be the first request, which would leave a <c>Dispatcher.Publish</c> in a hosted service's
///     own startup with no app to reach. A host starts this first.
/// </remarks>
internal sealed class DispatcherStart(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Dispatcher.Start(services);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
