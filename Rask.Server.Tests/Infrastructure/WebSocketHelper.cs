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
}
