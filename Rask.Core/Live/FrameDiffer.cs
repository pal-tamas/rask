namespace Rask.Core.Live;

/// <summary>
///     Kind of edit operation the diff codec emits when comparing two
///     <see cref="RenderFrame" /> streams. Maps to a verb the client interpreter
///     applies to its DOM-mirroring frame stream.
/// </summary>
public enum EditOpKind : byte
{
    /// <summary>Set or replace an attribute's value on the element at
    /// <see cref="EditOp.Path" />. <see cref="EditOp.Name" /> is the attribute name,
    /// <see cref="EditOp.Value" /> is the new value (null for bare attributes).</summary>
    SetAttribute = 1,

    /// <summary>Remove an attribute by name from the element at
    /// <see cref="EditOp.Path" />.</summary>
    RemoveAttribute = 2,

    /// <summary>Replace the text content of the text-or-raw node at
    /// <see cref="EditOp.Path" />.</summary>
    UpdateText = 3,

    /// <summary>Insert a new subtree at <see cref="EditOp.Path" /> (the index of the
    /// slot among the parent's existing DOM children; ops further into the same
    /// parent reference subsequent indices). <see cref="EditOp.Value" /> carries the
    /// pre-serialized HTML fragment for the inserted subtree (set by the codec at
    /// wire-format time once HtmlSerializer captures per-frame byte offsets).</summary>
    InsertSubtree = 4,

    /// <summary>Remove a contiguous run of <see cref="EditOp.Length" /> sibling
    /// subtrees starting at <see cref="EditOp.Path" />.</summary>
    RemoveSubtree = 5
}

/// <summary>
///     A single edit operation produced by <see cref="FrameDiffer.Diff" />. Each op
///     names the DOM node it targets via <see cref="Path" /> — a sequence of child
///     indices from the document root, counting only DOM-relevant nodes (elements,
///     text, raw, doctype). Attribute frames in the underlying render-tree stream
///     are NOT counted; Component frames are transparent (their rendered body
///     contributes siblings at the surrounding level). The path representation lets
///     the client interpreter walk its DOM by simple <c>parent.children[i]</c>
///     descent without needing to mirror the server's frame stream.
/// </summary>
public readonly struct EditOp
{
    public EditOp(EditOpKind kind, int[] path, string? name, string? value, int length = 0)
    {
        Kind = kind;
        Path = path;
        Name = name;
        Value = value;
        Length = length;
    }

    public EditOpKind Kind { get; }

    /// <summary>Child-index sequence from the document root that identifies the
    /// target DOM node (or, for <see cref="EditOpKind.InsertSubtree" /> /
    /// <see cref="EditOpKind.RemoveSubtree" />, the slot among siblings).</summary>
    public int[] Path { get; }

    public string? Name { get; }
    public string? Value { get; }
    public int Length { get; }
}

/// <summary>
///     Compares two <see cref="RenderFrame" /> streams (previous render vs current
///     render) and produces a minimal list of <see cref="EditOp" />s that transforms
///     the previous into the current. Mirrors the role of Blazor's
///     <c>RenderTreeDiffBuilder</c>; simpler because we don't carry sequence numbers
///     from a compile-time source mapping.
/// </summary>
public static class FrameDiffer
{
    /// <summary>
    ///     Walk <paramref name="oldFrames" /> and <paramref name="newFrames" /> together
    ///     producing edit ops into <paramref name="output" />. Returns the number of ops
    ///     written. When the streams are identical, returns 0 without touching the
    ///     output list. When <paramref name="newHtml" /> is supplied,
    ///     <see cref="EditOpKind.InsertSubtree" /> ops carry the HTML fragment to ship
    ///     to the client (sliced from <paramref name="newHtml" /> using each frame's
    ///     <see cref="RenderFrame.HtmlStart" />/<see cref="RenderFrame.HtmlEnd" />);
    ///     otherwise InsertSubtree ops have <c>Value == null</c> and the caller must
    ///     route those payloads through the full-HTML fallback.
    /// </summary>
    public static int Diff(
        ReadOnlySpan<RenderFrame> oldFrames,
        ReadOnlySpan<RenderFrame> newFrames,
        List<EditOp> output,
        string? newHtml = null)
    {
        var startCount = output.Count;
        var path = new List<int>(8);
        DiffSiblings(oldFrames, 0, oldFrames.Length,
                     newFrames, 0, newFrames.Length,
                     path, output, newHtml);
        return output.Count - startCount;
    }

