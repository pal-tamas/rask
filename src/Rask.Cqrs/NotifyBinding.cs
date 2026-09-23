using Microsoft.Extensions.Hosting;

namespace Rask.Cqrs;

/// <summary>
///     Hands <see cref="Notify" /> the root provider when a host starts, before anything has been dispatched.
/// </summary>
/// <remarks>
///     <see cref="NotifyRoot" /> covers every container, but only once something resolves a dispatcher. On a server
///     that could be the first request, which would leave a <c>Notify.Send</c> in a hosted service's own startup with
///     nowhere to publish. A host starts this first; a browser app and a test start no hosted services and are served
///     by <see cref="NotifyRoot" />.
/// </remarks>
internal sealed class NotifyBinding(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Notify.Configure(services);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
