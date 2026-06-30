using System.Net;
using Rask.Core;
using Rask.Core.ScopedAssets;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Server.Tests.Endpoints;

/// <summary>
///     Verifies <c>app.UseRask&lt;TApp&gt;(pathBase: "/appA")</c> scopes every
///     framework-owned endpoint under the prefix and that the same endpoint paths
///     are unreachable at origin root. Required so two Rask Server apps can live
///     side-by-side behind a reverse proxy without colliding.
/// </summary>
[Collection("ScopedAssets")]
public sealed class PathBaseEndpointTests
{
    public PathBaseEndpointTests() => ScopedAssetRegistry.InvalidateAll();

    [Fact]
    public async Task PrefixedRoot_RendersAppHtml()
    {
        using var host = RaskTestHost.Create<TestApp>(pathBase: "/appA");

        var response = await host.Http.GetAsync("/appA/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("path=/", body);
    }

    [Fact]
    public async Task UnprefixedRoot_Returns404WhenPathBaseConfigured()
    {
        using var host = RaskTestHost.Create<TestApp>(pathBase: "/appA");

        var response = await host.Http.GetAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PrefixedRuntimeScript_ServesRaskJs()
    {
        using var host = RaskTestHost.Create<TestApp>(pathBase: "/appA");

        var response = await host.Http.GetAsync("/appA/rask/rask.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("WebSocket", body);
    }

    [Fact]
    public async Task UnprefixedRuntimeScript_Returns404WhenPathBaseConfigured()
    {
        using var host = RaskTestHost.Create<TestApp>(pathBase: "/appA");

        var response = await host.Http.GetAsync("/rask/rask.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PrefixedAssetEndpoint_ServesScopedCss()
    {
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(Widget), out var hash);
        using var host = RaskTestHost.Create<TestApp>(pathBase: "/appA");

        var response = await host.Http.GetAsync($"/appA/_rask/a/{hash}.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnprefixedAssetEndpoint_Returns404WhenPathBaseConfigured()
    {
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(Widget), out var hash);
        using var host = RaskTestHost.Create<TestApp>(pathBase: "/appA");

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.css");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PrefixedHtml_LinksToPrefixedScopedAssets()
    {
        // A component with scoped CSS is rendered; the head must emit <link href="/appA/_rask/a/...">
        // not the legacy "/_rask/a/..." path. End-to-end check that LiveOptions.PathBase
        // assignment at UseRask time propagates into the head emission on first paint.
        ScopedAssetRegistry.RegisterCss(typeof(TestApp), ".test { color: blue; }");
        var hash = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);
        using var host = RaskTestHost.Create<TestApp>(pathBase: "/appA");

        var response = await host.Http.GetAsync("/appA/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains($"/appA/_rask/a/{hash}.css", body);
        Assert.DoesNotContain($"\"/_rask/a/{hash}.css\"", body);
    }

    [Fact]
    public async Task EmptyPathBase_KeepsLegacyRootRelativePaths()
    {
        // Regression guard for the pre-PathBase behavior — empty pathBase must not alter
        // the existing endpoint surface.
        using var host = RaskTestHost.Create<TestApp>(pathBase: "");

        var rootRes = await host.Http.GetAsync("/");
        var runtimeRes = await host.Http.GetAsync("/rask/rask.js");

        Assert.Equal(HttpStatusCode.OK, rootRes.StatusCode);
        Assert.Equal(HttpStatusCode.OK, runtimeRes.StatusCode);
    }

    [Fact]
    public async Task PathBase_NormalizesTrailingSlash()
    {
        // "/appA/" must resolve to the same endpoints as "/appA".
        using var host = RaskTestHost.Create<TestApp>(pathBase: "/appA/");

        var response = await host.Http.GetAsync("/appA/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PathBase_StripsPrefixFromRequestPathBeforeRouteResolution()
    {
        // The TestApp renders "path=" + RouteState.Path. A request to /appA/foo should
        // hit RouteState.Path="/foo", not "/appA/foo", so user-space routes are unaware
        // of the mount prefix.
        using var host = RaskTestHost.Create<TestApp>(pathBase: "/appA");

        var response = await host.Http.GetAsync("/appA/foo");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("path=/foo", body);
    }

    private sealed class Widget : Component
    {
        protected override RenderResult Render() => this;
    }
}
