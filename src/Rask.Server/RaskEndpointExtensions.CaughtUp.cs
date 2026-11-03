using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Rask.Server.Diagnostics;

namespace Rask.Server;

/// <content>Telling the browser it holds every page sent to it: what a press made again waits for.</content>
public static partial class RaskEndpointExtensions
{
    // {"type":"caughtUp","n":N} asks; the same frame answers, behind every page already sent and every event
    // queued before it. A yes to a `.Confirm` presses its button again (rask-confirm.ts), and the browser's own
    // dialog held the page still while the server rendered on: the press must be read from the page as the server
    // has it, not from the one the question was asked on — which a slow line can leave far behind the server's.
    private static FrameOutcome QueueCaughtUp(
        LiveSession session,
        JsonElement root,
        int payloadLength,
        LiveSessionStore store,
        RaskServerLimits limits,
        RaskMetrics? metrics)
    {
        if (!root.TryGetProperty("n", out var n)
            || n.ValueKind != JsonValueKind.Number
            || !n.TryGetInt64(out var asked))
        {
            return new FrameOutcome(FrameStatus.Handled, null);
        }

        var payloadBytes = (long)payloadLength;
        if (!AdmitHandler(session, store, limits, metrics, payloadBytes))
        {
            return new FrameOutcome(FrameStatus.Refused, "handler backlog");
        }

        session.EnqueueOnHandlerChain(previous => AnswerCaughtUpAsync(previous, store, session, asked, payloadBytes));
        return new FrameOutcome(FrameStatus.Handled, null);
    }

    private static async Task AnswerCaughtUpAsync(
        Task previous, LiveSessionStore store, LiveSession session, long asked, long payloadBytes)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64);
        try
        {
            await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            // Out of band, under the render lock: it leaves after the page being sent now, if one is, and that page
            // is kept for the press the browser makes again from it.
            await session.AnswerCaughtUpAsync(buffer.AsMemory(0, WriteCaughtUp(buffer, asked))).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // The connection or the session went meanwhile: the browser asks again on the one it opens next.
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            session.DecrementPendingHandlers();
            store.HandlerDequeued();
            session.SubtractPendingHandlerBytes(payloadBytes);
        }
    }

    // {"type":"caughtUp","n":N} — 44 bytes at most (long.MinValue).
    internal static int WriteCaughtUp(Span<byte> destination, long asked)
    {
        System.Text.Unicode.Utf8.TryWrite(destination, CultureInfo.InvariantCulture,
            $"{{\"type\":\"caughtUp\",\"n\":{asked}}}", out var written);
        return written;
    }
}
