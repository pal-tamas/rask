using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
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

    public static async Task<WasmHostingTestServer> CreateAsync(string? bundlePath)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.UseRouting();
        ((IEndpointRouteBuilder)app).UseRask(bundlePath);

        await app.StartAsync();
        return new WasmHostingTestServer(app);
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
