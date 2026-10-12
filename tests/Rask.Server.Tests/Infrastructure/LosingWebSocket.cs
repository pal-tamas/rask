using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     The host's own socket, except that the test can have the next frame the server writes fail with the
///     connection's error instead of going out: a link that dropped one write, made deterministic.
/// </summary>
/// <remarks>
///     Wraps whatever the host accepts, from a middleware placed after <c>UseWebSockets</c>
///     (<see cref="RaskTestHost.Create{TApp}" />'s <c>configureMiddleware</c>). Everything else is the real
///     socket, so the session keeps talking to the client after the write that failed.
/// </remarks>
internal sealed class LosingWebSocket
{
    private int _loseNext;

    /// <summary>The next frame the server sends is lost, and its send throws.</summary>
    public void LoseNext() => Volatile.Write(ref _loseNext, 1);

    public Task Wrap(HttpContext context, RequestDelegate next)
    {
        if (context.Features.Get<IHttpWebSocketFeature>() is { } accepts)
        {
            context.Features.Set<IHttpWebSocketFeature>(new Feature(accepts, this));
        }

        return next(context);
    }

    private bool Loses() => Interlocked.Exchange(ref _loseNext, 0) == 1;

    private sealed class Feature(IHttpWebSocketFeature inner, LosingWebSocket owner) : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => inner.IsWebSocketRequest;

        public async Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) =>
            new Socket(await inner.AcceptAsync(context), owner);
    }

    private sealed class Socket(WebSocket inner, LosingWebSocket owner) : WebSocket
    {
        public override WebSocketCloseStatus? CloseStatus => inner.CloseStatus;
        public override string? CloseStatusDescription => inner.CloseStatusDescription;
        public override WebSocketState State => inner.State;
        public override string? SubProtocol => inner.SubProtocol;

        public override Task SendAsync(
            ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken ct) =>
            owner.Loses() ? throw Lost() : inner.SendAsync(buffer, type, end, ct);

        public override ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer, WebSocketMessageType type, bool end, CancellationToken ct) =>
            owner.Loses() ? throw Lost() : inner.SendAsync(buffer, type, end, ct);

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct) =>
            inner.ReceiveAsync(buffer, ct);

        public override ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken ct) =>
            inner.ReceiveAsync(buffer, ct);

        public override void Abort() => inner.Abort();

        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) =>
            inner.CloseAsync(status, description, ct);

        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) =>
            inner.CloseOutputAsync(status, description, ct);

        public override void Dispose() => inner.Dispose();

        private static WebSocketException Lost() => new(WebSocketError.Faulted, "the frame was lost on the way out");
    }
}
