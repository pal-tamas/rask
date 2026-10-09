using System.Diagnostics;
using System.Net.WebSockets;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Security;

// Phase-4 resource-exhaustion limits: an idle connected socket is reclaimed, and the pending-handler
// queue is bounded by aggregate bytes (not just count). Both close the socket; the session survives.
public class ResourceLimitTests
{
    [Fact]
    public async Task A_socket_with_no_inbound_frames_is_closed_after_the_idle_timeout()
    {
        var idleTimeout = TimeSpan.FromMilliseconds(300);
        using var host = RaskTestHost.Create<TestApp>(configureServer: o => o.IdleSocketTimeout = idleTimeout);
        var sessionId = MarkupAssert.SessionId(await (await host.Http.GetAsync("/start", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var connecting = Stopwatch.GetTimestamp();
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);

        // Send nothing further. The wait is for the server's close itself, however long this machine takes
        // to deliver it; what is measured is the one thing a busy machine cannot fake, that the close did
        // not come sooner than the timeout the socket was given.
        var close = await ws.TryReceiveCloseAsync(LiveFrames.HangCeiling);
        var open = Stopwatch.GetElapsedTime(connecting);

        Assert.NotNull(close);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, close.Value.Status);
        Assert.Equal("idle timeout", close.Value.Reason);
        Assert.True(open >= idleTimeout, $"closed after {open}, sooner than the {idleTimeout} it was allowed to idle");
    }

    [Fact]
    public async Task An_active_socket_under_the_idle_timeout_stays_open()
    {
        // Comfortably larger than the inter-send gap below, so only genuine inactivity trips it.
        using var host = RaskTestHost.Create<TestApp>(
            configureServer: o => o.IdleSocketTimeout = TimeSpan.FromSeconds(5));
        var html = await (await host.Http.GetAsync("/start", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(html);
        var handlerId = MarkupAssert.FirstHandlerId(html);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);

        // Keep sending well within the 5 s window — the socket must stay open across a span
        // (~3 s) that would have tripped a naive total-lifetime timeout.
        for (var i = 0; i < 4; i++)
        {
            await Task.Delay(800, TestContext.Current.CancellationToken);
            await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
            await ws.ReceiveTextAsync();
        }

        Assert.Equal(WebSocketState.Open, ws.State);
    }

    [Fact]
    public async Task Pending_handler_bytes_over_the_cap_close_the_socket()
    {
        // 1 byte: the first handler frame's payload exceeds it, so the byte cap trips immediately
        // (the count cap is left generous so this isolates the byte path).
        using var host = RaskTestHost.Create<TestApp>(
            configureServer: o => { o.MaxPendingHandlerBytes = 1; o.MaxPendingHandlers = 10_000; });
        var html = await (await host.Http.GetAsync("/start", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(html);
        var handlerId = MarkupAssert.FirstHandlerId(html);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);

        for (var i = 0; i < 20; i++)
        {
            try { await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken); }
            catch { break; }
        }

        _ = await ws.TryReceiveCloseAsync(LiveFrames.HangCeiling);

        Assert.NotEqual(WebSocketState.Open, ws.State);
    }
}
