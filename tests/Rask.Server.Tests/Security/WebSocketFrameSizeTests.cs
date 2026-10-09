using System.Net.WebSockets;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Security;

// M1: a client must not be able to stream an unbounded fragmented WS frame and force the server to
// buffer it whole before parsing. The receive loop caps reassembly at MaxInboundFrameBytes and
// aborts the socket past it.
public class WebSocketFrameSizeTests
{
    [Fact]
    public async Task An_oversized_inbound_frame_aborts_the_socket()
    {
        using var host = RaskTestHost.Create<TestApp>(
            configureServer: o => o.MaxInboundFrameBytes = 32 * 1024); // small cap for the test
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

    [Fact]
    public async Task Large_messages_split_over_frames_are_each_dispatched()
    {
        // Past the 16 KB receive buffer, so each arrives in pieces; past the 64 KB the reassembly buffer is kept
        // at, so the second is reassembled after the first one's buffer was let go.
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull);
        var initialHtml = await (await host.Http.GetAsync("/start", TestContext.Current.CancellationToken))
            .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var handlerId = MarkupAssert.FirstHandlerId(initialHtml);
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);
        var padding = new string('x', 100 * 1024);

        await ws.SendJsonAsync(new { id = handlerId, padding }, ct: TestContext.Current.CancellationToken);
        var first = await ws.ReceiveTextAsync();
        await ws.SendJsonAsync(new { id = handlerId, padding }, ct: TestContext.Current.CancellationToken);
        var second = await ws.ReceiveTextAsync();

        Assert.Contains("count=1", first, StringComparison.Ordinal);
        Assert.Contains("count=2", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_normal_sized_frame_is_processed()
    {
        // Guard against a too-tight cap regressing legitimate traffic: a normal hello round-trips
        // fine under the default cap.
        using var host = RaskTestHost.Create<TestApp>();
        var get = await host.Http.GetAsync("/", TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(await get.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);

        // No exception, socket stays open.
        Assert.Equal(WebSocketState.Open, ws.State);
    }
}
