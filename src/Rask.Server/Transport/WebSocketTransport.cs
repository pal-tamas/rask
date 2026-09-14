using System.Net.WebSockets;

namespace Rask.Server.Transport;

/// <summary>
///     A live session's frames over a WebSocket.
/// </summary>
/// <remarks>
///     Deliberately thin: every member forwards to the socket with no state of its own and no async state
///     machine, so naming the seam costs the WebSocket path nothing. <see cref="Socket" /> is exposed for
///     the receive loop, which owns reading and the close handshake.
/// </remarks>
internal sealed class WebSocketTransport(WebSocket socket) : ILiveTransport
{
    /// <summary>The socket this carries frames over, for the loop that reads from it.</summary>
    internal WebSocket Socket { get; } = socket;

    public bool IsOpen => Socket.State == WebSocketState.Open;

    public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Socket.SendAsync(frame, WebSocketMessageType.Text, true, ct);

    /// <summary>
    ///     Sends the close frame and returns, leaving the parked <c>ReceiveAsync</c> to observe the
    ///     browser's echo as an ordinary close message and unwind the loop. <c>CloseAsync</c> would send
    ///     AND receive, which is a second concurrent receive and throws.
    /// </summary>
    public Task CloseAsync(LiveTransportClose reason, string description, CancellationToken ct) =>
        Socket.CloseOutputAsync(Status(reason), description, ct);

    public void Abort()
    {
        try
        {
            Socket.Abort();
        }
        catch
        {
            // Already torn down by the receive loop — nothing left to abort.
        }
    }

    private static WebSocketCloseStatus Status(LiveTransportClose reason) => reason switch
    {
        LiveTransportClose.GoingAway => WebSocketCloseStatus.EndpointUnavailable,
        LiveTransportClose.PolicyViolation => WebSocketCloseStatus.PolicyViolation,
        _ => WebSocketCloseStatus.NormalClosure,
    };
}
