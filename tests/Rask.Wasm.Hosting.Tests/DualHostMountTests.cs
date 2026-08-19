using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;
using Rask.Html.Components;
using Rask.Server;
using Rask.Wasm.Hosting;
using Rask.Wasm.Hosting.Tests.Infrastructure;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

// Deliberately OUTSIDE the Rask.* namespace tree: a consumer app resolves both hosts' extension
// methods at the `using` stage, where neither is nearer. Nesting this under Rask.Wasm.Hosting.Tests
// would let namespace proximity silently pick that host and hide the collision a real app hits.
namespace RaskDualHost.Tests;

/// <summary>
///     One app, both hosts: a WASM SPA served by <c>Rask.Wasm.Hosting</c> at the root, plus a
///     server-rendered Rask route chain (the operator dashboard) mounted under <c>/_rask</c> by
///     <c>Rask.Server</c>. This is the wasm-hosted <c>--ops</c> composition, and it is the only
///     configuration in which the two hosts' overlapping registrations meet.
///     <para>
///         Three things have to hold, and none of them held before: the shared
///         <c>/_rask/a/{hash}.{ext}</c> asset endpoint must be mapped exactly once whichever host
///         goes first, the SPA fallback must keep every route the server chain does not claim, and
///         the server chain must claim its own prefix.
///     </para>
/// </summary>
[Collection("ScopedAssets")]
public class DualHostMountTests
{
    public DualHostMountTests() => ScopedAssetRegistry.InvalidateAll();

    /// <summary>
    ///     Both hosts map <c>/_rask/a/{hash}.css</c>. Two endpoints with an identical route template
    ///     and identical precedence make routing throw <c>AmbiguousMatchException</c> at request time
    ///     — a 500 on every scoped stylesheet, i.e. an unstyled app, and only at runtime.
    /// </summary>
    [Theory]
    [InlineData(true)]  // server mounted first
    [InlineData(false)] // wasm hosting mounted first
    public async Task ScopedAsset_IsServed_WhicheverHostMapsFirst(bool serverFirst)
    {
        ScopedAssetRegistry.RegisterCss(typeof(DualWidget), ".dual { color: rebeccapurple; }");
        ScopedAssetRegistry.TryGetCss(typeof(DualWidget), out var hash);
        using var bundle = new FakeBundleDirectory();
        await using var host = await CreateAsync(bundle.Path, serverFirst);

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        // Compared against the registry rather than the source text: registration rewrites each
        // selector with the component's scope attribute, and the served bytes are what the browser
        // must get. Byte equality also proves the two hosts' handlers are interchangeable.
        var expected = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css)!.Value.Utf8.ToArray();
        Assert.Equal(expected, await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>
    ///     A registry miss must still fall back to the baked file in the published bundle, whichever
    ///     host owns the endpoint. The host process's registry is a strict subset of the in-WASM set,
    ///     so this is the normal path for the SPA's own assets — not an edge case.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ScopedAsset_FallsBackToTheBakedBundleFile_WhicheverHostMapsFirst(bool serverFirst)
    {
        using var bundle = new FakeBundleDirectory();
        var baked = new string('a', ScopedAssetRegistry.HashHexLength);
        var assetDir = Path.Combine(bundle.Path, "_rask", "a");
        Directory.CreateDirectory(assetDir);
        await File.WriteAllTextAsync(Path.Combine(assetDir, baked + ".css"), ".baked { color: teal; }");

        await using var host = await CreateAsync(bundle.Path, serverFirst);

        var response = await host.Http.GetAsync($"/_rask/a/{baked}.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(".baked { color: teal; }", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    ///     The server chain owns its prefix: <c>/_rask</c> and everything under it that isn't a
    ///     framework endpoint renders server-side rather than falling through to the SPA shell.
    /// </summary>
    [Fact]
    public async Task ServerChain_Serves_ItsOwnPrefix()
    {
        using var bundle = new FakeBundleDirectory();
        await using var host = await CreateAsync(bundle.Path, serverFirst: true);

        var response = await host.Http.GetAsync("/_rask");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("dual-host-marker", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Everything outside the server chain's prefix still reaches the SPA shell — mounting the
    ///     dashboard must not swallow the application's own client-side routes.
    /// </summary>
    [Fact]
    public async Task SpaFallback_Still_Serves_ClientRoutes()
    {
        using var bundle = new FakeBundleDirectory();
        await using var host = await CreateAsync(bundle.Path, serverFirst: true);

        var response = await host.Http.GetAsync("/some/client/route");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-rask-root", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // Both hosts in one app, in either order. `serverFirst` is the order the scaffolded Program.cs
    // uses; the reverse is what a user who reorders the two lines gets, and it has to behave the same.
    private static async Task<DualHost> CreateAsync(string bundlePath, bool serverFirst)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        // Exactly what the scaffolded wasm-hosted Program.cs writes. A bare AddRask()/UseRask<T>()
        // resolves against both packages' extension methods here, and AddRask() binds to the WASM
        // host's silently — it takes no optional parameters, so it wins the tie-break with no
        // ambiguity error. Each call names its host instead.
        builder.Services.AddRaskServer();
        builder.Services.AddRaskWasmHost();

        var app = builder.Build();
        app.UseRouting();

        if (serverFirst)
        {
            app.UseRaskServer<DualApp>("/_rask/{**path}");
            app.UseRaskWasmHost(bundlePath);
        }
        else
        {
            app.UseRaskWasmHost(bundlePath);
            app.UseRaskServer<DualApp>("/_rask/{**path}");
        }

        await app.StartAsync();
        return new DualHost(app);
    }

    private sealed class DualHost(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Http { get; } = app.GetTestServer().CreateClient();

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            LiveOptions.PathBase = string.Empty;
            ScopedAssetBundle.BakedDirectory = null;
        }
    }

    private sealed class DualWidget : Component
    {
        protected override Component? Render() => Div["widget"];
    }

    private sealed class DualApp : Component
    {
        protected override Component? Render() => Div.Id("dual-host-marker")["dashboard root"];
    }
}
