using System.IO.Compression;
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

    [Fact]
    public async Task PrecompressedSiblingNextToScopedAsset_DoesNotMislabelEndpointBytes()
    {
        // Regression: the SDK bakes .br/.gz siblings next to the baked /_rask/a/{hash} files,
        // but in an ASP.NET host those URLs are served by the in-process scoped-asset endpoint
        // (uncompressed), not UseStaticFiles. PrecompressedFileMiddleware used to spot the .br
        // sibling on disk and attach Content-Encoding: br to the endpoint's plain bytes — the
        // browser then failed to brotli-decode plaintext (ERR_CONTENT_DECODING_FAILED). The
        // middleware must skip when routing already matched an endpoint.
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".y { color: blue; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var bundle = new FakeBundleDirectory();
        var scopedDir = Path.Combine(bundle.Path, "_rask", "a");
        Directory.CreateDirectory(scopedDir);
        // A bogus .br sibling: if the middleware (wrongly) engaged, the response would carry
        // Content-Encoding: br over the endpoint's real (uncompressed) CSS bytes.
        File.WriteAllBytes(Path.Combine(scopedDir, $"{hash}.css.br"), new byte[] { 0x42, 0x52, 0x09, 0x09 });
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/_rask/a/{hash}.css");
        req.Headers.AcceptEncoding.ParseAdd("br");
        var response = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The endpoint now compresses its OWN immutable bytes (Content-Encoding: br). Whatever
        // representation arrives must DECODE to the real registry asset — never the bogus on-disk
        // .br sibling: the precompressed-file middleware must not engage for an endpoint-matched
        // request and overwrite the body with that 4-byte garbage.
        var raw = await response.Content.ReadAsByteArrayAsync();
        byte[] decoded;
        if (response.Content.Headers.ContentEncoding.Contains("br"))
        {
            using var dst = new MemoryStream();
            await using (var br = new BrotliStream(new MemoryStream(raw), CompressionMode.Decompress))
            {
                await br.CopyToAsync(dst);
            }

            decoded = dst.ToArray();
        }
        else
        {
            decoded = raw; // identity (or the client already decoded)
        }

        var expected = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css)!.Value.Utf8.ToArray();
        Assert.Equal(expected, decoded);
        Assert.NotEqual(new byte[] { 0x42, 0x52, 0x09, 0x09 }, decoded);
    }

    [Fact]
    public async Task RegistryMiss_FallsBackToBakedBundleFile()
    {
        // A published WASM host's in-process registry can be a strict subset of the in-WASM-runtime
        // set (only assemblies this host touched register), so its hash for the single concatenated
        // CSS/JS bundle won't match the browser's request. The baked /_rask/a/{hash}.{ext} the publish
        // wrote is authoritative — the endpoint must serve it, not 404-shadow it. (UseStaticFiles can't
        // serve it: routing matched this endpoint.) Registry is empty here, so the hash only resolves
        // via the baked-file fallback.
        using var bundle = new FakeBundleDirectory();
        var scopedDir = Path.Combine(bundle.Path, "_rask", "a");
        Directory.CreateDirectory(scopedDir);
        const string hash = "0123456789ab";
        File.WriteAllText(Path.Combine(scopedDir, $"{hash}.js"), "window.Rask=window.Rask||{};");
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        var ccRaw = response.Headers.GetValues("Cache-Control").First();
        Assert.Contains("immutable", ccRaw);
        Assert.Equal($"\"{hash}\"", response.Headers.ETag?.ToString());
        Assert.Contains("window.Rask", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RegistryMiss_BakedBundleFile_NegotiatesPrecompressedSibling()
    {
        // The baked-file fallback honours a precompressed .br sibling (the WASM publish bakes them),
        // serving it verbatim with Content-Encoding: br — zero request-time CPU.
        using var bundle = new FakeBundleDirectory();
        var scopedDir = Path.Combine(bundle.Path, "_rask", "a");
        Directory.CreateDirectory(scopedDir);
        const string hash = "0123456789ab";
        File.WriteAllText(Path.Combine(scopedDir, $"{hash}.js"), "window.Rask=window.Rask||{};");
        var brBytes = new byte[] { 0x42, 0x52, 0x07, 0x08 };
        File.WriteAllBytes(Path.Combine(scopedDir, $"{hash}.js.br"), brBytes);
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/_rask/a/{hash}.js");
        req.Headers.AcceptEncoding.ParseAdd("br");
        var response = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("br", response.Content.Headers.ContentEncoding);
        Assert.Equal(brBytes, await response.Content.ReadAsByteArrayAsync());
    }

    private sealed class WidgetA : Component
    {
        protected override RenderResult Render() => this;
    }
}
