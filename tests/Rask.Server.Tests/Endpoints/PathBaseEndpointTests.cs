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
    public async Task The_prefixed_root_renders_the_app()
    {
        using var host = RaskTestHost.Create<TestApp>(pathBase: "/appA");

        var response = await host.Http.GetAsync("/appA/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("path=/", body);
    }

    [Fact]
    public async Task The_unprefixed_root_answers_404_when_a_path_base_is_configured()
    {
        using var host = RaskTestHost.Create<TestApp>(pathBase: "/appA");

        var response = await host.Http.GetAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_prefixed_runtime_script_serves_rask_js()
    {
        using var host = RaskTestHost.Create<TestApp>(pathBase: "/appA");

        var response = await host.Http.GetAsync("/appA/rask/rask.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("WebSocket", body);
    }

    [Fact]
    public async Task The_unprefixed_runtime_script_answers_404_when_a_path_base_is_configured()
    {
        using var host = RaskTestHost.Create<TestApp>(pathBase: "/appA");

        var response = await host.Http.GetAsync("/rask/rask.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_prefixed_asset_endpoint_serves_scoped_CSS()
    {
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(Widget), out var hash);
        using var host = RaskTestHost.Create<TestApp>(pathBase: "/appA");

        var response = await host.Http.GetAsync($"/appA/_rask/a/{hash}.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_unprefixed_asset_endpoint_answers_404_when_a_path_base_is_configured()
    {
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(Widget), out var hash);
        using var host = RaskTestHost.Create<TestApp>(pathBase: "/appA");

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.css");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_prefixed_page_links_to_prefixed_scoped_assets()
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
    public async Task An_empty_path_base_keeps_the_legacy_root_relative_paths()
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
    public async Task A_path_base_with_a_trailing_slash_is_normalized()
    {
        // "/appA/" must resolve to the same endpoints as "/appA".
        using var host = RaskTestHost.Create<TestApp>(pathBase: "/appA/");

        var response = await host.Http.GetAsync("/appA/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_path_base_is_stripped_from_the_request_path_before_route_resolution()
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
        protected override Component? Render() => this;
    }
}
