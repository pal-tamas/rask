using System.Net.WebSockets;
using System.Text.Json;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// The client's slow-link pending-action bar resolves on a server `{type:"ack",seq}` frame.
// The ack is opt-in (only emitted when the inbound handler carried a `seq`) and must fire
// even when the render dedupes and ships no frame — otherwise a no-op click would wedge the
// bar until the client's hard-timeout backstop. These tests pin that protocol.
public class PendingAckTests
{
    [Fact]
    public async Task DedupedHandler_WithSeq_EmitsAckWithoutRenderFrame()
    {
        using var host = RaskTestHost.Create<NoOpApp>();
        var (ws, handlerId) = await ConnectAsync(host);

        // NoOpApp renders byte-identically, so the handler produces no frame — the ack is
        // the only thing on the wire, and the client needs it to clear the pending bar.
        await ws.SendJsonAsync(new { id = handlerId, seq = 1 });
        var (renders, ackSeq) = await ReadUntilAckAsync(ws);

        Assert.Empty(renders);
        Assert.Equal(1, ackSeq);
    }

    [Fact]
    public async Task StateChangingHandler_WithSeq_EmitsRenderThenAck()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var (ws, handlerId) = await ConnectAsync(host);

        await ws.SendJsonAsync(new { id = handlerId, seq = 7 });
        var (renders, ackSeq) = await ReadUntilAckAsync(ws);

        // The render frame lands first (the chain acks only after the dispatch's render),
        // then the ack closes the round-trip.
        Assert.Single(renders);
        Assert.Contains("count=1", renders[0]);
        Assert.Equal(7, ackSeq);
    }

    [Fact]
    public async Task Handler_WithoutSeq_EmitsNoAck()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var (ws, handlerId) = await ConnectAsync(host);

        // Opt-in: a seq-less client gets the render frame and nothing else — the exact
        // pre-feature contract, so existing dedup/ordering behaviour is untouched.
        await ws.SendJsonAsync(new { id = handlerId });
        var render = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(render);
        Assert.Contains("count=1", render!);
        Assert.False(IsAck(render!));

        var trailing = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));
        Assert.Null(trailing);
    }

    [Fact]
    public async Task StaleHandlerId_WithSeq_StillAcks()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var (ws, _) = await ConnectAsync(host);

        // Unknown handler id → TryInvokeHandlerAsync is false → no render runs, yet the
        // dispatch still completes, so the ack must flow and unwedge the client.
        await ws.SendJsonAsync(new { id = "does-not-exist", seq = 9 });
        var (renders, ackSeq) = await ReadUntilAckAsync(ws);

        Assert.Empty(renders);
        Assert.Equal(9, ackSeq);
    }

    [Fact]
    public async Task BurstOfHandlers_AcksInArrivalOrder()
    {
        using var host = RaskTestHost.Create<NoOpApp>();
        var (ws, handlerId) = await ConnectAsync(host);

        await ws.SendJsonAsync(new { id = handlerId, seq = 1 });
        await ws.SendJsonAsync(new { id = handlerId, seq = 2 });
        await ws.SendJsonAsync(new { id = handlerId, seq = 3 });

        // The handler chain dispatches in WS-arrival order, so acks come back 1, 2, 3.
        var seqs = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            var (renders, ackSeq) = await ReadUntilAckAsync(ws);
            Assert.Empty(renders);
            seqs.Add(ackSeq);
        }

        Assert.Equal(new long[] { 1, 2, 3 }, seqs);
    }

    [Fact]
    public async Task Navigate_WithSeq_DoesNotAck()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var (ws, _) = await ConnectAsync(host);

        // navigate is handled inline, never through the handler chain, so it carries no
        // seq from the client and the server must not ack it even if a seq is present.
        await ws.SendJsonAsync(new { type = "navigate", path = "/other", query = "", seq = 1 });

        string? frame;
        while ((frame = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(500))) is not null)
        {
            Assert.False(IsAck(frame), "navigate must not produce an ack frame");
        }
    }

    // Connects, sends hello, and drains any hello-time recovery frame so each test starts
    // from a quiet socket. Returns the live socket and the page's first click-handler id.
    private static async Task<(WebSocket ws, string handlerId)> ConnectAsync(RaskTestHost host)
    {
        var initial = await host.Http.GetAsync("/start");
        var html = await initial.Content.ReadAsStringAsync();
        var sessionId = Markup.SessionId(html);
        var handlerId = Markup.FirstHandlerId(html);

        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(300));
        return (ws, handlerId);
    }

    // Reads frames until an ack arrives, collecting any render frames seen first. An ack is
    // expected within the timeout — a missing ack is a protocol failure, not a pass.
    private static async Task<(List<string> renders, long ackSeq)> ReadUntilAckAsync(WebSocket ws)
    {
        var renders = new List<string>();
        while (true)
        {
            var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
            Assert.NotNull(text);
            using var doc = JsonDocument.Parse(text!);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var tp)
                && tp.ValueKind == JsonValueKind.String
                && tp.GetString() == "ack")
            {
                return (renders, root.GetProperty("seq").GetInt64());
            }

            renders.Add(text!);
        }
    }

    private static bool IsAck(string frame)
    {
        using var doc = JsonDocument.Parse(frame);
        return doc.RootElement.TryGetProperty("type", out var tp)
               && tp.ValueKind == JsonValueKind.String
               && tp.GetString() == "ack";
    }
}
