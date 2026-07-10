using System.Text;
using System.Text.Json;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// Navigation used to unconditionally ship the whole document (full-HTML morph). The
// diff gate now lets a navigation ride the diff path when the rendered <head> is
// byte-identical to the last applied document — diff+history instead of the full doc.
// Head-changing navigations (a per-route <title>) still fall back to full HTML so the
// title/scoped-asset delta reaches the client.
[Collection("WasmSession")]
// Forced removes payload-size comparison from the assertions; the head/structural gates are
// independent of sizing. The WasmSession collection serialises these tests so the static-field
// write is safe.
public class NavigationDiffGateTests() : ResettingTestBase(LiveDiffMode.Forced)
{
    [Fact]
    public async Task Navigate_SameHead_ShipsDiffWithHistory()
    {
        var (session, _) = NewSession(diffMode: DiffMode);
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
        var (session, _) = NewSession(diffMode: DiffMode);
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
    public async Task Navigate_QueryOnlyNoBodyChange_ShipsHistoryOnlyDiff()
    {
        var (session, _) = NewSession(diffMode: DiffMode);
        await session.InitialRenderAsync();

        // StubApp renders only the path, never the query — navigating to the same path
        // with a query produces zero DOM ops. The nav must still ship (to pushState the
        // URL), and it does so as a history-only diff (empty ops) rather than the whole
        // document.
        var result = await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/","query":"?q=1"}"""));

        var text = Encoding.UTF8.GetString(result);
        using var doc = JsonDocument.Parse(result.AsMemory());
        Assert.Equal("diff", doc.RootElement.GetProperty("kind").GetString());
        Assert.False(doc.RootElement.TryGetProperty("html", out _),
            $"Query-only navigation must not ship full HTML. Got: {text[..Math.Min(300, text.Length)]}");
        Assert.Empty(doc.RootElement.GetProperty("ops").EnumerateArray());
        var history = doc.RootElement.GetProperty("history");
        Assert.Equal("/?q=1", history.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Navigate_HeadChanges_ShipsDiffWithHeadFragment()
    {
        var (session, _) = NewSession<RouteTitleStubApp>(diffMode: DiffMode);
        await session.InitialRenderAsync();

        // RouteTitleStubApp changes the <title> AND an H1 text per route. The body delta is a
        // supported UpdateText op, so the nav ships a diff that carries the new <head> as a
        // fragment (the client morphs it into document.head) rather than the whole document.
        var result = await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/destination","query":""}"""));

        var text = Encoding.UTF8.GetString(result);
        using var doc = JsonDocument.Parse(result.AsMemory());
        Assert.Equal("diff", doc.RootElement.GetProperty("kind").GetString());
        Assert.False(doc.RootElement.TryGetProperty("html", out _),
            $"Head-changing nav with a supported body diff must not ship full HTML. Got: {text[..Math.Min(300, text.Length)]}");
        Assert.True(doc.RootElement.TryGetProperty("head", out var head), "expected a head fragment");
        Assert.Contains("title-/destination", head.GetString());
        Assert.NotEmpty(doc.RootElement.GetProperty("ops").EnumerateArray());
        var history = doc.RootElement.GetProperty("history");
        Assert.Equal("/destination", history.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Navigate_HeadChangesWithStructuralBody_StillShipsFullHtml()
    {
        var (session, _) = NewSession<RouteTitleStructuralStubApp>(diffMode: DiffMode);
        await session.InitialRenderAsync();

        // The body restructures per route (div ↔ unkeyed list) → untrusted positional
        // structural ops → DiffOpsAreClientSupported rejects → full HTML. The head fragment
        // is never sent; the full-document morph carries the head delta instead.
        var result = await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/destination","query":""}"""));

        var text = Encoding.UTF8.GetString(result);
        using var doc = JsonDocument.Parse(result.AsMemory());
        Assert.True(doc.RootElement.TryGetProperty("html", out var html),
            $"Structural-body nav must ship full HTML. Got: {text[..Math.Min(300, text.Length)]}");
        Assert.Contains("title-/destination", html.GetString());
        Assert.False(doc.RootElement.TryGetProperty("head", out _), "full-HTML payload carries no head fragment");
        Assert.Equal("/destination", doc.RootElement.GetProperty("history").GetProperty("url").GetString());
    }

    [Fact]
    public async Task ReactiveTitleChange_NoNavigation_ShipsDiffWithHeadAndNoHistory()
    {
        var (session, _) = NewSession<ReactiveTitleStubApp>(diffMode: DiffMode);
        var initial = await session.InitialRenderAsync();

        // A handler bumps a counter that drives BOTH the <title> and an H1 — no navigation.
        // The head fragment must ride the diff (previously a body-only diff froze the head),
        // and there is no history because nothing navigated.
        var handlerId = Markup.FirstHandlerId(initial);
        var result = await session.DispatchAsync(Utf8($$"""{"id":"{{handlerId}}","type":"click"}"""));

        using var doc = JsonDocument.Parse(result.AsMemory());
        Assert.Equal("diff", doc.RootElement.GetProperty("kind").GetString());
        Assert.True(doc.RootElement.TryGetProperty("head", out var head),
            "reactive title change must ship a head fragment");
        Assert.Contains("count-1", head.GetString());
        Assert.NotEmpty(doc.RootElement.GetProperty("ops").EnumerateArray());
        Assert.False(doc.RootElement.TryGetProperty("history", out _), "no navigation → no history");
    }
}
