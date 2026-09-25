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
