using System.Net.WebSockets;

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
/// An open socket whose sends never complete until the test says so — a client that has stopped reading,
/// made deterministic.
/// </summary>
/// <remarks>
/// <c>WebSocket.SendAsync</c> completes when the frame reaches the transport, not when the client reads it,
/// so a real client that stops reading fills the send buffer and the send never returns. That is not
/// reproducible against an in-memory test host (the OS absorbs a small frame either way), which is why the
/// behaviour is modelled here instead.
/// </remarks>
internal sealed class StallingWebSocket : WebSocket
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Aborted { get; private set; }

    public override WebSocketState State { get; } = WebSocketState.Open;
    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override string? SubProtocol => null;

    public void Release() => _released.TrySetResult();

    public override async Task SendAsync(
        ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken ct) =>
        await _released.Task.WaitAsync(ct);

    public override async ValueTask SendAsync(
        ReadOnlyMemory<byte> buffer, WebSocketMessageType type, bool end, CancellationToken ct) =>
        await _released.Task.WaitAsync(ct);

    public override void Abort()
    {
        Aborted++;
        _released.TrySetCanceled();
    }

    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct) =>
        throw new NotSupportedException("This socket only ever sends.");

    public override Task CloseAsync(WebSocketCloseStatus s, string? d, CancellationToken ct) => Task.CompletedTask;
    public override Task CloseOutputAsync(WebSocketCloseStatus s, string? d, CancellationToken ct) => Task.CompletedTask;
    public override void Dispose() { }
}
