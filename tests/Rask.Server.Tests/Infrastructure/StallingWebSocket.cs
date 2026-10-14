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
    private readonly TaskCompletionSource _sending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _delivered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Aborted { get; private set; }

    public override WebSocketState State { get; } = WebSocketState.Open;
    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override string? SubProtocol => null;

    /// <summary>Completes once a send has reached this socket, whether or not it has finished.</summary>
    public Task Sending => _sending.Task;

    /// <summary>Completes once a send has finished on this socket — the frame was delivered.</summary>
    public Task Delivered => _delivered.Task;

    public void Release() => _released.TrySetResult();

    public override Task SendAsync(
        ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken ct) =>
        SendCoreAsync(ct);

    public override async ValueTask SendAsync(
        ReadOnlyMemory<byte> buffer, WebSocketMessageType type, bool end, CancellationToken ct) =>
        await SendCoreAsync(ct);

    private async Task SendCoreAsync(CancellationToken ct)
    {
        _sending.TrySetResult();
        await _released.Task.WaitAsync(ct);
        _delivered.TrySetResult();
    }

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
