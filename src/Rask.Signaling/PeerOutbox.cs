using System.Net.WebSockets;
using System.Threading.Channels;

namespace Rask.Signaling;

/// <summary>
///     The frames waiting for one connection, written by that connection's own writer and nobody else's.
/// </summary>
/// <remarks>
///     A WebSocket allows one send at a time, and a relay sends to a peer from every other peer's loop. Writing
///     to the socket from those loops made each sender wait on the slowest receiver — a peer that stopped reading
///     held every peer that addressed it. So a sender only posts (<see cref="Post" />, never blocking), a bounded
///     queue holds what has not gone yet, and <see cref="RunAsync" /> sends it, each frame within
///     <see cref="SendTimeout" />. A peer whose queue fills, or whose socket takes longer than that, is not
///     keeping up: it is cut off, and its own loop takes it out of its room.
/// </remarks>
internal sealed class PeerOutbox : IDisposable
{
    /// <summary>How many frames may wait for one peer: a burst of ICE candidates, many times over.</summary>
    internal const int Capacity = 256;

    /// <summary>The longest one frame may take to send before the peer is cut off.</summary>
    internal static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    private readonly Channel<byte[]> _frames = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    private readonly CancellationTokenSource _cut = new();

    /// <summary>Queues <paramref name="frame" />; false when the connection is gone or was cut off for not keeping up.</summary>
    internal bool Post(byte[] frame)
    {
        if (_frames.Writer.TryWrite(frame))
        {
            return true;
        }

        if (_frames.Writer.TryComplete())
        {
            Cut();
        }

        return false;
    }

    /// <summary>No more frames: the writer sends what is queued and returns.</summary>
    internal void Complete() => _frames.Writer.TryComplete();

    /// <summary>Sends the queued frames, in order, until <see cref="Complete" /> or until the peer is cut off.</summary>
    internal async Task RunAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cut.Token);
        try
        {
            await foreach (var frame in _frames.Reader.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                if (socket.State != WebSocketState.Open)
                {
                    return;
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(SendTimeout);
                await socket.SendAsync(frame, WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            // Too slow, cut off, or gone: the socket is ended, so the peer's own receive loop ends with it and
            // takes the peer out of its room.
            socket.Abort();
        }
    }

    public void Dispose() => _cut.Dispose();

    // Ends the writer at once, whatever it is sending: a queue that filled means the peer is not reading.
    private void Cut()
    {
        try
        {
            _cut.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The connection has already finished.
        }
    }
}
