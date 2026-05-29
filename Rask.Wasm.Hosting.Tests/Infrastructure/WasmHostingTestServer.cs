using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

namespace Rask.Wasm.Hosting.Tests.Infrastructure;

internal sealed class WasmHostingTestServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private WasmHostingTestServer(WebApplication app)
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
        // Reset the static PathBase so tests configuring a prefix don't leak into
        // subsequent tests sharing the AppDomain.
        Rask.Core.Live.LiveOptions.PathBase = string.Empty;
    }

    public static async Task<WasmHostingTestServer> CreateAsync(
        string? bundlePath,
        bool withCompression = false,
        string pathBase = "")
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        if (withCompression)
        {
            builder.Services.AddRask();
        }

        var app = builder.Build();
        app.UseRouting();
        app.UseRask(bundlePath, pathBase: pathBase);

        await app.StartAsync();
        return new WasmHostingTestServer(app);
    }
}
