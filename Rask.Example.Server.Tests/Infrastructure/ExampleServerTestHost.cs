using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Rask.Example.Shared;
using Rask.Server;

namespace Rask.Example.Server.Tests.Infrastructure;

// Spins up the same wiring as Rask.Example.Server/Program.cs over an in-memory
// TestServer. Used by hosting tests to assert root GET routing and DI shape.
internal sealed class ExampleServerTestHost : IDisposable
{
    private readonly WebApplication _app;

    private ExampleServerTestHost(WebApplication app, TestServer server)
    {
        _app = app;
        Server = server;
        Http = server.CreateClient();
    }

    public TestServer Server { get; }
    public HttpClient Http { get; }

    public void Dispose()
    {
        Http.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public static ExampleServerTestHost Create()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddRask();
        builder.Services.AddExampleServices();

        var app = builder.Build();
        app.UseRouting();
        app.UseWebSockets();
        app.UseRask<App>();
        app.StartAsync().GetAwaiter().GetResult();

        return new ExampleServerTestHost(app, app.GetTestServer());
    }
}
