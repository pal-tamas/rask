using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Rask.Example.Wasm.Host;
using Rask.Wasm.Hosting;

namespace Rask.Example.Wasm.Host.Tests.Infrastructure;

// Replays the wiring in Rask.Example.Wasm.Host/Program.cs over a TestServer with an
// explicit fake bundle path. Mirrors the production setup: AddRask (compression)
// + UseRask (compression + precompressed + static-files + index fallback).
internal sealed class ExampleHostTestServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private ExampleHostTestServer(WebApplication app)
    {
        _app = app;
        Server = app.GetTestServer();
        Http = Server.CreateClient();
    }

    public TestServer Server { get; }
    public HttpClient Http { get; }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    public static async Task<ExampleHostTestServer> CreateAsync(string bundlePath)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRask(); // opt into compression — same as production
        builder.Services.AddPushDemo(builder.Configuration); // Web Push backend — same as production

        var app = builder.Build();
        app.UseRouting();
        app.MapPushDemo();
        app.UseRask(bundlePath);

        await app.StartAsync();
        return new ExampleHostTestServer(app);
    }
}
