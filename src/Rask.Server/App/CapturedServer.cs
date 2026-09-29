using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;

namespace Rask.Server;

/// <summary>
///     The server an app gets while a test runs its <c>Program.cs</c>: it starts and stops with the host, so
///     startup work runs as it would in production, and it never opens a socket — the test renders pages itself.
/// </summary>
internal sealed class CapturedServer : IServer
{
    public IFeatureCollection Features { get; } = Addresses();

    public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
        where TContext : notnull => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
    }

    // Something at startup may ask where the app listens; the answer is nowhere.
    private static FeatureCollection Addresses()
    {
        var features = new FeatureCollection();
        features.Set<IServerAddressesFeature>(new ServerAddressesFeature());
        return features;
    }
}
