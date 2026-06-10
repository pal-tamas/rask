using System.Net;
using Rask.Core;
using Rask.Core.ScopedAssets;
using Rask.Wasm.Hosting.Tests.Infrastructure;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Wasm.Hosting.Tests;

/// <summary>
///     Cross-host parity: the per-component asset endpoint registered by
///     <c>Rask.Wasm.Hosting</c> serves identical bytes for the same content hash as the
///     <c>Rask.Server</c> endpoint. The two endpoints share the same in-process
///     <see cref="ScopedAssetRegistry" /> static storage, so the parity is structural.
/// </summary>
[Collection("ScopedAssets")]
public class AssetEndpointParityTests
{
    public AssetEndpointParityTests() => ScopedAssetRegistry.InvalidateAll();

    [Fact]
    public async Task GetCss_KnownHash_ReturnsContentAddressedAsset()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsByteArrayAsync();
        var expected = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css)!.Value.Utf8.ToArray();
        Assert.Equal(expected, body);
    }

    [Fact]
    public async Task GetJs_KnownHash_ReturnsContentAddressedAsset()
    {
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f(){}");
        ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out var hash);
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ImmutableCacheHeaders_Match_ServerEndpoint()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.css");
        var ccRaw = response.Headers.GetValues("Cache-Control").First();
        Assert.Contains("immutable", ccRaw);
        Assert.Contains("max-age=31536000", ccRaw);
        Assert.Equal($"\"{hash}\"", response.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task NosniffHeader_IsPresent_LikeServerEndpoint()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.css");
        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var v));
        Assert.Equal("nosniff", v.Single());
    }

    [Fact]
    public async Task IfNoneMatch_Returns304_LikeServerEndpoint()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/_rask/a/{hash}.css");
        req.Headers.TryAddWithoutValidation("If-None-Match", $"\"{hash}\"");
        var response = await host.Http.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }

    [Fact]
    public async Task UnknownHash_Returns404()
    {
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);
        var response = await host.Http.GetAsync("/_rask/a/abcdef012345.css");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UppercaseHash_Returns404_SameAsServer()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync($"/_rask/a/{hash.ToUpperInvariant()}.css");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HeadRequest_ReturnsHeadersWithoutBody()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        using var req = new HttpRequestMessage(HttpMethod.Head, $"/_rask/a/{hash}.css");
        var response = await host.Http.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"\"{hash}\"", response.Headers.ETag?.ToString());
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task PostMethod_DoesNotReturnAssetBody()
    {
        // Behavioral difference vs Server: under the SPA fallback (MapFallback to
        // index.html), a POST to /_rask/a/{hash}.css is caught by the fallback (the
        // method-mismatch 405 from routing doesn't fire because the fallback claims any
        // unmatched-method request). What matters is the security contract: POST never
        // returns the asset's CSS/JS bytes.
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        var assetBytes = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css)!.Value.Utf8.ToArray();
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"/_rask/a/{hash}.css")
        {
            Content = new StringContent("")
        };
        var response = await host.Http.SendAsync(req);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEqual(assetBytes, body);
    }

    [Fact]
    public async Task BundleStaticFile_DoesNotShadow_AssetEndpoint()
    {
        // Verify that even when the published bundle contains files under /_rask/a/, the
        // dynamic endpoint takes precedence (asset endpoint is registered before the
        // static-file middleware in UseRask).
        using var bundle = new FakeBundleDirectory();
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
    }

    private sealed class WidgetA : Component
    {
        protected override RenderResult Render() => this;
    }
}