    private static void DiffSiblings(
        ReadOnlySpan<RenderFrame> oldFrames, int oldStart, int oldEnd,
        ReadOnlySpan<RenderFrame> newFrames, int newStart, int newEnd,
        List<int> path,
        List<EditOp> output,
        string? newHtml)
    {
        var oi = oldStart;
        var ni = newStart;
        var domSlot = 0;

        while (oi < oldEnd && ni < newEnd)
        {
            ref readonly var oldFrame = ref oldFrames[oi];
            ref readonly var newFrame = ref newFrames[ni];

            if (!SiblingMatches(oldFrame, newFrame))
            {
                // The roots don't align at this slot. Emit replace ops. The
                // InsertSubtree carries the HTML fragment for the new subtree (when
                // newHtml was supplied) so the client can apply the structural change
                // without re-rendering.
                output.Add(new EditOp(EditOpKind.RemoveSubtree, PathPlus(path, domSlot), null, null,
                    DomNodeCount(oldFrames, oi, oi + oldFrame.SubtreeLength)));
                output.Add(new EditOp(EditOpKind.InsertSubtree, PathPlus(path, domSlot), null,
                    SliceHtml(newHtml, newFrame.HtmlStart, newFrame.HtmlEnd),
                    DomNodeCount(newFrames, ni, ni + newFrame.SubtreeLength)));
                oi += oldFrame.SubtreeLength;
                ni += newFrame.SubtreeLength;
                domSlot++;
                continue;
            }

            switch (oldFrame.Kind)
            {
                case RenderFrameKind.Element:
                {
                    var oldChildStart = oi + 1;
                    var oldChildEnd = oi + oldFrame.SubtreeLength;
                    var newChildStart = ni + 1;
                    var newChildEnd = ni + newFrame.SubtreeLength;

                    // Attributes diff against the current element path.
                    DiffAttributes(oldFrames, ref oldChildStart, oldChildEnd,
                                   newFrames, ref newChildStart, newChildEnd,
                                   PathPlus(path, domSlot), output);

                    // Recurse into children. Push domSlot onto path, then diff inside.
                    path.Add(domSlot);
                    DiffSiblings(oldFrames, oldChildStart, oldChildEnd,
                                 newFrames, newChildStart, newChildEnd,
                                 path, output, newHtml);
                    path.RemoveAt(path.Count - 1);

                    oi += oldFrame.SubtreeLength;
                    ni += newFrame.SubtreeLength;
                    domSlot++;
                    break;
                }

                case RenderFrameKind.Text:
                case RenderFrameKind.Raw:
                    if (!string.Equals(oldFrame.Name, newFrame.Name, StringComparison.Ordinal))
                    {
                        output.Add(new EditOp(EditOpKind.UpdateText, PathPlus(path, domSlot), null, newFrame.Name));
                    }

                    oi++;
                    ni++;
                    domSlot++;
                    break;

                case RenderFrameKind.Doctype:
                    // Doctypes are identical-or-replaced. SiblingMatches gates on Kind
                    // equality (which it is at this branch), so just advance.
                    oi++;
                    ni++;
                    domSlot++;
                    break;

                default:
                    oi++;
                    ni++;
                    break;
            }
        }

        while (oi < oldEnd)
        {
            ref readonly var oldFrame = ref oldFrames[oi];
            output.Add(new EditOp(EditOpKind.RemoveSubtree, PathPlus(path, domSlot), null, null,
                DomNodeCount(oldFrames, oi, oi + oldFrame.SubtreeLength)));
            oi += oldFrame.SubtreeLength;
            // domSlot intentionally NOT advanced — RemoveSubtree shifts subsequent
            // siblings up by one slot in the parent's children list, so the next
            // remove (if any) still targets the current slot index.
        }

        while (ni < newEnd)
        {
            ref readonly var newFrame = ref newFrames[ni];
            output.Add(new EditOp(EditOpKind.InsertSubtree, PathPlus(path, domSlot), null,
                SliceHtml(newHtml, newFrame.HtmlStart, newFrame.HtmlEnd),
                DomNodeCount(newFrames, ni, ni + newFrame.SubtreeLength)));
            ni += newFrame.SubtreeLength;
            domSlot++;
        }
    }

