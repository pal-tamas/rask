using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// Regression: navigating away from a page on the Server example produced two
// outbound WS payloads per dispatch — an intermediate one fired by RouteState
// .Changed's StateHasChanged subscriber on the layout, plus the final one from
// EnforceAuthAndRenderAsync carrying history.url. Each payload morphs <head>
// on the client; LiveTicker's chart.js head asset removal forces the keyed
// morph to move the scoped-CSS link past it, which under Chromium briefly
// invalidates the cascade. With two morphs in rapid succession the .nav-item
// -btn rules disappear long enough for the sidebar to render with default
// browser button styling — visible as a one-frame gray-box flash.
//
// Fix: mirror WasmLiveSession's coalescing path. In-handler StateHasChanged
// just flips _pendingRenderInScope; RenderAndSendCoalescingAsync rebuilds
// (re-threading historyUrl/replace/auth) and the byte-dedup suppresses
// spurious identical sends. One payload per nav.
public class NavigationCoalescingTests
{
    [Fact]
    public async Task Navigate_WithRouteChangedStateHasChanged_EmitsOnlyOnePayload()
    {
        await using var fixture = await Connect();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/destination", query = "" });

        // Expect a single coalesced frame carrying the navigation target. Pre-fix
        // an earlier history-less frame would arrive first because the eager
        // in-scope render emitted it before EnforceAuthAndRenderAsync ran.
        var first = await fixture.Ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(first);
        using (var doc = JsonDocument.Parse(first!))
        {
            Assert.True(doc.RootElement.TryGetProperty("history", out var history),
                "First (only) post-nav frame must carry history.url — pre-fix this " +
                "frame was the second send and the first was the history-less " +
                "intermediate StateHasChanged emission.");
            Assert.Equal("/destination", history.GetProperty("url").GetString());
            Assert.Equal("push", history.GetProperty("action").GetString());
        }

        // No further outbound frames within the coalescing budget — every
        // in-handler StateHasChanged folded into the single send above. A pre-fix
        // run sees a second frame here (the EnforceAuthAndRenderAsync emission
        // that follows the eager in-scope render).
        var second = await fixture.Ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(500));
        Assert.Null(second);

        // Sanity: route state actually advanced — the RouteState.Changed
        // subscriber on the App ran during this dispatch.
        Assert.Equal("/destination",
            fixture.Session.Services.GetRequiredService<RouteState>().Path);
    }

    [Fact]
    public async Task Navigate_CoalescedPayload_StillCarriesFinalHistoryUrl()
    {
        // Companion to NavigationPublishRerenderTests on the WASM side
        // (Rask.Wasm.Tests/Session/NavigationPublishRerenderTests.cs): even when
        // the rebuild loop fires, the captured historyUrl/replace must be
        // re-threaded so the actually-sent payload preserves it. The Server's
        // RenderAndSendCoalescingAsync re-passes those args on every iteration
        // — without that, navigation would silently lose its pushState.
        await using var fixture = await Connect();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/destination", query = "" });

        // Drain every frame until the receive window closes. The last frame
        // must still carry history.url even if internal rebuilds ran.
        string? lastFrame = null;
        while (await fixture.Ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(500)) is { } frame)
        {
            lastFrame = frame;
        }

        Assert.NotNull(lastFrame);
        using var doc = JsonDocument.Parse(lastFrame!);
        Assert.True(doc.RootElement.TryGetProperty("history", out var history));
        Assert.Equal("/destination", history.GetProperty("url").GetString());
    }

    private static async Task<ConnectedSession> Connect()
    {
        var host = RaskTestHost.Create<NavigateInHandlerStateHasChangedApp>();
        var initial = await host.Http.GetAsync("/start");
        var sessionId = ExtractSessionId(await initial.Content.ReadAsStringAsync());
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        // Drain any post-attach catch-up frame so the per-test assertions
        // observe only nav-driven traffic.
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
