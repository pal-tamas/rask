using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Browser;
using Rask.Core.Components;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories
#pragma warning disable RASK019 // test-infra app fills <head> inline

namespace Rask.Server.Tests.Endpoints;

/// <summary>
///     End-to-end: opting into PWA with <c>AddRaskPwa</c> serves the manifest + service worker and emits
///     the manifest link into the server-rendered <c>&lt;head&gt;</c>; without it, none of that appears.
/// </summary>
// In the non-parallel "ScopedAssets" collection: the head contribution reads the process-wide
// LiveOptions.PathBase static at render time, which a concurrently-configured host would clobber.
[Collection("ScopedAssets")]
public sealed class ServerPwaTests
{
    private static WebAppManifest SampleManifest() => new()
    {
        Name = "Rask Server Showcase",
        ShortName = "Rask",
        ThemeColor = "#512BD4",
        Icons = [new ManifestIcon("icon.svg", "any", "image/svg+xml", "any maskable")]
    };

    [Fact]
    public async Task Default_NoPwa_NoManifestLinkAndNoPwaEndpoints()
    {
        using var host = RaskTestHost.Create<ShellApp>();

        var body = await (await host.Http.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("rel=\"manifest\"", body);

        // The PWA endpoints are not mapped, so these paths fall through to the SPA catch-all and render
        // the app shell (text/html) rather than serving manifest JSON / the service-worker script.
        var manifest = await host.Http.GetAsync("/rask/manifest.webmanifest");
        Assert.NotEqual("application/manifest+json", manifest.Content.Headers.ContentType?.MediaType);

        var sw = await host.Http.GetAsync("/rask-sw.js");
        Assert.NotEqual("text/javascript", sw.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AddRaskPwa_EmitsManifestLinkAndThemeColorInHead()
    {
        using var host = RaskTestHost.Create<ShellApp>(s => s.AddRaskPwa(SampleManifest()));

        var body = await (await host.Http.GetAsync("/")).Content.ReadAsStringAsync();

        Assert.Contains("rel=\"manifest\"", body);
        Assert.Contains("href=\"/rask/manifest.webmanifest\"", body);
        Assert.Contains("name=\"theme-color\"", body);
        Assert.Contains("content=\"#512BD4\"", body);
        // AddRaskPwa auto-registers the service worker so the app is installable with one call.
        Assert.Contains("serviceWorker", body);
        Assert.Contains("register(\"/rask-sw.js\")", body);
        // Exactly one manifest link, and it sits inside <head>.
        Assert.Equal(body.IndexOf("rel=\"manifest\"", StringComparison.Ordinal),
            body.LastIndexOf("rel=\"manifest\"", StringComparison.Ordinal));
        Assert.True(body.IndexOf("rel=\"manifest\"", StringComparison.Ordinal)
            < body.IndexOf("</head>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddRaskPwa_WithoutThemeColor_OmitsMetaButKeepsLink()
    {
        using var host = RaskTestHost.Create<ShellApp>(s => s.AddRaskPwa(new WebAppManifest { Name = "Bare" }));

        var body = await (await host.Http.GetAsync("/")).Content.ReadAsStringAsync();

        Assert.Contains("rel=\"manifest\"", body);
        Assert.DoesNotContain("name=\"theme-color\"", body);
    }

    [Fact]
    public async Task ManifestEndpoint_ServesManifestJsonWithRootedUrls()
    {
        using var host = RaskTestHost.Create<ShellApp>(s => s.AddRaskPwa(SampleManifest()));

        var response = await host.Http.GetAsync("/rask/manifest.webmanifest");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/manifest+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("Rask Server Showcase", root.GetProperty("name").GetString());
        Assert.Equal("/", root.GetProperty("start_url").GetString());
        Assert.Equal("/icon.svg", root.GetProperty("icons")[0].GetProperty("src").GetString());
    }

    [Fact]
    public async Task ServiceWorkerEndpoint_ServesOfflineFallbackSwNotAppShell()
    {
        using var host = RaskTestHost.Create<ShellApp>(s => s.AddRaskPwa(SampleManifest()));

        var response = await host.Http.GetAsync("/rask-sw.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);

        var sw = await response.Content.ReadAsStringAsync();
        // Shared push handler is present...
        Assert.Contains("addEventListener(\"push\"", sw);
        Assert.Contains("offline.html", sw);
        // ...but the Server SW must NOT carry the WASM app-shell navigation cache.
        Assert.DoesNotContain("rask-cache-v1", sw);
        Assert.DoesNotContain("index.html", sw);
    }

    [Fact]
    public async Task PathBase_RootsManifestLinkEndpointAndStartUrl()
    {
        using var host = RaskTestHost.Create<ShellApp>(s => s.AddRaskPwa(SampleManifest()), pathBase: "/appA");

        var body = await (await host.Http.GetAsync("/appA/")).Content.ReadAsStringAsync();
        Assert.Contains("href=\"/appA/rask/manifest.webmanifest\"", body);

        var json = await (await host.Http.GetAsync("/appA/rask/manifest.webmanifest")).Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("/appA/", doc.RootElement.GetProperty("start_url").GetString());
        Assert.Equal("/appA/icon.svg", doc.RootElement.GetProperty("icons")[0].GetProperty("src").GetString());

        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/appA/rask-sw.js")).StatusCode);
    }

    private sealed class ShellApp : Component
    {
        protected override RenderResult Render() =>
            [Doctype(), new Html()[new Head(), new Body()[new H1()["hi"]]]];
    }
}
