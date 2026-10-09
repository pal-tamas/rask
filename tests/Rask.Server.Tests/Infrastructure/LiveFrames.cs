using System.Text.Json;
using Xunit.Sdk;

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     Waits on what a live connection SAYS, never on how long it took to say it: the next frame, the frame
///     that carries a given thing, or the server's own word that it has finished what it was sent.
/// </summary>
/// <remarks>
///     <para>
///         A test that reads "the next frame within two seconds" or "frames until half a second of silence"
///         measures the machine. A render an idle laptop sends in a millisecond takes seconds on a runner
///         whose cores are all busy, so both shapes pass locally and fail in CI on tests that are right.
///     </para>
///     <para>
///         Written against two delegates rather than a socket, so a WebSocket and the HTTP fallback's stream
///         wait the same way (<see cref="WebSocketHelper" />, <see cref="LiveTestConnectionFrames" />).
///     </para>
/// </remarks>
internal static class LiveFrames
{
    /// <summary>The ceiling on every wait here: it bounds a hang, never the work. See <see cref="Rask.TestSupport.WaitFor.HangCeiling" />.</summary>
    public static readonly TimeSpan HangCeiling = Rask.TestSupport.WaitFor.HangCeiling;

    // No handler carries this id (theirs are h0, h1, …), so the dispatch runs nothing and renders nothing.
    private const string SettleHandlerId = "settle";

    // Counts down from below zero: a test's own seq is a small positive number, and must not be mistaken for one.
    private static long _lastSettleSeq;

    /// <summary>The next frame. Fails the test when the connection ends, or hangs, without sending one.</summary>
    public static async Task<string> NextAsync(Func<TimeSpan, Task<string?>> receive) =>
        await receive(HangCeiling) ?? throw NeverCame("a frame", []);

    /// <summary>
    ///     The first frame <paramref name="isIt" /> accepts, skipping the ones it does not. Fails the test, naming
    ///     <paramref name="what" /> and the frames it skipped, when the connection ends or hangs first.
    /// </summary>
    /// <remarks>
    ///     For an assertion about a PARTICULAR frame. A session may push a render the test did not ask for (a
    ///     catch-up on attach, the intermediate paint of a handler still awaiting), and which of those exist is
    ///     a matter of timing. Frames stay in order, so a match is still the first frame that qualifies.
    /// </remarks>
    public static async Task<string> UntilAsync(
        Func<TimeSpan, Task<string?>> receive, Func<string, bool> isIt, string what)
    {
        var skipped = new List<string>();
        var deadline = DateTime.UtcNow + HangCeiling;
        while (deadline - DateTime.UtcNow is var remaining && remaining > TimeSpan.Zero
               && await receive(remaining) is { } frame)
        {
            if (isIt(frame))
            {
                return frame;
            }

            skipped.Add(frame);
        }

        throw NeverCame(what, skipped);
    }

    /// <summary>
    ///     Returns once the server has finished every frame this connection sent it, with the frames it sent
    ///     back in the meantime, in order.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is how a test says "and nothing else came" (the list is empty) or "whatever that produced,
    ///         I have it all" without waiting out a silence. It sends a handler frame for an id no handler has,
    ///         stamped with a <c>seq</c>, and reads until the acknowledgement of that <c>seq</c>. The reader
    ///         takes frames in arrival order and runs a navigation to its end before reading on; handlers run
    ///         one after another; and an acknowledgement is sent after its own dispatch's render. So when the
    ///         acknowledgement arrives, everything sent before it has been dispatched and has rendered.
    ///     </para>
    ///     <para>
    ///         It IS a dispatch, with what every dispatch does: it is counted as one, and it re-checks that the
    ///         principal may still see the page. It is never answered where the server drops handler frames:
    ///         before the hello has attached, and between a sign-in handoff and the reconnect.
    ///     </para>
    /// </remarks>
    public static async Task<List<string>> SettledAsync(
        Func<object, Task> send, Func<TimeSpan, Task<string?>> receive)
    {
        var seq = Interlocked.Decrement(ref _lastSettleSeq);
        await send(new { id = SettleHandlerId, seq });

        var frames = new List<string>();
        var deadline = DateTime.UtcNow + HangCeiling;
        while (deadline - DateTime.UtcNow is var remaining && remaining > TimeSpan.Zero
               && await receive(remaining) is { } frame)
        {
            if (IsAckOf(frame, seq))
            {
                return frames;
            }

            frames.Add(frame);
        }

        throw NeverCame("the acknowledgement that the server had finished", frames);
    }

    /// <summary>The address a frame tells the browser to show, or null when it moves nothing.</summary>
    public static string? HistoryUrl(string frame)
    {
        using var doc = JsonDocument.Parse(frame);
        return doc.RootElement.ValueKind == JsonValueKind.Object
               && doc.RootElement.TryGetProperty("history", out var history)
               && history.TryGetProperty("url", out var url)
            ? url.GetString()
            : null;
    }

    private static bool IsAckOf(string frame, long seq)
    {
        if (!frame.StartsWith("{\"type\":\"ack\"", StringComparison.Ordinal))
        {
            return false;
        }

        using var doc = JsonDocument.Parse(frame);
        return doc.RootElement.GetProperty("seq").GetInt64() == seq;
    }

    private static FailException NeverCame(string what, List<string> instead)
    {
        var got = instead.Count == 0
            ? "Nothing came instead."
            : $"{instead.Count} other frame(s) came instead, the last: {Shorten(instead[^1])}";
        return FailException.ForFailure(
            $"The connection ended, or {HangCeiling.TotalSeconds:0} s passed, without {what}. {got}");
    }

    private static string Shorten(string frame) => frame.Length <= 300 ? frame : frame[..300] + "…";
}
