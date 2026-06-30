using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Rask.Core;
using Rask.Core.ScopedAssets;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Server.Tests.Endpoints;

/// <summary>
///     Verifies the per-component content-addressed asset endpoint:
///     <c>GET /_rask/a/{12-hex}.{css|js}</c>. Coverage spans HTTP method semantics,
///     ETag/cache, Range requests, hash validation, and method-not-allowed routing.
/// </summary>
[Collection("ScopedAssets")]
public class AssetEndpointTests
{
    public AssetEndpointTests() => ScopedAssetRegistry.InvalidateAll();

    // ─── Happy path ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetCss_KnownHash_Returns200_WithImmutableCacheAndEtag()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        var cc = response.Headers.CacheControl;
        Assert.NotNull(cc);
        Assert.True(cc!.Public);
        Assert.True(cc.MaxAge.HasValue);
        // immutable directive may parse as extension; assert via raw header value:
        var ccRaw = response.Headers.GetValues("Cache-Control").First();
        Assert.Contains("immutable", ccRaw);
        Assert.Contains("max-age=31536000", ccRaw);
        Assert.Equal($"\"{hash}\"", response.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task GetJs_KnownHash_Returns200_WithJavaScriptContentType()
    {
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f(){}");
        ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetCss_BodyBytes_AreByteEqualToRegistryStorage()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        var registryBytes = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css)!.Value.Utf8.ToArray();
        using var host = RaskTestHost.Create<TestApp>();

        var bodyBytes = await host.Http.GetByteArrayAsync($"/_rask/a/{hash}.css");
        Assert.Equal(registryBytes, bodyBytes);
    }

