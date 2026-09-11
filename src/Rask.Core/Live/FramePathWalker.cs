namespace Rask.Core.Live;

/// <summary>
///     Resolves a span of a live render's frame stream to the DOM coordinates the client patches: the path of
///     the parent element, the child slot the span starts at, and how many DOM nodes it produces.
/// </summary>
/// <remarks>
///     <para>
///         Used by Rask DevTools to find the elements a component rendered. User components are transparent in
///         the frame stream — no frame marks where one starts — so the serializer records each component's
///         <c>[start, end)</c> frame span while it walks, and this turns a span into a location afterwards.
///     </para>
///     <para>
///         The slot arithmetic has to agree with <see cref="FrameDiffer" /> exactly, or a highlighted element
///         would be the wrong one: an element, text, raw or doctype frame occupies one slot; an element's leading
///         attribute frames do not; an opaque element's children belong to another renderer and are not
///         entered; any other frame is stepped over without taking a slot.
///     </para>
/// </remarks>
internal static class FramePathWalker
{
    /// <summary>
    ///     Resolves the frames in <paramref name="start" />..<paramref name="end" /> (exclusive).
    /// </summary>
    /// <param name="frames">The whole frame stream of the render, root level first.</param>
    /// <param name="start">Index of the first frame of the span.</param>
    /// <param name="end">Index one past the span's last frame.</param>
    /// <param name="path">Cleared, then filled with the child slots from the document root to the span's parent.</param>
    /// <param name="firstSlot">The parent's child slot the span's first DOM node occupies.</param>
    /// <param name="domNodeCount">How many DOM nodes the span produces at that level.</param>
    /// <returns>
    ///     False when the span cannot be located — outside the stream, empty, starting inside an element's attributes,
    ///     or inside an opaque subtree.
    /// </returns>
    internal static bool TryResolve(
        ReadOnlySpan<RenderFrame> frames, int start, int end, List<int> path, out int firstSlot, out int domNodeCount)
    {
        ArgumentNullException.ThrowIfNull(path);

        path.Clear();
        firstSlot = 0;
        domNodeCount = 0;

        // An empty span is ambiguous, not merely small. A component rendering nothing as the last child of an element
        // gets the element's end index — which is also where the element's next sibling starts — so the stream cannot
        // say whether it sits inside the element or after it. It produced no DOM node to point at either way.
        if (start < 0 || end <= start || end > frames.Length)
        {
            return false;
        }

        var levelStart = 0;
        var levelEnd = frames.Length;

        while (true)
        {
            var index = levelStart;
            var slot = 0;
            var descended = false;

            while (index < levelEnd)
            {
                if (index == start)
                {
                    firstSlot = slot;
                    domNodeCount = CountDomNodes(frames, start, Math.Min(end, levelEnd));
                    return true;
                }

                ref readonly var frame = ref frames[index];
                var length = frame.Kind == RenderFrameKind.Element ? Math.Max(1, frame.SubtreeLength) : 1;

                if (frame.Kind == RenderFrameKind.Element && start > index && start < index + length)
                {
                    if (frame.Opaque)
                    {
                        return false;
                    }

                    path.Add(slot);
                    levelStart = SkipAttributes(frames, index + 1, index + length);
                    levelEnd = index + length;
                    descended = true;
                    break;
                }

                if (OccupiesSlot(frame.Kind))
                {
                    slot++;
                }

                index += length;
            }

            if (descended)
            {
                continue;
            }

            // The whole level was walked without reaching `start` at a frame boundary and without an element containing
            // it. With empty spans rejected up front, that leaves one shape: a span starting inside leading attributes.
            return false;
        }
    }

    private static int CountDomNodes(ReadOnlySpan<RenderFrame> frames, int from, int to)
    {
        var count = 0;
        var index = from;
        while (index < to)
        {
            ref readonly var frame = ref frames[index];
            if (OccupiesSlot(frame.Kind))
            {
                count++;
            }

            index += frame.Kind == RenderFrameKind.Element ? Math.Max(1, frame.SubtreeLength) : 1;
        }

        return count;
    }

    private static int SkipAttributes(ReadOnlySpan<RenderFrame> frames, int from, int end)
    {
        var index = from;
        while (index < end && frames[index].Kind == RenderFrameKind.Attribute)
        {
            index++;
        }

        return index;
    }

    private static bool OccupiesSlot(RenderFrameKind kind) =>
        kind is RenderFrameKind.Element or RenderFrameKind.Text or RenderFrameKind.Raw or RenderFrameKind.Doctype;
}
