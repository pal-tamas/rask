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
    public async Task A_known_CSS_hash_answers_200_with_an_immutable_cache_and_an_etag()
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
    public async Task A_known_JS_hash_answers_200_with_a_javascript_content_type()
    {
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f(){}");
        ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_source_map_for_a_debug_bundle_is_the_index_map_the_bundle_names()
    {
        // #1073: the bundle's last line names {hash}.js.map, resolved against the script's own URL.
        const string map = """{"version":3,"sources":["Features/A.ts"],"names":[],"mappings":"AAAA"}""";
        ScopedAssetRegistry.RegisterJs(
            typeof(WidgetA),
            "export function f(){}\n//# sourceMappingURL=data:application/json;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(map)));
        var hash = ScopedAssetRegistry.GetBundleHash(AssetKind.Js);
        using var host = RaskTestHost.Create<TestApp>();

        var bundle = await host.Http.GetStringAsync($"/_rask/a/{hash}.js");
        var response = await host.Http.GetAsync($"/_rask/a/{hash}.js.map");

        Assert.EndsWith($"//# sourceMappingURL={hash}.js.map\n", bundle, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"sections\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_source_map_for_a_release_bundle_or_an_unknown_hash_is_404()
    {
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f(){}");
        var hash = ScopedAssetRegistry.GetBundleHash(AssetKind.Js);
        using var host = RaskTestHost.Create<TestApp>();

        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.GetAsync($"/_rask/a/{hash}.js.map")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.GetAsync("/_rask/a/000000000000.js.map")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.GetAsync("/_rask/a/not-a-hash.js.map")).StatusCode);
    }

    [Fact]
    public async Task The_CSS_body_is_byte_equal_to_what_the_registry_stores()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        var registryBytes = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css)!.Value.Utf8.ToArray();
        using var host = RaskTestHost.Create<TestApp>();

        var bodyBytes = await host.Http.GetByteArrayAsync($"/_rask/a/{hash}.css");

        Assert.Equal(registryBytes, bodyBytes);
    }

    // ─── HTTP method semantics ────────────────────────────────────────────

    [Fact]
    public async Task A_HEAD_for_a_known_hash_answers_200_with_the_same_headers_and_no_body()
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
    public async Task A_POST_to_the_asset_endpoint_answers_405()
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
    public async Task A_PUT_to_the_asset_endpoint_answers_405()
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
    public async Task A_DELETE_to_the_asset_endpoint_answers_405()
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
    public async Task An_exact_If_None_Match_answers_304_with_no_body()
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
    public async Task A_stale_If_None_Match_answers_200_with_the_body()
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
    public async Task A_range_of_the_first_hundred_bytes_answers_206_partial_content()
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
    public async Task A_range_beyond_the_length_answers_416()
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
    public async Task An_unknown_hash_answers_404()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/_rask/a/abcdef012345.css");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_CSS_hash_requested_as_JS_answers_404()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var cssHash);
        using var host = RaskTestHost.Create<TestApp>();

        // The hash is real for CSS but unknown to JS bucket.
        var response = await host.Http.GetAsync($"/_rask/a/{cssHash}.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_upper_case_hash_answers_404()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        // Same hash but uppercase — rejected by IsLowercaseHex check.
        var response = await host.Http.GetAsync($"/_rask/a/{hash.ToUpperInvariant()}.css");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_hash_with_non_hex_characters_answers_404()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/_rask/a/notthexnoth.css");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_hash_too_short_answers_404()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/_rask/a/abc.css");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_hash_too_long_answers_404()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/_rask/a/abcdef0123456789.css");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_extension_does_not_serve_asset_content()
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
    public async Task A_path_traversal_attempt_does_not_serve_a_registered_asset()
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
    public async Task A_hundred_concurrent_gets_all_receive_identical_bytes()
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
    public async Task An_asset_above_one_megabyte_is_served()
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
    public async Task Non_ASCII_content_is_served_byte_for_byte()
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
    public async Task A_client_accepting_brotli_gets_valid_brotli_that_decodes_to_the_asset()
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
        protected override Component? Render() => this;
    }
}

[CollectionDefinition("ScopedAssets", DisableParallelization = true)]
public class ScopedAssetsCollectionDef
{
}
