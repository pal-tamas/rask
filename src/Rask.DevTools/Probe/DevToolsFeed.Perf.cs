using System.Diagnostics;

namespace Rask.DevTools.Probe;

/// <summary>One interaction as the Perf tab lists it: what set it off, and where its time went.</summary>
/// <param name="Sequence">Monotonic per feed, counted with the wire events and commits.</param>
/// <param name="Trigger">The page event that started it (<c>click</c>, <c>input</c>, <c>navigate</c>), or <c>render</c> for
/// a render nothing on the page asked for — a timer, a push, an async handler finishing late.</param>
/// <param name="Target">The type of the component whose handler ran, when one did.</param>
/// <param name="Timestamp">When it started, as a <see cref="Stopwatch" /> timestamp.</param>
/// <param name="HandlerTicks">The handler's own time, without any render it caused while it ran; null when none ran.</param>
/// <param name="Faulted">The handler threw.</param>
/// <param name="RenderTicks">The render walks it caused, added up.</param>
/// <param name="DiffTicks">Working out what to send, added up.</param>
/// <param name="Frames">How many render frames it sent to the page.</param>
/// <param name="Bytes">Their size on the wire, added up.</param>
/// <param name="PatchMilliseconds">How long the page took to apply them, as the page measured it; null until it reports.</param>
internal sealed record DevToolsInteraction(
    long Sequence,
    string Trigger,
    string? Target,
    long Timestamp,
    long? HandlerTicks,
    bool Faulted,
    long RenderTicks,
    long DiffTicks,
    int Frames,
    int Bytes,
    double? PatchMilliseconds)
{
    /// <summary>The app's side of it: handler, render and diff.</summary>
    internal long ServerTicks => (HandlerTicks ?? 0) + RenderTicks + DiffTicks;
}

internal sealed partial class DevToolsFeed
{
    /// <summary>How many interactions a feed keeps; the oldest go first.</summary>
    internal const int InteractionCapacity = 500;

    /// <summary>
    ///     How old a sent frame may be and still take the page's report of applying it. The page and the app keep separate
    ///     clocks, so a report is matched by the frame's size to the oldest waiting frame of that size — and one sent a
    ///     minute ago, which the page applied before any panel was listening, is no longer waiting.
    /// </summary>
    internal static readonly TimeSpan PatchWindow = TimeSpan.FromSeconds(2);

    private readonly Queue<Interaction> _interactions = new();
    private readonly List<(Interaction Interaction, long SentAt, int Bytes)> _unpatched = [];
    private Interaction? _open;
    private string? _lastInbound;

    /// <summary>The page sent a frame of <paramref name="kind" />: a navigation starts an interaction of its own.</summary>
    internal void PerfInbound(string kind, long timestamp)
    {
        lock (_gate)
        {
            _lastInbound = kind;
            if (kind == "navigate")
            {
                Open("navigate", null, timestamp);
            }
        }
    }

    /// <summary>A handler of <paramref name="ownerType" /> started: an interaction named for the event that reached it.</summary>
    internal void PerfHandlerStarted(string ownerType, long timestamp)
    {
        lock (_gate)
        {
            var open = Open(_lastInbound ?? "event", ownerType, timestamp);
            open.HandlerStart = timestamp;
        }
    }

    /// <summary>The handler that started at <paramref name="start" /> finished.</summary>
    internal void PerfHandlerEnded(long start, long end, bool faulted)
    {
        lock (_gate)
        {
            if (_open is { } open && open.HandlerStart == start)
            {
                open.HandlerEnd = end;
                open.HandlerTicks = end - start;
                open.Faulted |= faulted;
            }
        }

        Changed?.Invoke();
    }

    /// <summary>A render walk ran from <paramref name="start" /> to <paramref name="end" />.</summary>
    internal void PerfWalk(long start, long end)
    {
        lock (_gate)
        {
            var open = _open ?? Open("render", null, start);
            open.RenderTicks += end - start;
            // A render inside the handler's own time — an async handler asking for one mid-await — is render time, not
            // handler time.
            if (open.HandlerStart is { } handlerStart && start >= handlerStart && (open.HandlerEnd is not { } handlerEnd || end <= handlerEnd))
            {
                open.RenderInHandlerTicks += end - start;
            }
        }
    }

