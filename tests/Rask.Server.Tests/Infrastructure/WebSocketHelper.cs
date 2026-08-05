using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Rask.Server.Tests.Infrastructure;

internal static class WebSocketHelper
{
    public static async Task SendJsonAsync(this WebSocket ws, object payload, CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    public static async Task<string?> TryReceiveTextAsync(this WebSocket ws, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var buffer = new byte[16 * 1024];
        var sb = new StringBuilder();
        try
        {
            while (true)
            {
                var result = await ws.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage)
                {
                    return sb.ToString();
                }
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Receives until the peer's close frame arrives and returns its status and reason, or
    ///     <c>null</c> if the socket was aborted instead (no close frame — a <see cref="WebSocketException" />
    ///     out of the receive) or nothing arrived in time. The distinction is the whole point: a graceful
    ///     shutdown must produce a status here, and an abort must not.
    /// </summary>
    public static async Task<(WebSocketCloseStatus? Status, string? Reason)?> TryReceiveCloseAsync(
        this WebSocket ws, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var buffer = new byte[16 * 1024];
        try
        {
            while (true)
            {
                var result = await ws.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return (ws.CloseStatus, ws.CloseStatusDescription);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException)
        {
            // Aborted: the connection died without a close frame. Which of these surfaces depends on
            // the transport — a real socket raises WebSocketException, TestHost's in-memory pipe raises
            // IOException/ObjectDisposedException — and the distinction the caller cares about is only
            // "was there a close frame", so all three mean the same thing here.
            return null;
        }
    }
}
