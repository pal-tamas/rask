using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;

namespace Rask.Server.Tests.Infrastructure;

internal sealed class RaskTestHost : IDisposable
{
    private readonly WebApplication _app;

    private RaskTestHost(WebApplication app, TestServer server)
    {
        _app = app;
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
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        // Reset the static PathBase so a pathBase-configured host doesn't leak
        // into subsequent tests in the same AppDomain. Cheap; matches the
        // existing Diff-mode reset convention.
        Rask.Core.Live.LiveOptions.PathBase = string.Empty;
    }

    public static RaskTestHost Create<TApp>(
        Action<IServiceCollection>? configureServices = null,
        Action<IApplicationBuilder>? configureMiddleware = null,
        string pathBase = "")
        where TApp : Component
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddRask();
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseRouting();
        app.UseWebSockets();
        configureMiddleware?.Invoke(app);
        app.UseRask<TApp>(pathBase: pathBase);

        app.StartAsync().GetAwaiter().GetResult();

        var server = app.GetTestServer();
        return new RaskTestHost(app, server);
    }
}