    /// <summary>The diff for the render just walked took from <paramref name="start" /> to <paramref name="end" />.</summary>
    internal void PerfDiff(long start, long end)
    {
        lock (_gate)
        {
            if (_open is { } open)
            {
                open.DiffTicks += end - start;
            }
        }
    }

    /// <summary>A render frame of <paramref name="bytes" /> went to the page.</summary>
    internal void PerfFrameSent(int bytes, long timestamp)
    {
        lock (_gate)
        {
            var open = _open ?? Open("render", null, timestamp);
            open.Frames++;
            open.Bytes += bytes;
            _unpatched.Add((open, timestamp, bytes));
            if (_unpatched.Count > InteractionCapacity)
            {
                _unpatched.RemoveAt(0);
            }

            // A handler still running may send more; otherwise this frame is what the interaction was for.
            if (open.HandlerStart is null || open.HandlerEnd is not null)
            {
                _open = null;
            }
        }

        Changed?.Invoke();
    }

    /// <summary>
    ///     The page applied a frame of <paramref name="bytes" /> (-1 when it did not see its size) in
    ///     <paramref name="milliseconds" />, reported at <paramref name="now" />.
    /// </summary>
    internal void PerfPatch(double milliseconds, int bytes, long now)
    {
        lock (_gate)
        {
            var oldest = now - (long)(PatchWindow.TotalSeconds * Stopwatch.Frequency);
            _unpatched.RemoveAll(frame => frame.SentAt < oldest);
            var index = _unpatched.FindIndex(frame => bytes < 0 || frame.Bytes == bytes);
            if (index >= 0)
            {
                var interaction = _unpatched[index].Interaction;
                interaction.PatchMilliseconds = (interaction.PatchMilliseconds ?? 0) + milliseconds;
                _unpatched.RemoveAt(index);
            }
        }

        Changed?.Invoke();
    }

    /// <summary>The interactions currently held, oldest first.</summary>
    internal DevToolsInteraction[] InteractionsSnapshot()
    {
        lock (_gate)
        {
            var copy = new DevToolsInteraction[_interactions.Count];
            var i = 0;
            foreach (var item in _interactions)
            {
                copy[i++] = item.ToRecord();
            }

            return copy;
        }
    }

    /// <summary>Forgets every interaction held.</summary>
    internal void ClearInteractions()
    {
        lock (_gate)
        {
            _interactions.Clear();
            _unpatched.Clear();
            _open = null;
        }

        Changed?.Invoke();
    }

    // Starts an interaction, ending whichever was open: one that sent nothing is kept as it was.
    private Interaction Open(string trigger, string? target, long timestamp)
    {
        var item = new Interaction(++_sequence, trigger, target, timestamp);
        _interactions.Enqueue(item);
        if (_interactions.Count > InteractionCapacity)
        {
            _interactions.Dequeue();
        }

        _open = item;
        return item;
    }

    private sealed class Interaction(long sequence, string trigger, string? target, long timestamp)
    {
        internal long? HandlerStart { get; set; }
        internal long? HandlerEnd { get; set; }
        internal long? HandlerTicks { get; set; }
        internal bool Faulted { get; set; }
        internal long RenderTicks { get; set; }
        internal long RenderInHandlerTicks { get; set; }
        internal long DiffTicks { get; set; }
        internal int Frames { get; set; }
        internal int Bytes { get; set; }
        internal double? PatchMilliseconds { get; set; }

        internal DevToolsInteraction ToRecord() => new(
            sequence, trigger, target, timestamp,
            HandlerTicks is { } handler ? Math.Max(0, handler - RenderInHandlerTicks) : null,
            Faulted, RenderTicks, DiffTicks, Frames, Bytes, PatchMilliseconds);
    }
}
