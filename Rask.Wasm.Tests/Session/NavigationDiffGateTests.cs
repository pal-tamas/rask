using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Core.ScopedAssets;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// Navigation used to unconditionally ship the whole document (full-HTML morph). The
// diff gate now lets a navigation ride the diff path when the rendered <head> is
// byte-identical to the last applied document — diff+history instead of the full doc.
// Head-changing navigations (a per-route <title>) still fall back to full HTML so the
// title/scoped-asset delta reaches the client.
[Collection("WasmSession")]
public class NavigationDiffGateTests
{
    public NavigationDiffGateTests()
    {
        ScopedAssetRegistry.InvalidateAll();
        // Forced removes payload-size comparison from the assertions; the head/structural
        // gates are independent of sizing. The WasmSession collection serialises these
        // tests so the static-field write is safe.
        LiveOptions.DiffMode = LiveDiffMode.Forced;
    }

    [Fact]
    public async Task Navigate_SameHead_ShipsDiffWithHistory()
    {
        var (session, _) = NewSession();
        await session.InitialRenderAsync();

        var result = await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/destination","query":""}"""));

        var text = Encoding.UTF8.GetString(result);
        using var doc = JsonDocument.Parse(result.AsMemory());
        Assert.Equal("diff", doc.RootElement.GetProperty("kind").GetString());
        Assert.False(doc.RootElement.TryGetProperty("html", out _),
            $"Same-head navigation must not ship full HTML. Got: {text[..Math.Min(300, text.Length)]}");
        var history = doc.RootElement.GetProperty("history");
        Assert.Equal("push", history.GetProperty("action").GetString());
        Assert.Equal("/destination", history.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Navigate_WithQuery_ShipsDiffCarryingQueryInHistory()
    {
        var (session, _) = NewSession();
        await session.InitialRenderAsync();

        // Path changes (body diff) and the head is unchanged → diff path. The history URL
        // must still carry the query string the navigation requested.
        var result = await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/search","query":"?q=hi"}"""));

        using var doc = JsonDocument.Parse(result.AsMemory());
        Assert.Equal("diff", doc.RootElement.GetProperty("kind").GetString());
        var history = doc.RootElement.GetProperty("history");
        Assert.Equal("/search?q=hi", history.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Navigate_HeadChanges_ShipsFullHtmlWithHistory()
    {
        var (session, _) = NewSession<RouteTitleStubApp>();
        await session.InitialRenderAsync();

        var result = await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/destination","query":""}"""));

        var text = Encoding.UTF8.GetString(result);
        using var doc = JsonDocument.Parse(result.AsMemory());
        Assert.True(doc.RootElement.TryGetProperty("html", out var html),
            $"Head-changing navigation must ship full HTML. Got: {text[..Math.Min(300, text.Length)]}");
        Assert.Contains("title-/destination", html.GetString());
        var history = doc.RootElement.GetProperty("history");
        Assert.Equal("/destination", history.GetProperty("url").GetString());
    }

    private static (WasmLiveSession session, IServiceProvider services) NewSession<TApp>()
        where TApp : Component
    {
        var services = new ServiceCollection();
        services.AddSingleton<RouteState>();
        services.AddSingleton<Navigator>();
        var provider = services.BuildServiceProvider();
        var app = ActivatorUtilities.CreateInstance<TApp>(provider);
        var session = new WasmLiveSession(app, provider);
        JSInterop.Init(session);
        return (session, provider);
    }

    private static (WasmLiveSession session, IServiceProvider services) NewSession() => NewSession<StubApp>();

    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);
}
