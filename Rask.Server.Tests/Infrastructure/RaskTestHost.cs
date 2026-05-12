using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;

namespace Rask.Server.Tests.Infrastructure;

internal sealed class RaskTestHost : IDisposable
{
    private RaskTestHost(TestServer server)
    {
        Server = server;
        Http = server.CreateClient();
        WebSockets = server.CreateWebSocketClient();
        Store = server.Services.GetRequiredService<LiveSessionStore>();
    }

    public TestServer Server { get; }
    public HttpClient Http { get; }
    public WebSocketClient WebSockets { get; }
    public LiveSessionStore Store { get; }

    public Uri WebSocketUri => new(new Uri(Server.BaseAddress, "/rask/ws").ToString().Replace("http://", "ws://"));

    public void Dispose()
    {
        Http.Dispose();
        Server.Dispose();
    }

    public static RaskTestHost Create<TApp>(
        Action<IServiceCollection>? configureServices = null,
        Action<IApplicationBuilder>? configureMiddleware = null)
        where TApp : Component
    {
        var hostBuilder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddRask();
                configureServices?.Invoke(services);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseWebSockets();
                configureMiddleware?.Invoke(app);
                app.UseEndpoints(endpoints => endpoints.UseRask<TApp>());
            });

        return new RaskTestHost(new TestServer(hostBuilder));
    }
}
