using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;
using Rask.Spa.Hosting;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Server.Tests.Endpoints;

/// <summary>
///     A scoped-asset hash this process's registry lacks is answered from the app's web root.
/// </summary>
/// <remarks>
///     The case it exists for: the operator dashboard, server-rendered by <c>Rask.Server</c>, mounted
///     beside a WebAssembly app that <c>UseRaskSpa</c> serves. The dashboard's chain owns the
///     <c>/_rask/a/{hash}</c> route, routing gives it the app's asset requests before static files run,
///     and the app's hashes were registered in the browser's runtime, never in this one.
/// </remarks>
[Collection("ScopedAssets")]
public sealed class WebRootAssetFallbackTests : IDisposable
{
    private static readonly string _hash = new('a', ScopedAssetRegistry.HashHexLength);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "rask-webroot-" + Guid.NewGuid().ToString("N"));

    public WebRootAssetFallbackTests()
    {
        ScopedAssetRegistry.InvalidateAll();
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        LiveOptions.PathBase = string.Empty;
        LiveOptions.IsDevelopment = null;

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory left behind is not worth failing a run over.
        }
    }

    [Fact]
    public async Task A_hash_the_registry_lacks_is_served_from_the_web_root()
    {
        var webRoot = Seed("wwwroot", $"_rask/a/{_hash}.js", "window.Rask=window.Rask||{};");
        await using var app = await StartAsync(webRoot, spaBundle: null);
        using var http = app.GetTestClient();

        var response = await http.GetAsync($"/_rask/a/{_hash}.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", response.Headers.GetValues("Cache-Control").Single(), StringComparison.Ordinal);
        Assert.Equal($"\"{_hash}\"", response.Headers.ETag?.ToString());
        Assert.Contains("window.Rask", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_web_root_file_goes_out_as_its_precompressed_sibling()
    {
        var webRoot = Seed("wwwroot", $"_rask/a/{_hash}.js", "window.Rask=window.Rask||{};");
        byte[] brotli = [0x42, 0x52, 0x07, 0x08];
        await File.WriteAllBytesAsync(Path.Combine(webRoot, "_rask", "a", _hash + ".js.br"), brotli);
        await using var app = await StartAsync(webRoot, spaBundle: null);
        using var http = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/_rask/a/{_hash}.js");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("br", response.Content.Headers.ContentEncoding);
        Assert.Equal(brotli, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task A_hash_found_nowhere_is_a_404()
    {
        await using var app = await StartAsync(Path.Combine(_root, "empty"), spaBundle: null);
        using var http = app.GetTestClient();

        var response = await http.GetAsync($"/_rask/a/{_hash}.css");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_dashboard_beside_a_wasm_app_serves_the_apps_scoped_assets_and_leaves_it_its_routes()
    {
        // The bundle lives outside the web root, the way a build-machine dist path or an explicit
        // distPath does, so only UseRaskSpa sharing its _rask/a subtree can make the file reachable.
        var bundle = Seed("bundle", $"_rask/a/{_hash}.css", ".app{color:teal}");
        await File.WriteAllTextAsync(Path.Combine(bundle, "index.html"), "<!doctype html><body data-rask-root>spa</body>");
        await File.WriteAllTextAsync(Path.Combine(bundle, "rask.wasm.js"), "export function boot() {}");
        Directory.CreateDirectory(Path.Combine(bundle, "_framework"));
        await File.WriteAllTextAsync(Path.Combine(bundle, "_framework", "dotnet.js"), "export default {};");

        await using var app = await StartAsync(Path.Combine(_root, "empty"), bundle);
        using var http = app.GetTestClient();

        var asset = await http.GetAsync($"/_rask/a/{_hash}.css");
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal(".app{color:teal}", await asset.Content.ReadAsStringAsync());

        var dashboard = await http.GetAsync("/_rask");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Contains("dashboard-marker", await dashboard.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var clientRoute = await http.GetAsync("/orders/42");
        Assert.Equal(HttpStatusCode.OK, clientRoute.StatusCode);
        Assert.Contains("data-rask-root", await clientRoute.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private string Seed(string directory, string relative, string content)
    {
        var root = Path.Combine(_root, directory);
        var full = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return root;
    }

    private static async Task<WebApplication> StartAsync(string webRoot, string? spaBundle)
    {
        Directory.CreateDirectory(webRoot);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { WebRootPath = webRoot });
        builder.WebHost.UseTestServer();
        builder.Services.AddRaskServer();

        var app = builder.Build();
        app.UseRouting();

        if (spaBundle is null)
        {
            app.UseRask<DashboardApp>();
        }
        else
        {
            app.UseRaskServer<DashboardApp>("/_rask/{**path}");
            app.UseRaskSpa(spaBundle);
        }

        await app.StartAsync();
        return app;
    }

    private sealed class DashboardApp : Component
    {
        protected override Component? Render() => Div.Id("dashboard-marker")["dashboard"];
    }
}
