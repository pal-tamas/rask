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
    RemoveSubtree = 5,

    /// <summary>Move an existing sibling DOM node within its parent. <see cref="EditOp.Path" />
    /// resolves to the destination slot among the parent's DOM-relevant children;
    /// <see cref="EditOp.Length" /> is the source slot. The client detaches the node at the
    /// source, then inserts at the destination slot in the post-detach sibling list — both
    /// indexes are computed against the live DOM as it stands when this op runs (with any
    /// preceding ops already applied). Preserves DOM identity (focus, IDL property state, event
    /// listeners, iframe document state) since moving an existing node via
    /// <c>parent.insertBefore</c> doesn't materialise a new element.</summary>
    MoveSubtree = 6
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
    public EditOp(EditOpKind kind, int[] path, string? name, string? value, int length = 0, bool trusted = false)
    {
        Kind = kind;
        Path = path;
        Name = name;
        Value = value;
        Length = length;
        Trusted = trusted;
    }

    public EditOpKind Kind { get; }

    /// <summary>Child-index sequence from the document root that identifies the
    /// target DOM node (or, for <see cref="EditOpKind.InsertSubtree" /> /
    /// <see cref="EditOpKind.RemoveSubtree" /> / <see cref="EditOpKind.MoveSubtree" />,
    /// the slot among siblings).</summary>
    public int[] Path { get; }

    public string? Name { get; }
    public string? Value { get; }
    public int Length { get; }

    /// <summary>True when this structural op was produced by the keyed-matching path
    /// (where the moved/inserted/removed node is identified by <c>data-rask-key</c>, so the
    /// surrounding morph-baseline DOM state stays consistent under apply). Positional structural
    /// ops set this to <c>false</c> and the live-session gates route them through the full-HTML
    /// morph path. Non-structural ops (SetAttribute, RemoveAttribute, UpdateText) ignore the
    /// flag — they're always safe to ship.</summary>
    public bool Trusted { get; }
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
        => Diff(oldFrames, newFrames, output, out _, newHtml);

    /// <summary>
    ///     Variant that also reports whether the keyed-matching path was used at any depth.
    ///     The live-session gates route on this so structural ops produced by keyed matching
    ///     (where the moved/inserted/removed node has a stable <c>data-rask-key</c> identity)
    ///     can ship as diff while positional structural ops still route to the full-HTML morph
    ///     path. See <see cref="EditOp.Trusted" /> for the per-op marker that carries the same
    ///     signal to <c>DiffOpsAreClientSupported</c>.
    /// </summary>
    public static int Diff(
        ReadOnlySpan<RenderFrame> oldFrames,
        ReadOnlySpan<RenderFrame> newFrames,
        List<EditOp> output,
        out bool usedKeyedPath,
        string? newHtml = null)
    {
        var startCount = output.Count;
        var path = new List<int>(8);
        var ctx = new DiffContext();
        DiffSiblings(oldFrames, 0, oldFrames.Length,
                     newFrames, 0, newFrames.Length,
                     path, output, newHtml, ctx);
        usedKeyedPath = ctx.UsedKeyedPath;
        return output.Count - startCount;
    }

    private sealed class DiffContext
    {
        public bool UsedKeyedPath;
    }

    private static void DiffSiblings(
        ReadOnlySpan<RenderFrame> oldFrames, int oldStart, int oldEnd,
        ReadOnlySpan<RenderFrame> newFrames, int newStart, int newEnd,
        List<int> path,
        List<EditOp> output,
        string? newHtml,
        DiffContext ctx)
    {
        // Keyed matching kicks in only when every child on BOTH sides is a keyed
        // Element. A single unkeyed child, or any non-Element sibling (text/raw/doctype)
        // mixed with elements, or a duplicate key on either side, falls back to the
        // positional walk below. This mirrors the morph engine's all-or-nothing keyed
        // reconciliation in Rask.Core/Resources/rask-morph.js — same parents that the
        // morph treats as keyed get the keyed diff path, no surprise divergence.
        if (TryCollectKeyedChildren(oldFrames, oldStart, oldEnd, out var oldKids)
            && TryCollectKeyedChildren(newFrames, newStart, newEnd, out var newKids))
        {
            ctx.UsedKeyedPath = true;
            DiffKeyedSiblings(oldFrames, oldKids!, newFrames, newKids!, path, output, newHtml, ctx);
            return;
        }


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
                                 path, output, newHtml, ctx);
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

    private struct KeyedChild
    {
        public string Key;
        public int FrameIndex;
    }

    /// <summary>
    ///     Probes a sibling range for the all-or-nothing keyed contract: every direct child
    ///     must be an Element carrying a <c>data-rask-key</c> attribute, and the keys must be
    ///     unique within the range. Returns the ordered child list when the contract holds;
    ///     otherwise <c>false</c> and the caller falls back to the positional walk. Mirrors
    ///     the morph engine's keyed-children probe in <c>Rask.Core/Resources/rask-morph.js</c>
    ///     so parents that the morph reconciles by key get the same treatment in the diff
    ///     codec — same definition of "keyed list" on both sides.
    /// </summary>
    private static bool TryCollectKeyedChildren(
        ReadOnlySpan<RenderFrame> frames, int start, int end, out List<KeyedChild>? children)
    {
        children = null;
        if (start >= end)
        {
            return false;
        }

        HashSet<string>? seen = null;
        List<KeyedChild>? buffer = null;
        var i = start;
        while (i < end)
        {
            ref readonly var f = ref frames[i];
            if (f.Kind == RenderFrameKind.Attribute)
            {
                // Stray attribute frame at the sibling boundary (shouldn't happen
                // after CountLeadingAttributes consumes them inside DiffAttributes,
                // but defensive — bail to positional).
                return false;
            }

            if (f.Kind != RenderFrameKind.Element)
            {
                // Mixed content (text/raw/doctype as a sibling) — the morph engine
                // treats this as unkeyed, so we do too.
                return false;
            }

            var key = ExtractRaskKey(frames, i, end);
            if (key is null)
            {
                return false;
            }

            seen ??= new HashSet<string>(StringComparer.Ordinal);
            if (!seen.Add(key))
            {
                // Duplicate keys in the same sibling list — diagnostic-worthy but we
                // fall back to positional rather than guessing which one to match.
                return false;
            }

            buffer ??= new List<KeyedChild>();
            buffer.Add(new KeyedChild { Key = key, FrameIndex = i });

            i += f.SubtreeLength;
        }

        if (buffer is null)
        {
            return false;
        }

        children = buffer;
        return true;
    }

    private static string? ExtractRaskKey(ReadOnlySpan<RenderFrame> frames, int elementIndex, int end)
    {
        // The serializer emits attribute frames in the order WriteAttributes(sb) wrote
        // them — id, class, style, then data-* (one frame per data-key entry), then any
        // tag-specific attributes. data-rask-key is just one of the data-* entries, so
        // we have to walk all leading Attribute frames rather than peek at a known slot.
        for (var i = elementIndex + 1; i < end; i++)
        {
            ref readonly var af = ref frames[i];
            if (af.Kind != RenderFrameKind.Attribute)
            {
                return null;
            }

            if (string.Equals(af.Name, "data-rask-key", StringComparison.Ordinal))
            {
                return af.Value;
            }
        }

        return null;
    }

    private static void DiffKeyedSiblings(
        ReadOnlySpan<RenderFrame> oldFrames, List<KeyedChild> oldKids,
        ReadOnlySpan<RenderFrame> newFrames, List<KeyedChild> newKids,
        List<int> path, List<EditOp> output, string? newHtml, DiffContext ctx)
    {
        var oldByKey = new Dictionary<string, int>(oldKids.Count, StringComparer.Ordinal);
        for (var i = 0; i < oldKids.Count; i++)
        {
            oldByKey[oldKids[i].Key] = i;
        }

        var newByKey = new Dictionary<string, int>(newKids.Count, StringComparer.Ordinal);
        for (var j = 0; j < newKids.Count; j++)
        {
            newByKey[newKids[j].Key] = j;
        }

        // 1) REMOVES. Walk old right-to-left so each emitted slot stays valid (a remove
        //    shifts subsequent siblings left, but we never visit those again).
        for (var i = oldKids.Count - 1; i >= 0; i--)
        {
            var oc = oldKids[i];
            if (newByKey.ContainsKey(oc.Key))
            {
                continue;
            }

            ref readonly var elem = ref oldFrames[oc.FrameIndex];
            output.Add(new EditOp(
                EditOpKind.RemoveSubtree,
                PathPlus(path, i),
                null,
                null,
                DomNodeCount(oldFrames, oc.FrameIndex, oc.FrameIndex + elem.SubtreeLength),
                trusted: true));
        }

        // 2) Build tracking list = old children whose keys survived. Note we work with
        //    the KeyedChild structs (so we can look up frame data later) — the "current
        //    slot index" is the index within this list as it mutates during steps 3-4.
        var surviving = new List<KeyedChild>(oldKids.Count);
        foreach (var oc in oldKids)
        {
            if (newByKey.ContainsKey(oc.Key))
            {
                surviving.Add(oc);
            }
        }

        // 3) INSERTS. Walk new left-to-right; emit InsertSubtree for keys not in old, at
        //    the new position. Each insert into `surviving` shifts the tracking indexes
        //    of everything after it — exactly mirrors what the client does to its DOM.
        for (var j = 0; j < newKids.Count; j++)
        {
            var nc = newKids[j];
            if (oldByKey.ContainsKey(nc.Key))
            {
                continue;
            }

            ref readonly var elem = ref newFrames[nc.FrameIndex];
            var html = SliceHtml(newHtml, elem.HtmlStart, elem.HtmlEnd);
            output.Add(new EditOp(
                EditOpKind.InsertSubtree,
                PathPlus(path, j),
                null,
                html,
                DomNodeCount(newFrames, nc.FrameIndex, nc.FrameIndex + elem.SubtreeLength),
                trusted: true));
            surviving.Insert(j, nc);
        }

        // 4) MOVES. `surviving` and `newKids` now hold the same key set; compute the
        //    permutation (target[i] = new position of surviving[i]) and find its longest
        //    increasing subsequence. Elements ON the LIS are already in the right relative
        //    order — they stay put. Elements OFF the LIS need to move. This is the same
        //    minimal-moves strategy React's reconciler uses; for our two benchmark
        //    scenarios it emits 2 moves on KeyedList100Reorder and 0 on DeleteMiddleRow.
        var n = surviving.Count;
        if (n > 0)
        {
            var targets = new int[n];
            for (var i = 0; i < n; i++)
            {
                targets[i] = newByKey[surviving[i].Key];
            }

            var lis = ComputeLisIndexSet(targets);

            // Collect (key, targetSlot) for non-LIS elements, sorted ascending by target.
            // The walk order matters: applying moves dst-ascending keeps the tracking
            // indexes consistent with what the client sees (the post-detach refNode lookup
            // on the client uses the same shifted indices).
            List<(string Key, int Target)>? moveable = null;
            for (var i = 0; i < n; i++)
            {
                if (lis.Contains(i))
                {
                    continue;
                }

                moveable ??= new List<(string, int)>();
                moveable.Add((surviving[i].Key, targets[i]));
            }

            if (moveable is { Count: > 0 })
            {
                moveable.Sort(static (a, b) => a.Target.CompareTo(b.Target));

                foreach (var (key, target) in moveable)
                {
                    var src = -1;
                    for (var i = 0; i < surviving.Count; i++)
                    {
                        if (string.Equals(surviving[i].Key, key, StringComparison.Ordinal))
                        {
                            src = i;
                            break;
                        }
                    }

                    if (src < 0 || src == target)
                    {
                        continue;
                    }

                    output.Add(new EditOp(
                        EditOpKind.MoveSubtree,
                        PathPlus(path, target),
                        null,
                        null,
                        src,
                        trusted: true));

                    var moved = surviving[src];
                    surviving.RemoveAt(src);
                    surviving.Insert(target, moved);
                }
            }
        }

        // 5) INNER DIFFS for kept elements. Path uses the NEW slot index, so the client
        //    walks its post-permutation DOM at the same coordinate. Note `surviving` may
        //    have been mutated by Moves above and could now hold InsertSubtree-only nodes
        //    in slots that originally weren't kept — we recurse against the ORIGINAL
        //    keyed match (`oldByKey[nc.Key]`), not against `surviving[j]`.
        for (var j = 0; j < newKids.Count; j++)
        {
            var nc = newKids[j];
            if (!oldByKey.TryGetValue(nc.Key, out var oldIdx))
            {
                continue;
            }

            var oc = oldKids[oldIdx];
            ref readonly var oldElem = ref oldFrames[oc.FrameIndex];
            ref readonly var newElem = ref newFrames[nc.FrameIndex];

            if (!string.Equals(oldElem.Name, newElem.Name, StringComparison.Ordinal))
            {
                // Same key, different tag (e.g., user replaced <li data-rask-key="3"> with
                // <div data-rask-key="3">). Treat as a fresh node: remove the old, insert
                // the new at slot j. The earlier remove-and-insert passes wouldn't have
                // covered this because the key IS in both maps.
                output.Add(new EditOp(
                    EditOpKind.RemoveSubtree,
                    PathPlus(path, j),
                    null,
                    null,
                    DomNodeCount(oldFrames, oc.FrameIndex, oc.FrameIndex + oldElem.SubtreeLength),
                    trusted: true));
                output.Add(new EditOp(
                    EditOpKind.InsertSubtree,
                    PathPlus(path, j),
                    null,
                    SliceHtml(newHtml, newElem.HtmlStart, newElem.HtmlEnd),
                    DomNodeCount(newFrames, nc.FrameIndex, nc.FrameIndex + newElem.SubtreeLength),
                    trusted: true));
                continue;
            }

            var oldChildStart = oc.FrameIndex + 1;
            var oldChildEnd = oc.FrameIndex + oldElem.SubtreeLength;
            var newChildStart = nc.FrameIndex + 1;
            var newChildEnd = nc.FrameIndex + newElem.SubtreeLength;

            DiffAttributes(oldFrames, ref oldChildStart, oldChildEnd,
                           newFrames, ref newChildStart, newChildEnd,
                           PathPlus(path, j), output);

            path.Add(j);
            DiffSiblings(oldFrames, oldChildStart, oldChildEnd,
                         newFrames, newChildStart, newChildEnd,
                         path, output, newHtml, ctx);
            path.RemoveAt(path.Count - 1);
        }
    }

    /// <summary>
    ///     Returns the set of indexes in <paramref name="arr" /> that form a longest strictly
    ///     increasing subsequence. O(n²) — fine for the typical 10–500-row keyed lists we
    ///     diff; a binary-search variant would drop us to O(n log n) but isn't load-bearing
    ///     for the benchmark sizes we care about today.
    /// </summary>
    // Patience-sorting LIS in O(N log N). `tails[k]` holds the arr-index of the smallest
    // tail value among all increasing subsequences of length k+1 seen so far; binary search
    // finds where each new element extends or replaces. `prev[i]` chains back to reconstruct.
    // Returns the set of arr indexes that belong to one optimal LIS — same length as the
    // previous O(N²) DP, but any LIS of optimal length produces the same move count for
    // FrameDiffer's keyed reorder (the only consumer), so the choice between ties is benign.
    internal static HashSet<int> ComputeLisIndexSet(int[] arr)
    {
        var n = arr.Length;
        var result = new HashSet<int>();
        if (n == 0)
        {
            return result;
        }

        var tails = new int[n];   // tails[k] = arr-index of smallest tail of LIS length k+1
        var prev = new int[n];    // prev[i] = arr-index of LIS predecessor of i (or -1)
        var tailsLen = 0;

        for (var i = 0; i < n; i++)
        {
            var x = arr[i];

            // Binary-search tails[0..tailsLen) for the first slot whose value is >= x.
            var lo = 0;
            var hi = tailsLen;
            while (lo < hi)
            {
                var mid = (lo + hi) >> 1;
                if (arr[tails[mid]] < x)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            prev[i] = lo > 0 ? tails[lo - 1] : -1;
            tails[lo] = i;
            if (lo == tailsLen)
            {
                tailsLen++;
            }
        }

        var cur = tails[tailsLen - 1];
        while (cur >= 0)
        {
            result.Add(cur);
            cur = prev[cur];
        }

        return result;
    }
}
