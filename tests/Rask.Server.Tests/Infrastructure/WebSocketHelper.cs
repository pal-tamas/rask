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

    /// <summary>
    ///     Returns once the server has attached the socket that just sent a hello for <paramref name="sessionId" />.
    /// </summary>
    /// <remarks>
    ///     A hello is answered with nothing unless a render is owed, so draining a frame after it waited out its
    ///     whole timeout — 2 s a test, about half this suite's time — and only incidentally left the attach done.
    ///     This waits for the attach itself. A session the store does not know keeps the old drain.
    /// </remarks>
    public static async Task AttachedAsync(this WebSocket ws, RaskTestHost host, string sessionId, TimeSpan timeout)
    {
        if (host.Store.Peek(sessionId) is not { } session)
        {
            _ = await ws.TryReceiveTextAsync(timeout);
            return;
        }

        // Qualified: this file is linked into test projects that do not import Rask.TestSupport globally.
        await Rask.TestSupport.WaitFor.True(() => session.HasOpenTransport, timeout, "the server attaches the socket");
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
        catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException)
        {
            // Aborted — nothing more will arrive, which is an assertion for the caller, not a crash.
            return null;
        }
    }

    /// <summary>
    ///     Reads every frame until the server closes, and answers its close the way a browser does. Start it
    ///     BEFORE the server begins to close: the answer completes the handshake, so a drain ends on it rather
    ///     than waiting out its budget and aborting (#1138).
    /// </summary>
    /// <returns>The text frames, then the close status and reason — both null if the socket was aborted instead.</returns>
    public static async Task<(List<string> Frames, WebSocketCloseStatus? Status, string? Reason)> ReadUntilServerClosesAsync(
        this WebSocket ws, TimeSpan timeout)
    {
        var frames = new List<string>();
        while (await ws.TryReceiveTextAsync(timeout) is { } frame)
        {
            frames.Add(frame);
        }

        if (ws.State != WebSocketState.CloseReceived)
        {
            return (frames, null, null);
        }

        // Read before the answer: what the server said is settled the moment its close arrived.
        var (status, reason) = (ws.CloseStatus, ws.CloseStatusDescription);
        try
        {
            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException)
        {
            // The server did not wait for the answer. Its receive loop reads only while the socket is Open,
            // so a close that lands between two frames ends the loop, and the connection, with the answer
            // still on its way. The close the server SENT is what a caller asserts on, and it arrived.
        }

        return (frames, status, reason);
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
