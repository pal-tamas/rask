using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wasm.Hosting.Tests.Infrastructure;

namespace Rask.Wasm.Hosting.Tests;

public class UseRaskTests
{
    [Fact]
    public async Task BundleDirDoesNotExist_FallbackReturns503_WithBundleNotFoundMessage()
    {
        await using var host =
            await WasmHostingTestServer.CreateAsync("/tmp/rask-wasm-hosting-tests-definitely-not-here");

        var response = await host.Http.GetAsync("/anything");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("bundle not found at", body);
    }

    [Fact]
    public async Task WasmFile_ServedAsApplicationWasm()
    {
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync("/_framework/foo.wasm");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/wasm", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task JsFile_ServedAsApplicationJavascript()
    {
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync("/js/app.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnknownExtension_ServedAsOctetStream_NotFallback()
    {
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync("/unknown.bin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("opaque", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StaticResponses_HaveCacheControlNoCache()
    {
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        foreach (var path in new[] { "/_framework/foo.wasm", "/js/app.js", "/unknown.bin" })
        {
            var response = await host.Http.GetAsync(path);
            Assert.True(response.Headers.CacheControl?.NoCache == true,
                $"{path} missing Cache-Control: no-cache");
        }
    }

    [Fact]
    public async Task UnknownSpaPath_FallsBackToIndexHtml_WithTextHtmlContentType()
    {
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync("/some/spa/route/that-does-not-exist");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-rask-root", body);
        Assert.Contains("fake", body);
    }

    [Fact]
    public async Task FallbackResponse_HasCacheControlNoCache()
    {
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync("/some/spa/route");

        Assert.True(response.Headers.CacheControl?.NoCache == true);
    }

    [Fact]
    public async Task SecondRequest_WithIfNoneMatch_Returns304()
    {
        using var bundle = new FakeBundleDirectory();
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path);

        var first = await host.Http.GetAsync("/_framework/foo.wasm");
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);

        var second = new HttpRequestMessage(HttpMethod.Get, "/_framework/foo.wasm");
        second.Headers.IfNoneMatch.Add(etag!);
        var secondResponse = await host.Http.SendAsync(second);

        Assert.Equal(HttpStatusCode.NotModified, secondResponse.StatusCode);
    }

    [Fact]
    public async Task AddRaskRegistered_BrotliRequestedForWasm_ResponseUsesBrotli()
    {
        // Pad the .wasm to 8 KiB so the body crosses ResponseCompression's minimum-size
        // threshold (otherwise small payloads are passed through unchanged).
        using var bundle = new FakeBundleDirectory(wasmPaddingBytes: 8 * 1024);
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path, withCompression: true);

        var req = new HttpRequestMessage(HttpMethod.Get, "/_framework/foo.wasm");
        req.Headers.AcceptEncoding.ParseAdd("br");
        var response = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("br", response.Content.Headers.ContentEncoding);
        Assert.Equal("application/wasm", response.Content.Headers.ContentType?.MediaType);
        // Highly-compressible repeating bytes — output should be a tiny fraction of the input.
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length < 1024,
            $"expected brotli output to be << raw 8 KiB, was {bytes.Length} bytes");
    }

    [Fact]
    public async Task AddRaskRegistered_NoAcceptEncoding_NoCompression()
    {
        using var bundle = new FakeBundleDirectory(wasmPaddingBytes: 8 * 1024);
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path, withCompression: true);

        var response = await host.Http.GetAsync("/_framework/foo.wasm");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task AddRaskNotRegistered_BrotliRequested_NoCompression()
    {
        using var bundle = new FakeBundleDirectory(wasmPaddingBytes: 8 * 1024);
        await using var host = await WasmHostingTestServer.CreateAsync(bundle.Path, withCompression: false);

        var req = new HttpRequestMessage(HttpMethod.Get, "/_framework/foo.wasm");
        req.Headers.AcceptEncoding.ParseAdd("br");
        var response = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public void UseRask_OnEndpointBuilderWithoutApplicationBuilder_Throws()
    {
        using var bundle = new FakeBundleDirectory();
        var stub = new FakeEndpointRouteBuilder();

        var ex = Assert.Throws<InvalidOperationException>(() => stub.UseRask(bundle.Path));
        Assert.Contains("IApplicationBuilder", ex.Message);
    }

    private sealed class FakeEndpointRouteBuilder : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } =
            new ServiceCollection().BuildServiceProvider();

        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();

        public IApplicationBuilder CreateApplicationBuilder() =>
            throw new NotSupportedException("test stub does not build apps");
    }
}
