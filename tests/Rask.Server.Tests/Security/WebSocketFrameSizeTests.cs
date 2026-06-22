using System.Net.WebSockets;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Security;

// M1: a client must not be able to stream an unbounded fragmented WS frame and force the server to
// buffer it whole before parsing. The receive loop caps reassembly at MaxInboundFrameBytes and
// aborts the socket past it.
//
// In SessionGracePeriod (DisableParallelization) so the static MaxInboundFrameBytes write doesn't
// leak onto a parallel test's connection (same rationale as WebSocketFrameRateTests / the
// MaxPendingHandlers tests). The 32 KB cap is benign for typical small-frame tests, but serialising
// the global mutation keeps it from ever applying to traffic another test depends on.
[Collection("SessionGracePeriod")]
public class WebSocketFrameSizeTests
{
    [Fact]
    public async Task OversizedInboundFrame_AbortsSocket()
    {
        var prev = RaskEndpointExtensions.MaxInboundFrameBytes;
        RaskEndpointExtensions.MaxInboundFrameBytes = 32 * 1024; // small cap for the test
        try
        {
            using var host = RaskTestHost.Create<TestApp>();
            using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);

            // Larger than both the 16KB server receive buffer (forces the multi-fragment reassembly
            // path) and the cap. The cap is enforced before JSON parsing, so raw bytes suffice.
            var big = new byte[256 * 1024];

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var aborted = false;
            try
            {
                await ws.SendAsync(big, WebSocketMessageType.Text, true, cts.Token);
                var buf = new byte[1024];
                while (true)
                {
                    var r = await ws.ReceiveAsync(buf, cts.Token);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        aborted = true;
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Timed out without the socket being torn down → the cap did not fire.
            }
            catch (Exception)
            {
                // WebSocketException / IOException etc. — the server aborted the connection.
                aborted = true;
            }

            Assert.True(aborted, "server must abort the socket on an over-cap inbound frame");
        }
        finally
        {
            RaskEndpointExtensions.MaxInboundFrameBytes = prev;
        }
    }

    [Fact]
    public async Task NormalSizedFrame_IsProcessed()
    {
        // Guard against a too-tight cap regressing legitimate traffic: a normal hello round-trips
        // fine under the default cap.
        using var host = RaskTestHost.Create<TestApp>();
        var get = await host.Http.GetAsync("/");
        var sessionId = Markup.SessionId(await get.Content.ReadAsStringAsync());

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        // No exception, socket stays open.
        Assert.Equal(WebSocketState.Open, ws.State);
    }
}
