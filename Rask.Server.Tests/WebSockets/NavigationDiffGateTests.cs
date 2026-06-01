using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// Navigation used to unconditionally ship the whole document (full-HTML morph). The diff
// gate now lets a navigation ride the diff path when the rendered <head> is byte-identical
// to the last sent document — diff+history instead of the full doc. Head-changing
// navigations (a per-route <title>) still fall back to full HTML so the title/scoped-asset
// delta reaches the client.
//
// In SessionGracePeriod so the static LiveOptions.DiffMode write serialises with the other
// DiffMode-mutating WS test classes.
[Collection("SessionGracePeriod")]
public class NavigationDiffGateTests
{
    // Forced pins the diff path so the assertions don't depend on payload sizing; the
    // head/structural gates are independent of size.
    public NavigationDiffGateTests() => LiveOptions.DiffMode = LiveDiffMode.Forced;

    [Fact]
    public async Task Navigate_SameHead_ShipsDiffWithHistory()
    {
        await using var fixture = await Connect<NavigateInHandlerStateHasChangedApp>();

        // First nav seeds the render-cache baseline: a fresh attach dedups its catch-up
        // render against the GET-time HTML, so _renderCache._previous is null until a
        // non-deduped render rotates it through. (Same reason the first post-attach
        // interaction always ships full HTML.) This first nav ships full HTML and snapshots.
        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/seed", query = "" });
        _ = await DrainToLastFrame(fixture.Ws);

        // Second nav: head unchanged (static <title>), body diffs → diff + history.
        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/destination", query = "" });

        var frame = await DrainToLastFrame(fixture.Ws);
        Assert.NotNull(frame);
        using var doc = JsonDocument.Parse(frame!);
        Assert.Equal("diff", doc.RootElement.GetProperty("kind").GetString());
        Assert.False(doc.RootElement.TryGetProperty("html", out _),
            $"Same-head navigation must not ship full HTML. Got: {Truncate(frame!)}");
        var history = doc.RootElement.GetProperty("history");
        Assert.Equal("push", history.GetProperty("action").GetString());
        Assert.Equal("/destination", history.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Navigate_QueryOnlyNoBodyChange_ShipsHistoryOnlyDiff()
    {
        await using var fixture = await Connect<NavigateInHandlerStateHasChangedApp>();

        // First nav seeds the render-cache baseline (see note above).
        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/page", query = "" });
        _ = await DrainToLastFrame(fixture.Ws);

        // Re-navigate to the SAME path with a query. The app renders only the path, so the
        // body is unchanged → zero DOM ops. The nav must still ship to pushState the URL,
        // as a history-only diff (empty ops) rather than the whole document.
        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/page", query = "?q=1" });

        var frame = await DrainToLastFrame(fixture.Ws);
        Assert.NotNull(frame);
        using var doc = JsonDocument.Parse(frame!);
        Assert.Equal("diff", doc.RootElement.GetProperty("kind").GetString());
        Assert.False(doc.RootElement.TryGetProperty("html", out _),
            $"Query-only navigation must not ship full HTML. Got: {Truncate(frame!)}");
        Assert.Empty(doc.RootElement.GetProperty("ops").EnumerateArray());
        var history = doc.RootElement.GetProperty("history");
        Assert.Equal("/page?q=1", history.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Navigate_HeadChanges_ShipsDiffWithHeadFragment()
    {
        await using var fixture = await Connect<RouteTitleNavApp>();

        // First nav seeds the render-cache baseline (see Navigate_SameHead note).
        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/seed", query = "" });
        _ = await DrainToLastFrame(fixture.Ws);

        // RouteTitleNavApp changes the <title> AND an H1 text per route. The body delta is a
        // supported UpdateText op, so the nav ships a diff carrying the new <head> as a
        // fragment (client morphs it into document.head) rather than the whole document.
        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/destination", query = "" });

        var frame = await DrainToLastFrame(fixture.Ws);
        Assert.NotNull(frame);
        using var doc = JsonDocument.Parse(frame!);
        Assert.Equal("diff", doc.RootElement.GetProperty("kind").GetString());
        Assert.False(doc.RootElement.TryGetProperty("html", out _),
            $"Head-changing nav with a supported body diff must not ship full HTML. Got: {Truncate(frame!)}");
        Assert.True(doc.RootElement.TryGetProperty("head", out var head), "expected a head fragment");
        Assert.Contains("t-/destination", head.GetString());
        Assert.NotEmpty(doc.RootElement.GetProperty("ops").EnumerateArray());
        Assert.Equal("/destination", doc.RootElement.GetProperty("history").GetProperty("url").GetString());
    }

    [Fact]
    public async Task Navigate_HeadChangesWithStructuralBody_StillShipsFullHtml()
    {
        await using var fixture = await Connect<RouteTitleStructuralNavApp>();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/seed", query = "" });
        _ = await DrainToLastFrame(fixture.Ws);

        // The body restructures per route (div ↔ unkeyed list) → untrusted positional
        // structural ops → DiffOpsAreClientSupported rejects → full HTML. The head fragment
        // is never sent; the full-document morph carries the head delta instead.
        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/destination", query = "" });

        var frame = await DrainToLastFrame(fixture.Ws);
        Assert.NotNull(frame);
        using var doc = JsonDocument.Parse(frame!);
        Assert.True(doc.RootElement.TryGetProperty("html", out var html),
            $"Structural-body nav must ship full HTML. Got: {Truncate(frame!)}");
        Assert.Contains("t-/destination", html.GetString());
        Assert.False(doc.RootElement.TryGetProperty("head", out _), "full-HTML payload carries no head fragment");
        Assert.Equal("/destination", doc.RootElement.GetProperty("history").GetProperty("url").GetString());
    }

    // Drain every frame until the receive window closes; the last frame is the coalesced
    // final send (rebuilds re-thread history, so the last frame is the authoritative one).
    private static async Task<string?> DrainToLastFrame(WebSocket ws)
    {
        string? last = null;
        while (await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(500)) is { } frame)
        {
            last = frame;
        }

        return last;
    }

    private static string Truncate(string s) => s[..Math.Min(300, s.Length)];

    private static async Task<ConnectedSession> Connect<TApp>() where TApp : Rask.Core.Component
    {
        var host = RaskTestHost.Create<TApp>();
        var initial = await host.Http.GetAsync("/start");
        var sessionId = ExtractSessionId(await initial.Content.ReadAsStringAsync());
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        // Drain the post-attach catch-up frame so the nav assertions observe only nav
        // traffic — and so _lastSentHtml is seeded as the diff/head baseline.
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        var session = host.Store.Get(sessionId)!;
        return new ConnectedSession(host, ws, session);
    }

    private static string ExtractSessionId(string html) =>
        Regex.Match(html, "data-rask-root=\"([^\"]+)\"").Groups[1].Value;

    private sealed class ConnectedSession : IAsyncDisposable
    {
        public ConnectedSession(RaskTestHost host, WebSocket ws, LiveSession session)
        {
            Host = host;
            Ws = ws;
            Session = session;
        }

        public RaskTestHost Host { get; }
        public WebSocket Ws { get; }
        public LiveSession Session { get; }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Ws.State == WebSocketState.Open)
                {
                    await Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
            }
            catch
            {
            }

            Ws.Dispose();
            Host.Dispose();
        }
    }
}
