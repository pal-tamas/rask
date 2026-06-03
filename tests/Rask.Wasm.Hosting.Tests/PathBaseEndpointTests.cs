using System.Net;
using Rask.Core;
using Rask.Core.ScopedAssets;
using Rask.Wasm.Hosting.Tests.Infrastructure;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Wasm.Hosting.Tests;

/// <summary>
///     Verifies <c>app.UseRask&lt;TApp&gt;(bundlePath, pathBase: "/sub")</c> mounts the
///     entire static-file surface (index.html, scoped-asset endpoint, SPA fallback)
///     under the prefix and returns 404 at the origin root. Required so two WASM
///     AppBundles can coexist in one host process.
/// </summary>
[Collection("ScopedAssets")]
public sealed class PathBaseEndpointTests
{
    [Fact]
    public async Task PrefixedRoot_ServesIndexHtml()
    {
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path, pathBase: "/sub");

        var response = await host.Http.GetAsync("/sub/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-rask-root", body);
    }

    [Fact]
    public async Task UnprefixedRoot_Returns404_WhenPathBaseConfigured()
    {
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path, pathBase: "/sub");

        var response = await host.Http.GetAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PrefixedStaticFile_ServesWasmWithCorrectMime()
    {
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path, pathBase: "/sub");

        var response = await host.Http.GetAsync("/sub/_framework/foo.wasm");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/wasm", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnprefixedStaticFile_Returns404_WhenPathBaseConfigured()
    {
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path, pathBase: "/sub");

        var response = await host.Http.GetAsync("/_framework/foo.wasm");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PrefixedAssetEndpoint_ServesScopedCss()
    {
        ScopedAssetRegistry.InvalidateAll();
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(Widget), out var hash);
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path, pathBase: "/sub");

        var response = await host.Http.GetAsync($"/sub/_rask/a/{hash}.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnprefixedAssetEndpoint_Returns404_WhenPathBaseConfigured()
    {
        ScopedAssetRegistry.InvalidateAll();
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(Widget), out var hash);
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path, pathBase: "/sub");

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.css");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PrefixedSpaFallback_ServesIndexHtmlForUnknownRoute()
    {
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path, pathBase: "/sub");

        var response = await host.Http.GetAsync("/sub/users/42");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-rask-root", body);
    }

    [Fact]
    public async Task PathBase_NormalizesTrailingSlash()
    {
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path, pathBase: "/sub/");

        var response = await host.Http.GetAsync("/sub/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EmptyPathBase_PreservesLegacyRootRelativeBehavior()
    {
        // Regression guard: the original UseRask() without a pathBase must still serve at root.
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BundleMissingWithPathBase_PrefixedRoute_Returns503()
    {
        await using var host = await WasmHostingTestServer.CreateAsync(
            "/tmp/rask-wasm-pathbase-tests-definitely-not-here",
            pathBase: "/sub");

        var response = await host.Http.GetAsync("/sub/anything");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task BundleMissingWithPathBase_UnprefixedRoute_Returns404()
    {
        await using var host = await WasmHostingTestServer.CreateAsync(
            "/tmp/rask-wasm-pathbase-tests-definitely-not-here",
            pathBase: "/sub");

        var response = await host.Http.GetAsync("/anything");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class Widget : Component { protected override RenderResult Render() => this; }
}