    [Fact]
    public async Task NosniffHeader_IsPresent_OnAssetResponses()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.css");
        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var v));
        Assert.Equal("nosniff", v.Single());
    }

    // ─── HTTP method semantics ────────────────────────────────────────────

    [Fact]
    public async Task Head_KnownHash_Returns200_NoBody_SameHeaders()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        using var req = new HttpRequestMessage(HttpMethod.Head, $"/_rask/a/{hash}.css");
        var response = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"\"{hash}\"", response.Headers.ETag?.ToString());
        var bodyBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(bodyBytes);
    }

    [Fact]
    public async Task Post_AssetEndpoint_Returns405()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        using var req = new HttpRequestMessage(HttpMethod.Post, $"/_rask/a/{hash}.css")
        {
            Content = new StringContent("")
        };
        var response = await host.Http.SendAsync(req);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Put_AssetEndpoint_Returns405()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        using var req = new HttpRequestMessage(HttpMethod.Put, $"/_rask/a/{hash}.css")
        {
            Content = new StringContent("")
        };
        var response = await host.Http.SendAsync(req);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AssetEndpoint_Returns405()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        using var req = new HttpRequestMessage(HttpMethod.Delete, $"/_rask/a/{hash}.css");
        var response = await host.Http.SendAsync(req);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // ─── ETag / cache ─────────────────────────────────────────────────────

    [Fact]
    public async Task IfNoneMatchExact_Returns304_NoBody()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/_rask/a/{hash}.css");
        req.Headers.TryAddWithoutValidation("If-None-Match", $"\"{hash}\"");
        var response = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task IfNoneMatchStale_Returns200WithBody()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/_rask/a/{hash}.css");
        req.Headers.TryAddWithoutValidation("If-None-Match", "\"000000000000\"");
        var response = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    // ─── Range requests ──────────────────────────────────────────────────

    [Fact]
    public async Task RangeFirstHundredBytes_Returns206PartialContent()
    {
        // Pad source to ensure rewritten bytes exceed 100 bytes.
        var bigCss = string.Concat(Enumerable.Repeat(".x { color: red; }\n", 20));
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), bigCss);
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/_rask/a/{hash}.css");
        req.Headers.Range = new RangeHeaderValue(0, 99);
        var response = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(100, body.Length);
    }

    [Fact]
    public async Task RangeBeyondLength_Returns416()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/_rask/a/{hash}.css");
        // Request bytes way past end of the small body.
        req.Headers.TryAddWithoutValidation("Range", "bytes=100000-200000");
        var response = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
    }

    // ─── Negative paths ──────────────────────────────────────────────────

    [Fact]
    public async Task UnknownHash_Returns404()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var response = await host.Http.GetAsync("/_rask/a/abcdef012345.css");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CrossKindMismatch_CssHashRequestedAsJs_Returns404()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var cssHash);
        using var host = RaskTestHost.Create<TestApp>();

        // The hash is real for CSS but unknown to JS bucket.
        var response = await host.Http.GetAsync($"/_rask/a/{cssHash}.js");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpperCaseHash_Returns404()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        // Same hash but uppercase — rejected by IsLowercaseHex check.
        var response = await host.Http.GetAsync($"/_rask/a/{hash.ToUpperInvariant()}.css");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NonHexCharacters_Returns404()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var response = await host.Http.GetAsync("/_rask/a/notthexnoth.css");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HashTooShort_Returns404()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var response = await host.Http.GetAsync("/_rask/a/abc.css");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HashTooLong_Returns404()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var response = await host.Http.GetAsync("/_rask/a/abcdef0123456789.css");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownExtension_DoesNotServeAssetContent()
    {
        // /_rask/a/{hash}.gif doesn't match either route — falls through to the framework's
        // App fallback (which renders the home page with text/html). The asset endpoint
        // itself does NOT serve the .gif request; what matters is that no CSS/JS body
        // leaks. Asserting Content-Type is the cleanest signal.
        using var host = RaskTestHost.Create<TestApp>();
        var response = await host.Http.GetAsync("/_rask/a/abcdef012345.gif");
        var ct = response.Content.Headers.ContentType?.MediaType;
        Assert.NotEqual("text/css", ct);
        Assert.NotEqual("text/javascript", ct);
        Assert.NotEqual("application/javascript", ct);
    }

    [Fact]
    public async Task PathTraversalAttempt_DoesNotServeRegisteredAsset()
    {
        // After URL normalization the request hits a different path; the framework's App
        // fallback may return 200 with the home page. Critical: no asset bytes leaked.
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        var assetBytes = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css)!.Value.Utf8.ToArray();
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync($"/_rask/a/../../etc/{hash}.css");
        var body = await response.Content.ReadAsByteArrayAsync();
        // The exact body depends on the framework's fallback, but it must NOT be the
        // asset bytes (path traversal must not bypass the route constraint).
        Assert.NotEqual(assetBytes, body);
    }

    // ─── Concurrency ──────────────────────────────────────────────────────

    [Fact]
    public async Task Concurrent100Gets_AllReturnIdenticalBytes()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        var expected = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css)!.Value.Utf8.ToArray();
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => host.Http.GetByteArrayAsync($"/_rask/a/{hash}.css"))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, body => Assert.Equal(expected, body));
    }

    // ─── Hash content edge cases ─────────────────────────────────────────

    [Fact]
    public async Task LargeAsset_AboveOneMb_ServesSuccessfully()
    {
        var bigSource = new StringBuilder();
        for (var i = 0; i < 20_000; i++)
        {
            bigSource.Append(".class").Append(i).Append(" { color: rgb(")
                .Append(i % 256).Append(",0,0); padding: 1px 2px 3px 4px; margin: 0; }\n");
        }

        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), bigSource.ToString());
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        var body = await host.Http.GetByteArrayAsync($"/_rask/a/{hash}.css");
        Assert.True(body.Length > 1_000_000, $"expected >1MB body, got {body.Length}");
    }

    [Fact]
    public async Task NonAsciiContent_ServesByteIdentically()
    {
        // UTF-8 content with multi-byte chars (emoji, RTL marks). Round-trip must preserve.
        const string css = ".x::before { content: '🎨'; } /* مرحبا */";
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), css);
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        var expected = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css)!.Value.Utf8.ToArray();
        using var host = RaskTestHost.Create<TestApp>();

        var body = await host.Http.GetByteArrayAsync($"/_rask/a/{hash}.css");
        Assert.Equal(expected, body);
    }

    [Fact]
    public async Task AcceptBrotli_ServesValidBrotli_ThatDecodesToTheAsset()
    {
        // A payload big enough that brotli measurably shrinks it.
        var css = string.Concat(Enumerable.Range(0, 200)
            .Select(i => $".r{i} {{ color: rgb({i % 256},0,0); }}\n"));
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), css);
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        var expected = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css)!.Value.Utf8.ToArray();
        using var host = RaskTestHost.Create<TestApp>();

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/_rask/a/{hash}.css");
        req.Headers.AcceptEncoding.ParseAdd("br");
        var response = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Accept-Encoding", response.Headers.Vary);

        var raw = await response.Content.ReadAsByteArrayAsync();
        if (response.Content.Headers.ContentEncoding.Contains("br"))
        {
            // Encoding-suffixed ETag so a conditional request matches the exact representation.
            Assert.Equal($"\"{hash}-br\"", response.Headers.ETag?.ToString());
            Assert.True(raw.Length < expected.Length, "brotli should shrink the bundle");
            using var dst = new MemoryStream();
            await using var br = new BrotliStream(new MemoryStream(raw), CompressionMode.Decompress);
            await br.CopyToAsync(dst);
            Assert.Equal(expected, dst.ToArray());
        }
        else
        {
            Assert.Equal(expected, raw); // the test client auto-decompressed
        }
    }

    // ─── Test fixtures ───────────────────────────────────────────────────

    private sealed class WidgetA : Component
    {
        protected override RenderResult Render() => this;
    }
}

[CollectionDefinition("ScopedAssets", DisableParallelization = true)]
public class ScopedAssetsCollectionDef
{
}
