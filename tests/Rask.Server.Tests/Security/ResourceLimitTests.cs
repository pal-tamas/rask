using System.Net.WebSockets;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Security;

// Phase-4 resource-exhaustion limits: an idle connected socket is reclaimed, and the pending-handler
// queue is bounded by aggregate bytes (not just count). Both close the socket; the session survives.
public class ResourceLimitTests
{
    [Fact]
    public async Task IdleSocket_NoInboundFrames_IsClosedAfterTheTimeout()
    {
        using var host = RaskTestHost.Create<TestApp>(
            configureServer: o => o.IdleSocketTimeout = TimeSpan.FromMilliseconds(300));
        var sessionId = MarkupAssert.SessionId(await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync());

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        // Send nothing further — the server must close the idle socket within the timeout window.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (ws.State == WebSocketState.Open && DateTime.UtcNow < deadline)
        {
            if (await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(100)) is null
                && ws.State == WebSocketState.Open)
            {
                await Task.Delay(20);
            }
        }

        Assert.NotEqual(WebSocketState.Open, ws.State);
    }

    [Fact]
    public async Task ActiveSocket_UnderIdleTimeout_StaysOpen()
    {
        // Comfortably larger than the inter-send gap below, so only genuine inactivity trips it.
        using var host = RaskTestHost.Create<TestApp>(
            configureServer: o => o.IdleSocketTimeout = TimeSpan.FromSeconds(5));
        var html = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(html);
        var handlerId = MarkupAssert.FirstHandlerId(html);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(500));

        // Keep sending well within the 5 s window — the socket must stay open across a span
        // (~3 s) that would have tripped a naive total-lifetime timeout.
        for (var i = 0; i < 4; i++)
        {
            await Task.Delay(800);
            await ws.SendJsonAsync(new { id = handlerId });
            _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        }

        Assert.Equal(WebSocketState.Open, ws.State);
    }

    [Fact]
    public async Task PendingHandlerBytes_ExceedsCap_ClosesSocket()
    {
        // 1 byte: the first handler frame's payload exceeds it, so the byte cap trips immediately
        // (the count cap is left generous so this isolates the byte path).
        using var host = RaskTestHost.Create<TestApp>(
            configureServer: o => { o.MaxPendingHandlerBytes = 1; o.MaxPendingHandlers = 10_000; });
        var html = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(html);
        var handlerId = MarkupAssert.FirstHandlerId(html);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        for (var i = 0; i < 20; i++)
        {
            try { await ws.SendJsonAsync(new { id = handlerId }); }
            catch { break; }
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (ws.State == WebSocketState.Open && DateTime.UtcNow < deadline)
        {
            if (await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(100)) is null
                && ws.State == WebSocketState.Open)
            {
                await Task.Delay(20);
            }
        }

        Assert.NotEqual(WebSocketState.Open, ws.State);
    }
}
