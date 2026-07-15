using System.Net.WebSockets;

namespace Rask.Benchmarks.Infrastructure;

/// <summary>
///     A WebSocket that reports itself Open and drops every frame it is handed, counting the bytes.
///     <para>
///         The capacity reports need a session in its <b>connected steady state</b>: every buffer pair
///         (<c>RenderedHtmlBuffers</c>, the <c>SessionRenderCache</c> frame writers, and
///         <c>_writeBuffer</c>/<c>_lastSentBuffer</c>) grown to its high-water mark. Those only fill on
///         the render→send path, and <c>LiveSession.RenderAndSendAsync</c> early-returns unless a socket
///         is attached and <see cref="WebSocketState.Open" />. A real socket would work but would fold
///         Kestrel's own per-connection buffers (32 KB of receive buffer alone) into a number that is
///         supposed to measure the <i>session</i> — so the transport is stubbed out instead.
///     </para>
///     <para>
///         Only the send path is implemented: the reports drive renders directly and never run the
///         inbound socket loop, so <see cref="ReceiveAsync" /> would signal a bug in the harness rather
///         than a case worth faking. <see cref="BytesSent" /> is the harness's proof that frames really
///         flowed — a session whose count is still zero never reached steady state and its footprint
///         would be silently under-measured.
///     </para>
/// </summary>
internal sealed class NullWebSocket : WebSocket
{
    private WebSocketState _state = WebSocketState.Open;

    /// <summary>Total payload bytes this socket was handed. Zero means no frame ever reached the wire.</summary>
    public long BytesSent { get; private set; }

    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override WebSocketState State => _state;
    public override string? SubProtocol => null;

    public override void Abort() => _state = WebSocketState.Aborted;

    public override void Dispose() => _state = WebSocketState.Closed;

    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
        CancellationToken cancellationToken)
    {
        _state = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
        CancellationToken cancellationToken)
    {
        _state = WebSocketState.CloseSent;
        return Task.CompletedTask;
    }

    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The capacity reports drive renders directly and never receive.");

    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType,
        bool endOfMessage, CancellationToken cancellationToken)
    {
        BytesSent += buffer.Count;
        return Task.CompletedTask;
    }

    // LiveSession sends via the ReadOnlyMemory overload. The base implementation would unwrap it to the
    // ArraySegment one above, but only after a MemoryMarshal.TryGetArray round-trip — overriding here
    // keeps the stub's own cost out of the churn pass's allocation counter.
    public override ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType,
        bool endOfMessage, CancellationToken cancellationToken)
    {
        BytesSent += buffer.Length;
        return ValueTask.CompletedTask;
    }
}