    private static string? SliceHtml(string? newHtml, int start, int end)
    {
        if (newHtml is null || end <= start || (uint)end > (uint)newHtml.Length)
        {
            return null;
        }

        return newHtml.Substring(start, end - start);
    }

    private static void DiffAttributes(
        ReadOnlySpan<RenderFrame> oldFrames, ref int oldCursor, int oldEnd,
        ReadOnlySpan<RenderFrame> newFrames, ref int newCursor, int newEnd,
        int[] elementPath,
        List<EditOp> output)
    {
        var oldAttrStart = oldCursor;
        var newAttrStart = newCursor;
        var oldAttrs = CountLeadingAttributes(oldFrames, oldAttrStart, oldEnd);
        var newAttrs = CountLeadingAttributes(newFrames, newAttrStart, newEnd);

        var common = Math.Min(oldAttrs, newAttrs);
        for (var k = 0; k < common; k++)
        {
            var oldAttr = oldFrames[oldAttrStart + k];
            var newAttr = newFrames[newAttrStart + k];
            if (!string.Equals(oldAttr.Name, newAttr.Name, StringComparison.Ordinal))
            {
                output.Add(new EditOp(EditOpKind.RemoveAttribute, elementPath, oldAttr.Name, null));
                output.Add(new EditOp(EditOpKind.SetAttribute, elementPath, newAttr.Name, newAttr.Value));
            }
            else if (!string.Equals(oldAttr.Value, newAttr.Value, StringComparison.Ordinal))
            {
                output.Add(new EditOp(EditOpKind.SetAttribute, elementPath, newAttr.Name, newAttr.Value));
            }
        }

        for (var k = common; k < oldAttrs; k++)
        {
            output.Add(new EditOp(EditOpKind.RemoveAttribute, elementPath, oldFrames[oldAttrStart + k].Name, null));
        }

        for (var k = common; k < newAttrs; k++)
        {
            var added = newFrames[newAttrStart + k];
            output.Add(new EditOp(EditOpKind.SetAttribute, elementPath, added.Name, added.Value));
        }

        oldCursor = oldAttrStart + oldAttrs;
        newCursor = newAttrStart + newAttrs;
    }

    private static int CountLeadingAttributes(ReadOnlySpan<RenderFrame> frames, int start, int end)
    {
        var count = 0;
        for (var i = start; i < end && frames[i].Kind == RenderFrameKind.Attribute; i++)
        {
            count++;
        }

        return count;
    }

    private static bool SiblingMatches(RenderFrame a, RenderFrame b)
    {
        if (a.Kind != b.Kind)
        {
            return false;
        }

        return a.Kind switch
        {
            RenderFrameKind.Element => string.Equals(a.Name, b.Name, StringComparison.Ordinal),
            _ => true
        };
    }

    private static int[] PathPlus(List<int> basePath, int slot)
    {
        // Allocates a fresh int[] per op so consumers can hold onto the path safely.
        // Size is small (typical depth < 10), and ops are the wire bytes we're saving
        // — a 10-int array is dwarfed by the 50 KB body it replaces.
        var arr = new int[basePath.Count + 1];
        for (var i = 0; i < basePath.Count; i++)
        {
            arr[i] = basePath[i];
        }

        arr[basePath.Count] = slot;
        return arr;
    }

    private static int DomNodeCount(ReadOnlySpan<RenderFrame> frames, int start, int end)
    {
        // Number of DOM-structural sibling nodes spanned by frames[start..end). Used
        // to size RemoveSubtree.Length (# of consecutive siblings to remove on the
        // client). The body of the first frame counts as one (it IS one DOM node);
        // we only count siblings at depth = 0 within the range.
        var count = 0;
        var i = start;
        while (i < end)
        {
            var kind = frames[i].Kind;
            if (kind != RenderFrameKind.Attribute)
            {
                count++;
            }

            i += frames[i].SubtreeLength;
        }

        return count;
    }
}
