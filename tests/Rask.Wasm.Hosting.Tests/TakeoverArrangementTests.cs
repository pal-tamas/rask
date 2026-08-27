using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Html.Components;
using Rask.Server;
using Rask.Wasm.Hosting;
using Rask.Wasm.Hosting.Tests.Infrastructure;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories
#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

// Deliberately outside the Rask.* namespace tree, for the same reason DualHostMountTests is: a
// consumer app resolves both hosts' extension methods at the `using` stage, and nesting under one
// host's namespace would let proximity hide a collision a real app hits.
namespace RaskTakeover.Tests;

/// <summary>
///     The arrangement a browser takeover needs: <c>Rask.Server</c> renders every page live, and the
///     WASM bundle sits alongside as assets only.
/// </summary>
/// <remarks>
///     This is the inverse of the wasm-hosted composition. There the SPA owns the root and the server
///     chain is mounted under a prefix; here the server owns everything and the bundle is reachable
///     but routes nothing, so a page can boot the runtime when it decides to rather than being
///     replaced by <c>index.html</c> before it ever renders.
/// </remarks>
public class TakeoverArrangementTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/some/page")]
    public async Task TheServerStillRendersItsPages(string path)
    {
        // The whole point. Calling the SPA form here would shadow every server-rendered page with
        // index.html, and the visitor would never see server-rendered HTML at all.
        //
        // "/" is here because leaving it out is what let a real bug through: this asserted only a deep
        // path, which has no index.html to be rewritten to, so it passed while the home page — the one
        // every visitor lands on first — served the bundle's SPA shell instead of the app. A static
        // pipeline shadows the root by a different mechanism (UseDefaultFiles) than it shadows
        // everything else (MapFallback), so testing one proves nothing about the other.
        using var bundle = new FakeBundleDirectory();
        await using var host = await CreateAsync(bundle.Path);

        var body = await host.Http.GetStringAsync(path);

        Assert.Contains("server-rendered", body);
        Assert.DoesNotContain("fake", body);
    }

    [Fact]
    public async Task TheBundleAssetsAreStillReachable()
    {
        // And the other half: the runtime has to be fetchable, or there is nothing for the page to
        // boot when the handover comes.
        using var bundle = new FakeBundleDirectory();
        await using var host = await CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync("/_framework/foo.wasm");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/wasm", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AssetsAreServedBeforeRoutingSelectsThePageEndpoint()
    {
        // The load-bearing ordering, pinned because getting it wrong fails confusingly rather than
        // loudly. UseRouting selects an endpoint before UseStaticFiles runs, and the static-file
        // middleware steps aside when one is already selected — so mapping the bundle AFTER
        // UseRouting makes the server's catch-all answer /_framework/*.wasm with text/html, and the
        // browser reports a broken WASM module rather than a routing mistake.
        using var bundle = new FakeBundleDirectory();
        await using var host = await CreateAsync(bundle.Path);

        var asset = await host.Http.GetAsync("/_framework/foo.wasm");
        var page = await host.Http.GetStringAsync("/some/page");

        Assert.Equal("application/wasm", asset.Content.Headers.ContentType?.MediaType);
        Assert.Contains("server-rendered", page);
    }

    private static async Task<TakeoverHost> CreateAsync(string bundlePath)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRaskServer();
        builder.Services.AddRaskWasmHost();

        var app = builder.Build();

        // Assets BEFORE UseRouting, deliberately — see the ordering test above.
        app.UseRaskWasmAssets(bundlePath);

        app.UseRouting();
        app.UseRaskServer<TakeoverApp>();

        await app.StartAsync();
        return new TakeoverHost(app);
    }

    // Nested and chain-form, matching DualHostMountTests: the bare Div/Title are the generated chain
    // entries, which is how every other component in this suite is written.
    private sealed class TakeoverApp : Component
    {
        protected override Component? Render() => Div["server-rendered"];
    }

    private sealed class TakeoverHost(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Http { get; } = app.GetTestServer().CreateClient();

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await app.DisposeAsync();
        }
    }
}
