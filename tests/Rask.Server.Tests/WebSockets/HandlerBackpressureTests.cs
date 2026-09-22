using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// Backpressure circuit-breaker (RaskServerOptions.MaxPendingHandlers): a hung handler
// stalls the dispatch chain head, so queued dispatches — each retaining a cloned JsonElement —
// accumulate. Once the queue exceeds the bound the receive loop must close the socket instead of
// growing memory without limit. Over HTTP the POST that trips it is refused and the stream ends with the
// reason, which is what the browser reconnects from.
public class HandlerBackpressureTests
{
    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_queue_past_its_bound_while_a_handler_hangs_closes_the_socket(LiveTransportKind transport)
    {
        HangingApp.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var host = RaskTestHost.Create<HangingApp>(
                configureServer: o => o.MaxPendingHandlers = 4);
            var initialHtml = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
            var sessionId = MarkupAssert.SessionId(initialHtml);
            var handlerId = MarkupAssert.FirstHandlerId(initialHtml);

            await using var ws = await LiveTestConnection.OpenAsync(host, transport, sessionId);

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
            while (ws.IsOpen && DateTime.UtcNow < deadline)
            {
                // Draining receives lets the client observe the server's close frame.
                if (await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(100)) is null && ws.IsOpen)
                {
                    await Task.Delay(20);
                }
            }

            Assert.False(ws.IsOpen);
        }
        finally
        {
            HangingApp.Gate.TrySetResult(); // unblock the hung handler so teardown is clean
        }
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task Normal_traffic_under_the_bound_stays_open(LiveTransportKind transport)
    {
        using var host = RaskTestHost.Create<TestApp>(
            configureServer: o => o.MaxPendingHandlers = 512);
        var initialHtml = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var handlerId = MarkupAssert.FirstHandlerId(initialHtml);

        await using var ws = await LiveTestConnection.OpenAsync(host, transport, sessionId);

        // These drain quickly (no hung handler), so pending never approaches the bound.
        for (var i = 0; i < 10; i++)
        {
            await ws.SendJsonAsync(new { id = handlerId });
            _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        }

        Assert.True(ws.IsOpen);
    }
}
