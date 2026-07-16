using System.Net.WebSockets;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// Backpressure circuit-breaker (RaskServerOptions.MaxPendingHandlers): a hung handler
// stalls the dispatch chain head, so queued dispatches — each retaining a cloned JsonElement —
// accumulate. Once the queue exceeds the bound the receive loop must close the socket instead of
// growing memory without limit.
public class HandlerBackpressureTests
{
    [Fact]
    public async Task QueueExceedsBound_WhileHandlerHung_ClosesSocket()
    {
        HangingApp.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var host = RaskTestHost.Create<HangingApp>(
                configureServer: o => o.MaxPendingHandlers = 4);
            var initialHtml = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
            var sessionId = MarkupAssert.SessionId(initialHtml);
            var handlerId = MarkupAssert.FirstHandlerId(initialHtml);

            using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });

            // First click hangs on the gate (chain head stalls); the rest queue behind it. Send
            // well past the bound — once pending exceeds MaxPendingHandlers the server closes the
            // socket, so later client sends may throw; that's expected.
            for (var i = 0; i < 20; i++)
            {
                try
                {
                    await ws.SendJsonAsync(new { id = handlerId });
                }
                catch
                {
                    break; // socket closed mid-flood
                }
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (ws.State == WebSocketState.Open && DateTime.UtcNow < deadline)
            {
                // Draining receives lets the client observe the server's close frame.
                if (await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(100)) is null
                    && ws.State == WebSocketState.Open)
                {
                    await Task.Delay(20);
                }
            }

            Assert.NotEqual(WebSocketState.Open, ws.State);
        }
        finally
        {
            HangingApp.Gate.TrySetResult(); // unblock the hung handler so teardown is clean
        }
    }

    [Fact]
    public async Task UnderBound_NormalTraffic_StaysOpen()
    {
        using var host = RaskTestHost.Create<TestApp>(
            configureServer: o => o.MaxPendingHandlers = 512);
        var initialHtml = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var handlerId = MarkupAssert.FirstHandlerId(initialHtml);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        // These drain quickly (no hung handler), so pending never approaches the bound.
        for (var i = 0; i < 10; i++)
        {
            await ws.SendJsonAsync(new { id = handlerId });
            _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        }

        Assert.Equal(WebSocketState.Open, ws.State);
    }
}
