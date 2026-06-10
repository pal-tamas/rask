using System.Net;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Rask.Example.Wasm.Host.Tests.Infrastructure;
using Rask.Wasm.Hosting;

namespace Rask.Example.Wasm.Host.Tests.Hosting;

public sealed class ProgramTests
{
    [Fact]
    public void AddRask_RegistersResponseCompressionService()
    {
        var sc = new ServiceCollection();
        sc.AddRask();
        // Compression's provider depends on ILogger / options at instantiation time,
        // which we don't want to wire here. Verify by checking the registration
        // descriptors directly — that's what AddRask is meant to install.
        Assert.Contains(sc, s => s.ServiceType == typeof(IResponseCompressionProvider));
    }

    [Fact]
    public async Task RootGet_ServesIndexHtml_FromBundle()
    {
        using var bundle = new FakeBundle();
        await using var host = await ExampleHostTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-rask-root", body);
        Assert.Contains("fake bundle", body);
    }

    [Fact]
    public async Task UnknownPath_FallsBackToIndexHtml_ForSpaRouting()
    {
        using var bundle = new FakeBundle();
        await using var host = await ExampleHostTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync("/some/spa/route");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-rask-root", body);
    }

    [Fact]
    public async Task WasmAsset_ServedWithApplicationWasmContentType()
    {
        using var bundle = new FakeBundle();
        await using var host = await ExampleHostTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync("/_framework/dotnet.wasm");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/wasm", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task MissingBundle_ReturnsServiceUnavailable_WithHelpfulMessage()
    {
        await using var host = await ExampleHostTestServer.CreateAsync(
            "/tmp/rask-example-wasm-host-tests-does-not-exist");

        var response = await host.Http.GetAsync("/");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("bundle not found", body);
    }
}
