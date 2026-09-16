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
    ///     Receives frames until one satisfies <paramref name="predicate" />, and returns it (null if the budget
    ///     runs out first). Frames that do not match are skipped.
    /// </summary>
    /// <remarks>
    ///     For an assertion about a PARTICULAR frame rather than about the next one. A live session may push a
    ///     render the test did not ask for — a catch-up on attach, or the intermediate paint an async handler
    ///     emits while it is still awaiting — and which of those exist depends on timing, so "the first frame
    ///     after my message" is not a property the dispatcher promises. A test that assumes it passes on an idle
    ///     machine and reports the wrong thing on a loaded one: #1120 read a pre-trip document and blamed a
    ///     missing error boundary.
    ///     Ordering IS promised where it matters, and this preserves the assertions that rest on it: frames stay
    ///     in order, so a match here is still the first frame that qualifies.
    /// </remarks>
    public static async Task<string?> ReceiveUntilAsync(
        this WebSocket ws, Func<string, bool> predicate, TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            var text = await ws.TryReceiveTextAsync(remaining);
            if (text is null)
            {
                return null;
            }

            if (predicate(text))
            {
                return text;
            }
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

    /// <summary>
    ///     Closes from the client and waits for the server's answering close frame. The server sends that
    ///     frame from the receive loop's <c>finally</c>, AFTER detaching the socket from its session, so on
    ///     return the loop's cleanup has run — no sleep needed to observe what it did.
    /// </summary>
    public static async Task CloseAndAwaitServerCleanupAsync(this WebSocket ws)
    {
        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        Assert.NotNull(await ws.TryReceiveCloseAsync(TimeSpan.FromSeconds(5)));
    }
}
