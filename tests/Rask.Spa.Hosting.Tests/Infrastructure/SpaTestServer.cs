using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Spa.Hosting.Tests.Infrastructure;

/// <summary>
///     A host running <see cref="RaskSpaEndpointExtensions.UseRaskSpa" />.
/// </summary>
/// <remarks>
///     Deliberately not in a shared xUnit collection, unlike the WASM hosting tests. That package has
///     to serialise its host tests because <c>UseRask</c> writes two process-wide statics
///     (<c>ScopedAssetBundle.BakedDirectory</c> and <c>LiveOptions.PathBase</c>) that one test can
///     re-point out from under another. This package writes none, which is a consequence of it taking
///     no dependency on <c>Rask.Core</c> — so these tests can run in parallel.
/// </remarks>
internal sealed class SpaTestServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private SpaTestServer(WebApplication app)
    {
        _app = app;
        Http = app.GetTestServer().CreateClient();
    }

    public HttpClient Http { get; }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    public static async Task<SpaTestServer> CreateAsync(
        string? distPath,
        string environment = "Production",
        string pathBase = "",
        bool withCompression = false,
        bool withApi = false,
        Action<SpaHostingOptions>? configure = null,
        string? contentRoot = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            // Pinned to a directory with no wwwroot unless a test asks otherwise, so the
            // published-app resolution step is exercised only when it is the thing under test.
            ContentRootPath = contentRoot ?? Path.GetTempPath(),
        });
        builder.WebHost.UseTestServer();

        if (withCompression)
        {
            builder.Services.AddRaskSpaHost();
        }

        var app = builder.Build();
        app.UseRouting();

        if (withApi)
        {
            // Mapped before UseRaskSpa, which is the documented contract.
            app.MapGet("/api/ping", () => Results.Text("pong"));
        }

        app.UseRaskSpa(distPath, pathBase, configure);

        await app.StartAsync();
        return new SpaTestServer(app);
    }
}
