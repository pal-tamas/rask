using Microsoft.Extensions.DependencyInjection;

namespace Rask.Querying;

/// <summary>
///     Holds the session's <see cref="SessionQueryClient" /> and stops it when the session's scope ends. The client
///     itself is handed to every event's scope too, so it cannot be what the scope disposes.
/// </summary>
internal sealed class SessionQueryClientOwner(IServiceProvider services) : IDisposable
{
    internal SessionQueryClient Client { get; } = ActivatorUtilities.CreateInstance<SessionQueryClient>(services);

    public void Dispose() => Client.Stop();
}
