using System.IO.Compression;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

// The page document is the largest and most compressible thing this host serves — rask.sh's landing page
// is 78,525 bytes raw against 15,540 gzipped — and it used to go out raw while only the scoped CSS/TS
// bundles were compressed. The page handler now compresses the document itself — scoped to the page, so
// neither the app's own endpoints nor its own ResponseCompressionOptions are touched.
public class PageCompressionTests
{
    [Theory]
    [InlineData("br")]
    [InlineData("gzip")]
    public async Task A_page_is_served_compressed_when_the_client_accepts_it(string encoding)
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await GetAsync(host, encoding);

        Assert.Equal([encoding], response.Content.Headers.ContentEncoding);
        // Compressed, and still the page: the rendered body decodes intact.
        Assert.Contains("path=/", await DecodeAsync(response, encoding), StringComparison.Ordinal);
        // A shared cache must never hand a brotli body to a client that did not ask for one.
        Assert.Contains("Accept-Encoding", response.Headers.Vary);
    }

    [Fact]
    public async Task Brotli_is_preferred_when_the_client_accepts_both()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await GetAsync(host, "gzip", "br");

        Assert.Equal(["br"], response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task A_page_is_served_raw_when_the_client_accepts_no_encoding()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/");

        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.Contains("path=/", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turning_CompressPageHtml_off_serves_the_page_raw()
    {
        // The opt-out for an app whose pages render a long-lived secret next to attacker-influenced input
        // (BREACH). Every encoding offered, none taken.
        using var host = RaskTestHost.Create<TestApp>(configureServer: o => o.CompressPageHtml = false);

        var response = await GetAsync(host, "br", "gzip");

        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.Contains("path=/", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_compressed_page_keeps_its_no_store_policy()
    {
        // The shell embeds the session id, so it is never cacheable. Compression rewrites headers on the
        // way out (Vary, Content-Encoding, drops Content-Length); it must not disturb that one.
        using var host = RaskTestHost.Create<TestApp>();

        var response = await GetAsync(host, "br");

        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("br")]
    [InlineData("gzip")]
    public async Task An_app_that_already_compresses_is_not_compressed_twice(string encoding)
    {
        // The upgrade path: an app that already runs UseResponseCompression. Its middleware wraps the body
        // the handler now writes brotli into; seeing Content-Encoding already set, it must pass the bytes
        // through — a doubly-encoded body decodes to gibberish and nothing would say why.
        using var host = RaskTestHost.Create<TestApp>(
            configureServices: services => services.AddResponseCompression(o =>
            {
                o.Providers.Add<GzipCompressionProvider>();
                o.Providers.Add<BrotliCompressionProvider>();
            }),
            configureMiddleware: app => app.UseResponseCompression());

        var response = await GetAsync(host, encoding);

        Assert.Equal([encoding], response.Content.Headers.ContentEncoding);
        Assert.Contains("path=/", await DecodeAsync(response, encoding), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_apps_own_endpoints_are_not_compressed_by_Rask()
    {
        // The option names the page, and only the page is analysed for BREACH. An app's JSON API returning
        // a token beside an echoed query value is exactly that attack's shape, so Rask compressing it too
        // — which app-wide UseResponseCompression middleware would — is not a side effect to ship.
        using var host = RaskTestHost.Create<TestApp>(
            configureMiddleware: app => ((IEndpointRouteBuilder)app).MapGet(
                "/api/echo", (string q) => Results.Json(new { token = "s3cr3t", q })));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/echo?q=abc");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        var response = await host.Http.SendAsync(request);

        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.Contains("s3cr3t", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void AddRask_leaves_the_apps_compression_options_alone()
    {
        // Registering AddResponseCompression would configure the app-GLOBAL ResponseCompressionOptions —
        // EnableForHttps on for an app's own middleware, providers and levels overwritten. AddRask must
        // register none of it.
        using var host = RaskTestHost.Create<TestApp>();

        Assert.Null(host.Services.GetService<IResponseCompressionProvider>());
    }

    [Theory]
    [InlineData("br;q=0, gzip", "gzip")] // an explicit refusal is not a preference
    [InlineData("gzip;q=1, br;q=0.5", "gzip")] // the client's ranking wins
    [InlineData("gzip;q=0.5, br;q=0.5", "br")] // a tie goes to the smaller encoding
    [InlineData("gzip;q=0, br;q=0", null)]
    [InlineData("deflate, identity", null)]
    public async Task Quality_values_are_honoured(string acceptEncoding, string? expected)
    {
        using var host = RaskTestHost.Create<TestApp>();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
        var response = await host.Http.SendAsync(request);

        Assert.Equal(expected is null ? Array.Empty<string>() : new[] { expected }, response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task An_uncompressed_answer_still_varies_on_Accept_Encoding()
    {
        // The raw page was chosen because of the header too, so a cache has to key on it either way — the
        // variant it stores first decides what the next client gets otherwise.
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/");

        Assert.Contains("Accept-Encoding", response.Headers.Vary);
    }

    [Fact]
    public void CompressPageHtml_is_on_by_default() => Assert.True(new RaskServerOptions().CompressPageHtml);

    private static Task<HttpResponseMessage> GetAsync(RaskTestHost host, params string[] encodings)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        foreach (var encoding in encodings)
        {
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue(encoding));
        }

        return host.Http.SendAsync(request);
    }

    private static async Task<string> DecodeAsync(HttpResponseMessage response, string encoding)
    {
        await using var body = await response.Content.ReadAsStreamAsync();
        await using Stream decoded = encoding == "br"
            ? new BrotliStream(body, CompressionMode.Decompress)
            : new GZipStream(body, CompressionMode.Decompress);
        using var reader = new StreamReader(decoded);
        return await reader.ReadToEndAsync();
    }
}
