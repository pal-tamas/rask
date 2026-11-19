using System.Net.WebSockets;

namespace Rask.Signaling.Tests;

// A relay sends to a peer from every other peer's loop. A peer that stops reading must cost the senders nothing:
// a post never waits, and the peer is cut off once its queue is full.
public class PeerOutboxTests
{
    [Fact]
    public async Task A_peer_that_stops_reading_is_cut_off_and_never_holds_the_sender()
    {
        var socket = new StalledSocket();
        using var outbox = new PeerOutbox();
        var writing = outbox.RunAsync(socket, TestContext.Current.CancellationToken);
        var accepted = 0;

        for (var i = 0; i < PeerOutbox.Capacity + 2; i++)
        {
            accepted += outbox.Post([1]) ? 1 : 0;
        }

        await writing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.InRange(accepted, PeerOutbox.Capacity, PeerOutbox.Capacity + 1);
        Assert.True(socket.Aborted);
        Assert.False(outbox.Post([1]));
    }

    [Fact]
    public async Task What_was_queued_goes_out_in_order_before_the_writer_returns()
    {
        var socket = new StalledSocket { Stalls = false };
        using var outbox = new PeerOutbox();
        var writing = outbox.RunAsync(socket, TestContext.Current.CancellationToken);

        outbox.Post([1]);
        outbox.Post([2]);
        outbox.Complete();
        await writing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal([1, 2], socket.Sent);
        Assert.False(socket.Aborted);
    }

    private sealed class StalledSocket : WebSocket
    {
        public bool Stalls { get; init; } = true;

        public bool Aborted { get; private set; }

        public List<byte> Sent { get; } = [];

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => Aborted ? WebSocketState.Aborted : WebSocketState.Open;

        public override string? SubProtocol => null;

        public override void Abort() => Aborted = true;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override void Dispose()
        {
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            if (Stalls)
            {
                return Task.Delay(Timeout.Infinite, cancellationToken);
            }

            Sent.AddRange(buffer);
            return Task.CompletedTask;
        }
    }
}
