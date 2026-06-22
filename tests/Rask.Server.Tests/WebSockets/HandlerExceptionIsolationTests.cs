using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// STAB-A / STAB-B regression: the WS dispatcher must survive a faulting handler and must not let a
// stuck handler wedge the session.
//
//  - A throwing handler is caught by the RootErrorBoundary that wraps every App, which renders a
//    styled "Application error" page instead of crashing the socket or returning HTTP 500. The
//    dispatch lock is released as part of that render, so the connection stays open.
//  - The dispatch lock (session.Lock) is distinct from the render lock (_renderLock), and AttachSocket
//    takes neither — so a handler parked under the dispatch lock across a disconnect can't block the
//    reconnect, and once it clears the queued work drains through the reconnected socket.
[Collection("SessionGracePeriod")]
public class HandlerExceptionIsolationTests
{
    // Assert against the legacy full-HTML `html` field (framework default is now diff mode).
    public HandlerExceptionIsolationTests() => LiveOptions.DiffMode = LiveDiffMode.DisabledFull;

    [Fact]
    public async Task FaultingHandler_TripsRootErrorBoundary_KeepsSocketOpen()
    {
        using var host = RaskTestHost.Create<ThrowingHandlerApp>();
        var html = await (await host.Http.GetAsync("/")).Content.ReadAsStringAsync();
        var sessionId = Markup.SessionId(html);
        var boom = HandlerIdFor(html, "boom");

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2)); // initial render dedups → no frame

        // The handler throws; the root error boundary catches it and renders the fallback page.
        // The dispatch completes normally (so the lock is released) and the socket stays open —
        // no HTTP 500, no crash, no leaked lock that would hang every future dispatch.
        await ws.SendJsonAsync(new { id = boom });
        var resp = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(resp);
        Assert.Contains("Application error", HtmlOf(resp!));
        Assert.Equal(WebSocketState.Open, ws.State);
    }

    [Fact]
    public async Task Reconnect_AfterParkedHandler_ResumesDispatchOnceItClears()
    {
        GatedCounterApp.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var host = RaskTestHost.Create<GatedCounterApp>();
            var html = await (await host.Http.GetAsync("/")).Content.ReadAsStringAsync();
            var sessionId = Markup.SessionId(html);
            var hang = HandlerIdFor(html, "hang");
            var bump = HandlerIdFor(html, "bump");

            // Socket 1: park a handler on the gate (it holds the dispatch lock and leaves
            // InHandlerScope set), then drop the socket without releasing it.
            var ws1 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws1.SendJsonAsync(new { type = "hello", session = sessionId });
            _ = await ws1.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
            await ws1.SendJsonAsync(new { id = hang });
            await Task.Delay(150); // let the handler reach the gate
            await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, "drop", CancellationToken.None);
            ws1.Dispose();

            // Socket 2: reconnect within grace. The hello must not block on the parked handler —
            // if it did, this connect+hello+queue sequence would hang here.
            using var ws2 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws2.SendJsonAsync(new { type = "hello", session = sessionId });
            _ = await ws2.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(WebSocketState.Open, ws2.State);

            // Queue a state-changing handler behind the parked one, then release the gate. Once the
            // chain head clears, the bump must run and its render (count=1, so not deduped) must
            // reach the reconnected socket — proving the session resumed rather than wedging.
            await ws2.SendJsonAsync(new { id = bump });
            GatedCounterApp.Gate.TrySetResult();

            // Drain frames until the bump's render lands — the parked handler's own completion
            // render (count=0) may arrive first; the point is the queued bump reaches the socket.
            var sawBump = false;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
            while (DateTime.UtcNow < deadline)
            {
                var frame = await ws2.TryReceiveTextAsync(TimeSpan.FromSeconds(1));
                if (frame is not null && HtmlOf(frame).Contains("count=1"))
                {
                    sawBump = true;
                    break;
                }
            }

            Assert.True(sawBump, "the queued bump never rendered through the reconnected socket");
        }
        finally
        {
            GatedCounterApp.Gate.TrySetResult(); // ensure the parked handler is always released
        }
    }

    private static string HandlerIdFor(string html, string buttonText)
    {
        var match = Regex.Match(
            html,
            "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>[^<]*" + Regex.Escape(buttonText));
        Assert.True(match.Success, $"button '{buttonText}' not found");
        return match.Groups[1].Value;
    }

    private static string HtmlOf(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.GetProperty("html").GetString()!;
    }
}
