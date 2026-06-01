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
    public async Task Navigate_HeadChanges_ShipsFullHtmlWithHistory()
    {
        await using var fixture = await Connect<RouteTitleNavApp>();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/destination", query = "" });

        var frame = await DrainToLastFrame(fixture.Ws);
        Assert.NotNull(frame);
        using var doc = JsonDocument.Parse(frame!);
        Assert.True(doc.RootElement.TryGetProperty("html", out var html),
            $"Head-changing navigation must ship full HTML. Got: {Truncate(frame!)}");
        Assert.Contains("t-/destination", html.GetString());
        var history = doc.RootElement.GetProperty("history");
        Assert.Equal("/destination", history.GetProperty("url").GetString());
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
