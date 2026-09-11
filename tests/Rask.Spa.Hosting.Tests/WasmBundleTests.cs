using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Rask.Spa.Hosting.Tests.Infrastructure;

namespace Rask.Spa.Hosting.Tests;

/// <summary>
///     <c>UseRaskSpa</c> serving a published Rask WebAssembly app — the job <c>Rask.Wasm.Hosting</c> used
///     to do with a host of its own.
/// </summary>
public class WasmBundleTests
{
    [Fact]
    public void A_wasm_bundle_is_recognised_from_its_files_and_a_bundler_output_is_not()
    {
        using var wasm = new FakeWasmBundleDirectory();
        using var dist = new FakeDistDirectory();

        Assert.True(SpaAppBundle.IsRaskWasm(new PhysicalFileProvider(wasm.Path)));
        Assert.False(SpaAppBundle.IsRaskWasm(new PhysicalFileProvider(dist.Path)));
    }

    [Theory]
    [InlineData("/_framework/foo.wasm", "application/wasm")]
    [InlineData("/_framework/dotnet.js", "text/javascript")]
    // Registered by name rather than by opening the directory to every extension: without it the
    // runtime's ICU data 404s and every culture-aware call fails in the browser.
    [InlineData("/_framework/icudt.dat", "application/octet-stream")]
    [InlineData("/_rask/a/" + FakeWasmBundleDirectory.ScopedCss, "text/css")]
    public async Task Runtime_files_are_served_with_their_types(string path, string mediaType)
    {
        using var bundle = new FakeWasmBundleDirectory();
        await using var host = await SpaTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    // Fingerprinted by the SDK, and scoped assets named by their content hash: the URL changes when
    // the bytes do.
    [InlineData("/_framework/dotnet.native.7a8b9c2d3e4f.wasm", true)]
    [InlineData("/_rask/a/" + FakeWasmBundleDirectory.ScopedCss, true)]
    // The SDK's default output keeps one name across deploys, so it has to revalidate.
    [InlineData("/_framework/foo.wasm", false)]
    [InlineData("/_framework/dotnet.js", false)]
    [InlineData("/rask.wasm.js", false)]
    // A hand-written assets folder is not Vite's hashed directory, whatever it is called.
    [InlineData("/assets/logo.svg", false)]
    [InlineData("/index.html", false)]
    public async Task Only_what_the_publish_hashed_is_cached_for_ever(string path, bool immutable)
    {
        using var bundle = new FakeWasmBundleDirectory();
        await using var host = await SpaTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cacheControl = response.Headers.CacheControl?.ToString() ?? string.Empty;
        Assert.Equal(immutable, cacheControl.Contains("immutable", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/_framework/missing.wasm")]
    [InlineData("/_rask/a/ffffffffffff.css")]
    public async Task A_missing_runtime_file_is_a_404_even_when_it_asks_for_html(string path)
    {
        using var bundle = new FakeWasmBundleDirectory();
        await using var host = await SpaTestServer.CreateAsync(bundle.Path);

        // Answered with the index document, a missing runtime file surfaces in the browser as a broken
        // WebAssembly module — nowhere near the file that is actually missing.
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        var response = await host.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_client_side_route_gets_the_index_document()
    {
        using var bundle = new FakeWasmBundleDirectory();
        await using var host = await SpaTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync("/orders/42");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
        Assert.Contains("data-rask-root", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_precompressed_runtime_file_keeps_its_wasm_type()
    {
        using var bundle = new FakeWasmBundleDirectory();
        await using var host = await SpaTestServer.CreateAsync(bundle.Path);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/_framework/compressed.wasm");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        var response = await host.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("br", response.Content.Headers.ContentEncoding);
        // WebAssembly.instantiateStreaming refuses anything not served as application/wasm.
        Assert.Equal("application/wasm", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(FakeWasmBundleDirectory.BrotliSibling, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Response_compression_covers_the_runtime()
    {
        // Padded past ResponseCompression's minimum body size, and highly compressible.
        using var bundle = new FakeWasmBundleDirectory(wasmPaddingBytes: 8 * 1024);
        await using var host = await SpaTestServer.CreateAsync(bundle.Path, withCompression: true);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/_framework/foo.wasm");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        var response = await host.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("br", response.Content.Headers.ContentEncoding);
        Assert.True((await response.Content.ReadAsByteArrayAsync()).Length < 1024);
    }

    [Fact]
    public async Task A_prefix_scopes_the_runtime_files_too()
    {
        using var bundle = new FakeWasmBundleDirectory();
        await using var host = await SpaTestServer.CreateAsync(bundle.Path, pathBase: "/sub");

        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/sub/_framework/foo.wasm")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.GetAsync("/_framework/foo.wasm")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await host.Http.GetAsync("/sub/_framework/missing.wasm")).StatusCode);
    }

    [Fact]
    public async Task The_scoped_assets_join_the_web_root_and_nothing_else_does()
    {
        using var bundle = new FakeWasmBundleDirectory();
        await using var host = await SpaTestServer.CreateAsync(bundle.Path);

        // Rask.Server's /_rask/a/{hash} endpoint answers a hash its registry lacks from the web root,
        // and in an app mounting the operator dashboard it is that endpoint, not the static files, that
        // routing picks for the bundle's scoped assets.
        var webRoot = host.Services.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider;

        Assert.True(webRoot.GetFileInfo("_rask/a/" + FakeWasmBundleDirectory.ScopedCss).Exists);

        // Only that subtree: the rest of the bundle stays behind the prefix it was mounted under.
        Assert.False(webRoot.GetFileInfo("_framework/foo.wasm").Exists);
        Assert.False(webRoot.GetFileInfo("index.html").Exists);
    }

    [Fact]
    public async Task A_bundler_output_leaves_the_web_root_alone()
    {
        using var dist = new FakeDistDirectory();
        await using var host = await SpaTestServer.CreateAsync(dist.Path);

        var webRoot = host.Services.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider;

        Assert.IsNotType<CompositeFileProvider>(webRoot);
    }
}
