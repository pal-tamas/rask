using System.Net.WebSockets;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Security;

// A client must not be able to flood the socket with small frames the per-frame size cap and the
// handler-backlog breaker don't bound — each non-handler frame (jsResult / navigate / malformed)
// still costs a JSON parse. The receive loop counts inbound frames over a sliding one-second window
// (MaxInboundFramesPerSecond) and closes the socket on a flood.
//
// In SessionGracePeriod (DisableParallelization) so the static MaxInboundFramesPerSecond write can't
// leak onto a parallel test's connection: a low cap here would otherwise trip the breaker on an
// unrelated socket mid-run (e.g. CheckboxBindingDiffTests sends 6 frames and would be closed at the
// 6th). Same rationale that puts HandlerBackpressureTests (MaxPendingHandlers) in this collection.
[Collection("SessionGracePeriod")]
public class WebSocketFrameRateTests
{
    [Fact]
    public async Task InboundFrameFlood_ClosesSocket()
    {
        var prev = RaskEndpointExtensions.MaxInboundFramesPerSecond;
        RaskEndpointExtensions.MaxInboundFramesPerSecond = 5; // small cap for the test
        try
        {
            using var host = RaskTestHost.Create<TestApp>();
            using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var closed = false;
            try
            {
                // Fire well past the cap within the one-second window. Tiny valid-JSON frames are
                // counted before any handler routing, so they trip the rate breaker.
                for (var i = 0; i < 50; i++)
                {
                    await ws.SendJsonAsync(new { type = "noop" });
                }

                var buf = new byte[1024];
                while (true)
                {
                    var r = await ws.ReceiveAsync(buf, cts.Token);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        closed = true;
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Timed out without the socket closing → the cap did not fire.
            }
            catch (Exception)
            {
                // The server closed/aborted mid-send → the cap fired.
                closed = true;
            }

            Assert.True(closed, "server must close the socket on an inbound frame flood");
        }
        finally
        {
            RaskEndpointExtensions.MaxInboundFramesPerSecond = prev;
        }
    }

    [Fact]
    public async Task FramesUnderRateCap_AreProcessed()
    {
        // Guard against the cap regressing legitimate traffic: a hello plus a few frames under the
        // default cap round-trip fine and leave the socket open.
        using var host = RaskTestHost.Create<TestApp>();
        var get = await host.Http.GetAsync("/");
        var sessionId = Markup.SessionId(await get.Content.ReadAsStringAsync());

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        for (var i = 0; i < 10; i++)
        {
            await ws.SendJsonAsync(new { type = "noop" });
        }

        Assert.Equal(WebSocketState.Open, ws.State);
    }
}
