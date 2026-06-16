using System.Buffers;
using System.Diagnostics;

namespace Rask.Core.Live;

/// <summary>
///     Kind of edit operation the diff codec emits when comparing two
///     <see cref="RenderFrame" /> streams. Maps to a verb the client interpreter
///     applies to its DOM-mirroring frame stream.
/// </summary>
public enum EditOpKind : byte
{
    /// <summary>
    ///     Set or replace an attribute's value on the element at
    ///     <see cref="EditOp.Path" />. <see cref="EditOp.Name" /> is the attribute name,
    ///     <see cref="EditOp.Value" /> is the new value (null for bare attributes).
    /// </summary>
    SetAttribute = 1,

    /// <summary>
    ///     Remove an attribute by name from the element at
    ///     <see cref="EditOp.Path" />.
    /// </summary>
    RemoveAttribute = 2,

    /// <summary>
    ///     Replace the text content of the text-or-raw node at
    ///     <see cref="EditOp.Path" />.
    /// </summary>
    UpdateText = 3,

    /// <summary>
    ///     Insert a new subtree at <see cref="EditOp.Path" /> (the index of the
    ///     slot among the parent's existing DOM children; ops further into the same
    ///     parent reference subsequent indices). <see cref="EditOp.Value" /> carries the
    ///     pre-serialized HTML fragment for the inserted subtree (set by the codec at
    ///     wire-format time once HtmlSerializer captures per-frame byte offsets).
    /// </summary>
    InsertSubtree = 4,

    /// <summary>
    ///     Remove a contiguous run of <see cref="EditOp.Length" /> sibling
    ///     subtrees starting at <see cref="EditOp.Path" />.
    /// </summary>
    RemoveSubtree = 5,

    /// <summary>
    ///     Move an existing sibling DOM node within its parent. <see cref="EditOp.Path" />
    ///     resolves to the destination slot among the parent's DOM-relevant children;
    ///     <see cref="EditOp.Length" /> is the source slot. The client detaches the node at the
    ///     source, then inserts at the destination slot in the post-detach sibling list — both
    ///     indexes are computed against the live DOM as it stands when this op runs (with any
    ///     preceding ops already applied). Preserves DOM identity (focus, IDL property state, event
    ///     listeners, iframe document state) since moving an existing node via
    ///     <c>parent.insertBefore</c> doesn't materialise a new element.
    /// </summary>
    MoveSubtree = 6,

    /// <summary>
    ///     A batch of sibling moves under a single keyed parent. <see cref="EditOp.Path" />
    ///     resolves to the shared parent node; <see cref="EditOp.Moves" /> is a flat
    ///     <c>[dst0, src0, dst1, src1, …]</c> array, replayed in order with identical semantics to a
    ///     run of <see cref="MoveSubtree" /> ops (detach the source slot, then insert at the
    ///     destination slot in the post-detach sibling list). The order is load-bearing: each
    ///     dst/src pair is computed against the live DOM as mutated by all preceding pairs in the
    ///     batch, so it must not be reordered. Collapses N per-row moves (each re-emitting the full
    ///     parent path) into one op + one path — the dominant wire-bytes cost of a keyed-list
    ///     reorder. Like <see cref="MoveSubtree" /> it only ever comes from the keyed path and
    ///     preserves DOM identity.
    /// </summary>
    PermutationBatch = 7
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
    public EditOp(EditOpKind kind, int[] path, string? name, string? value, int length = 0, bool trusted = false,
        int[]? moves = null)
    {
        Kind = kind;
        Path = path;
        Name = name;
        Value = value;
        Length = length;
        Trusted = trusted;
        Moves = moves;
    }

    public EditOpKind Kind { get; }

    /// <summary>
    ///     Child-index sequence from the document root that identifies the
    ///     target DOM node (or, for <see cref="EditOpKind.InsertSubtree" /> /
    ///     <see cref="EditOpKind.RemoveSubtree" /> / <see cref="EditOpKind.MoveSubtree" />,
    ///     the slot among siblings).
    /// </summary>
    public int[] Path { get; }

    public string? Name { get; }
    public string? Value { get; }
    public int Length { get; }

    /// <summary>
    ///     For <see cref="EditOpKind.PermutationBatch" /> only: a flat
    ///     <c>[dst0, src0, dst1, src1, …]</c> array of sibling moves under the parent at
    ///     <see cref="Path" />, in apply order. Null for every other op kind.
    /// </summary>
    public int[]? Moves { get; }

    /// <summary>
    ///     True when this structural op was produced by the keyed-matching path
    ///     (where the moved/inserted/removed node is identified by <c>data-rask-key</c>, so the
    ///     surrounding morph-baseline DOM state stays consistent under apply). Positional structural
    ///     ops set this to <c>false</c> and the live-session gates route them through the full-HTML
    ///     morph path. Non-structural ops (SetAttribute, RemoveAttribute, UpdateText) ignore the
    ///     flag — they're always safe to ship.
    /// </summary>
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
        => Diff(oldFrames, newFrames, output, new DiffScratch(), out usedKeyedPath, newHtml);

    /// <summary>
    ///     Scratch-pooled variant. <paramref name="scratch" /> carries the reusable
    ///     collections the keyed-reconciliation path would otherwise allocate per render
    ///     (key maps, surviving/live lists, the LIS set, and the per-parent child buffers).
    ///     A long-lived <see cref="DiffScratch" /> owned per session (see
    ///     <c>SessionRenderCache</c>) makes the steady-state keyed diff allocation-free;
    ///     callers without one use the parameterless overloads, which allocate a transient
    ///     scratch per call — unchanged behaviour for tests and one-shot callers.
    /// </summary>
    public static int Diff(
        ReadOnlySpan<RenderFrame> oldFrames,
        ReadOnlySpan<RenderFrame> newFrames,
        List<EditOp> output,
        DiffScratch scratch,
        out bool usedKeyedPath,
        string? newHtml = null)
    {
        var startCount = output.Count;
        scratch.ResetForDiff();
        DiffSiblings(oldFrames, 0, oldFrames.Length,
            newFrames, 0, newFrames.Length,
            output, newHtml, scratch);
        usedKeyedPath = scratch.UsedKeyedPath;
        return output.Count - startCount;
    }

    private static void DiffSiblings(
        ReadOnlySpan<RenderFrame> oldFrames, int oldStart, int oldEnd,
        ReadOnlySpan<RenderFrame> newFrames, int newStart, int newEnd,
        List<EditOp> output,
        string? newHtml,
        DiffScratch scratch)
    {
        var path = scratch.Path;

        // Keyed matching kicks in only when every child on BOTH sides is a keyed
        // Element. A single unkeyed child, or any non-Element sibling (text/raw/doctype)
        // mixed with elements, or a duplicate key on either side, falls back to the
        // positional walk below. This mirrors the morph engine's all-or-nothing keyed
        // reconciliation in Rask.Core/Resources/rask-morph.js — same parents that the
        // morph treats as keyed get the keyed diff path, no surprise divergence.
        //
        // The keyed buffers (key maps + child lists) come from a pooled bundle that stays
        // live through DiffKeyedSiblings' inner-diff recursion, so it's returned only after
        // that call completes; the positional fallback returns it immediately.
        var bundle = scratch.RentBundle();
        if (TryCollectKeyedChildren(oldFrames, oldStart, oldEnd, bundle.OldKids, bundle.Seen))
        {
            bundle.Seen.Clear();
            if (TryCollectKeyedChildren(newFrames, newStart, newEnd, bundle.NewKids, bundle.Seen))
            {
                scratch.UsedKeyedPath = true;
                try
                {
                    DiffKeyedSiblings(oldFrames, newFrames, output, newHtml, scratch, bundle);
                }
                finally
                {
                    scratch.ReturnBundle(bundle);
                }

                return;
            }
        }

        scratch.ReturnBundle(bundle);


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
                        output, newHtml, scratch);
                    path.RemoveAt(path.Count - 1);

                    oi += oldFrame.SubtreeLength;
                    ni += newFrame.SubtreeLength;
                    domSlot++;
                    break;
                }

                case RenderFrameKind.Text:
                    if (!string.Equals(oldFrame.Name, newFrame.Name, StringComparison.Ordinal))
                    {
                        output.Add(new EditOp(EditOpKind.UpdateText, PathPlus(path, domSlot), null, newFrame.Name));
                    }

                    oi++;
                    ni++;
                    domSlot++;
                    break;

                case RenderFrameKind.Raw:
                    // Only equal-valued Raw frames reach here — a changed Raw is a SiblingMatches
                    // non-match (see SiblingMatches) and already shipped as Remove+Insert above, so
                    // there is nothing to patch in place. Advance past the (single) Raw frame and
                    // its DOM slot to keep the positional walk aligned.
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

        // Name-keyed reconcile — NOT positional. Attributes can be conditionally present
        // (e.g. `checked` on a checkbox appears/disappears as it toggles, and it's emitted
        // mid-list before data-rask-on-change), which shifts the index of every following
        // attribute. A positional walk then mis-pairs names across the shift and emits ops
        // that rename/clobber unrelated attributes — observed as a toggling checkbox losing
        // its data-rask-on-change handler (so it stops responding) and gaining a spurious
        // value="". HTML attribute order carries no meaning and the client morph path keys
        // attributes by name, so we match by name here too: O(n*m) over the handful of
        // attributes an element carries.
        for (var k = 0; k < newAttrs; k++)
        {
            var na = newFrames[newAttrStart + k];
            var oldValue = FindAttribute(oldFrames, oldAttrStart, oldAttrs, na.Name, out var inOld);
            if (!inOld || !string.Equals(oldValue, na.Value, StringComparison.Ordinal))
            {
                output.Add(new EditOp(EditOpKind.SetAttribute, elementPath, na.Name, na.Value));
            }
        }

        for (var k = 0; k < oldAttrs; k++)
        {
            var oa = oldFrames[oldAttrStart + k];
            FindAttribute(newFrames, newAttrStart, newAttrs, oa.Name, out var inNew);
            if (!inNew)
            {
                output.Add(new EditOp(EditOpKind.RemoveAttribute, elementPath, oa.Name, null));
            }
        }

        oldCursor = oldAttrStart + oldAttrs;
        newCursor = newAttrStart + newAttrs;
    }

    private static string? FindAttribute(
        ReadOnlySpan<RenderFrame> frames, int attrStart, int attrCount, string? name, out bool found)
    {
        for (var i = 0; i < attrCount; i++)
        {
            ref readonly var f = ref frames[attrStart + i];
            if (string.Equals(f.Name, name, StringComparison.Ordinal))
            {
                found = true;
                return f.Value;
            }
        }

        found = false;
        return null;
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
            // A Raw frame's verbatim markup parses into a *variable-length* run of sibling DOM
            // nodes with no boundary the client can address by path, so a changed Raw cannot be
            // patched in place: UpdateText would both HTML-escape the markup (showing literal
            // <span> tags) and only touch the run's first node. Treat differing Raw values as a
            // non-match so the value change ships as an untrusted Remove+Insert, which routes to
            // the full-HTML morph (LiveDiffGate.DiffOpsAreClientSupported) — the morph reparses
            // the new markup correctly. Equal Raw values match and produce no op.
            RenderFrameKind.Raw => string.Equals(a.Name, b.Name, StringComparison.Ordinal),
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
        ReadOnlySpan<RenderFrame> frames, int start, int end, List<KeyedChild> children, HashSet<string> seen)
    {
        // Fills the caller-supplied (pooled) buffers rather than allocating. On a false
        // return the partial contents are discarded by the caller (ReturnBundle clears the
        // whole bundle), so callers must not rely on them.
        if (start >= end)
        {
            return false;
        }

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

            if (!seen.Add(key))
            {
                // Duplicate keys in the same sibling list — diagnostic-worthy but we
                // fall back to positional rather than guessing which one to match.
                return false;
            }

            children.Add(new KeyedChild { Key = key, FrameIndex = i });

            i += f.SubtreeLength;
        }

        return children.Count > 0;
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
        ReadOnlySpan<RenderFrame> oldFrames,
        ReadOnlySpan<RenderFrame> newFrames,
        List<EditOp> output, string? newHtml,
        DiffScratch scratch, KeyedBundle bundle)
    {
        // All working collections come from the pooled bundle (cleared on rent), so a keyed
        // diff allocates nothing here beyond the EditOp path arrays (intentional wire data)
        // and the two ArrayPool-rented int buffers in the MOVES step below.
        var path = scratch.Path;
        var oldKids = bundle.OldKids;
        var newKids = bundle.NewKids;

        var oldByKey = bundle.OldByKey;
        for (var i = 0; i < oldKids.Count; i++)
        {
            oldByKey[oldKids[i].Key] = i;
        }

        var newByKey = bundle.NewByKey;
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
                true));
        }

        // 2) Build tracking list = old children whose keys survived. Note we work with
        //    the KeyedChild structs (so we can look up frame data later) — the "current
        //    slot index" is the index within this list as it mutates during steps 3-4.
        var surviving = bundle.Surviving;
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
                true));
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
            // `targets` and `newIndexToSurv` are size-n scratch permutations used only within
            // this step (before the step-5 recursion), so they come from ArrayPool — rented
            // oversized, hence every loop bounds on `n`, never `.Length`. `lis` and `live`
            // reuse the bundle's set/list.
            var targets = ArrayPool<int>.Shared.Rent(n);
            var newIndexToSurv = ArrayPool<int>.Shared.Rent(n);
            try
            {
                for (var i = 0; i < n; i++)
                {
                    targets[i] = newByKey[surviving[i].Key];
                }

                var lis = bundle.LisSet;
                ComputeLisIndexSet(targets, n, lis);

                // Elements ON the LIS are already in the right relative order — they never move.
                // Everything else must be repositioned. We walk the NEW indices RIGHT-TO-LEFT and
                // move each off-LIS element to sit immediately before the element at the next new
                // index (its "anchor"). Going right-to-left, that anchor is already in its final
                // slot, so anchoring against it places the moved node correctly — this is the
                // standard correct minimal-move reconcile (Vue/Inferno).
                //
                // The earlier implementation walked target-ascending and inserted each node at its
                // numeric target index in the mutating list. That is WRONG for permutations needing
                // 3+ moves: insert-at-numeric-target does not account for the unmoved (LIS) backbone
                // the nodes must weave around, so the resulting DOM order was incorrect. It went
                // unnoticed because the only keyed-move test asserted op *count*, never the order.
                //
                // newIndexToSurv[j] = surviving-index whose new position is j. `targets` is a
                // permutation of 0..n-1 (surviving and newKids share the same key set), so it is
                // invertible.
                for (var i = 0; i < n; i++)
                {
                    newIndexToSurv[targets[i]] = i;
                }

                // `live` holds surviving-indices in current DOM order. Steps 1-3 already aligned
                // `surviving` to the post-insert order, so this starts as 0..n-1 and we mutate it
                // in lockstep with the moves the client will apply (detach at src, insert at dst).
                var live = bundle.Live;
                for (var i = 0; i < n; i++)
                {
                    live.Add(i);
                }

                // Accumulate the run's moves as flat [dst0, src0, dst1, src1, …] pairs instead of
                // emitting one MoveSubtree op each — every op would re-emit the full parent path,
                // which dominates the wire bytes of a large reorder. After the loop we ship a single
                // PermutationBatch op carrying the shared parent path once. The flat array MUST stay
                // in emission order: each dst/src is computed against `live` as mutated by all
                // preceding pairs, so the client replays it left-to-right (see EditOpKind docs).
                var moves = bundle.MovesBuffer;
                for (var j = n - 1; j >= 0; j--)
                {
                    var id = newIndexToSurv[j];
                    if (lis.Contains(id))
                    {
                        continue;
                    }

                    var src = live.IndexOf(id);
                    live.RemoveAt(src);

                    // Destination = current (post-detach) index of the anchor at new index j+1,
                    // or the end of the list when this is the last new index (no anchor).
                    var dst = j + 1 < n ? live.IndexOf(newIndexToSurv[j + 1]) : live.Count;

                    if (dst != src)
                    {
                        moves.Add(dst);
                        moves.Add(src);
                    }

                    // Re-insert even on the no-op (dst == src) case so `live` stays consistent for
                    // the remaining iterations' src/dst lookups.
                    live.Insert(dst, id);
                }

                if (moves.Count > 0)
                {
                    output.Add(new EditOp(
                        EditOpKind.PermutationBatch,
                        path.ToArray(),
                        null,
                        null,
                        trusted: true,
                        moves: moves.ToArray()));
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(targets);
                ArrayPool<int>.Shared.Return(newIndexToSurv);
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
                    true));
                output.Add(new EditOp(
                    EditOpKind.InsertSubtree,
                    PathPlus(path, j),
                    null,
                    SliceHtml(newHtml, newElem.HtmlStart, newElem.HtmlEnd),
                    DomNodeCount(newFrames, nc.FrameIndex, nc.FrameIndex + newElem.SubtreeLength),
                    true));
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
                output, newHtml, scratch);
            path.RemoveAt(path.Count - 1);
        }
    }

    /// <summary>
    ///     Returns the set of indexes in <paramref name="arr" /> that form a longest strictly
    ///     increasing subsequence. O(n log n) via patience sorting (see the implementation
    ///     note below) — comfortably handles the 1,000–5,000-row keyed permutations the
    ///     scale benchmarks throw at the keyed-reorder path.
    /// </summary>
    // Patience-sorting LIS in O(N log N). `tails[k]` holds the arr-index of the smallest
    // tail value among all increasing subsequences of length k+1 seen so far; binary search
    // finds where each new element extends or replaces. `prev[i]` chains back to reconstruct.
    // Returns the set of arr indexes that belong to one optimal LIS — same length as the
    // previous O(N²) DP, but any LIS of optimal length produces the same move count for
    // FrameDiffer's keyed reorder (the only consumer), so the choice between ties is benign.
    internal static HashSet<int> ComputeLisIndexSet(int[] arr)
    {
        // Allocating convenience overload kept for the direct unit/micro benchmarks
        // (LisAlgorithmTests, VsBlazor MicroBenchmarks). The hot path uses the pooled
        // variant below.
        var result = new HashSet<int>();
        ComputeLisIndexSet(arr, arr.Length, result);
        return result;
    }

    // Pooled variant: writes the LIS index set into <paramref name="result" /> (cleared first)
    // for the first <paramref name="len" /> entries of <paramref name="arr" /> (which may be an
    // oversized ArrayPool rental). Rents its two O(len) work arrays from the pool, so a keyed
    // reorder's LIS computation allocates nothing.
    internal static void ComputeLisIndexSet(int[] arr, int len, HashSet<int> result)
    {
        result.Clear();
        if (len == 0)
        {
            return;
        }

        var tails = ArrayPool<int>.Shared.Rent(len); // tails[k] = arr-index of smallest tail of LIS length k+1
        var prev = ArrayPool<int>.Shared.Rent(len); // prev[i] = arr-index of LIS predecessor of i (or -1)
        try
        {
            var tailsLen = 0;

            for (var i = 0; i < len; i++)
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

            // len > 0 (guarded above) and every iteration sets tails[lo] with lo <= tailsLen,
            // bumping tailsLen on the first extension — so tailsLen is now >= 1 and the
            // back-walk start index is in range.
            Debug.Assert(tailsLen >= 1, "LIS tails must be non-empty for non-empty input");
            var cur = tails[tailsLen - 1];
            while (cur >= 0)
            {
                result.Add(cur);
                cur = prev[cur];
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(tails);
            ArrayPool<int>.Shared.Return(prev);
        }
    }

    /// <summary>
    ///     Reusable scratch for the keyed-diff path. Holds the DOM-path accumulator plus a
    ///     small free-list of per-parent keyed buffers (key maps, child lists, the LIS set).
    ///     Reused across renders — cleared, not reallocated — so a keyed list diff allocates
    ///     nothing in steady state. One instance per session; NOT thread-safe (the render
    ///     loop is single-threaded per session). Created via the public parameterless ctor.
    /// </summary>
    public sealed class DiffScratch
    {
        // Free-list of keyed-parent buffers. A keyed parent rents one for the lifetime of its
        // DiffKeyedSiblings call — whose key maps and child lists stay live across the step-5
        // inner-diff recursion — and returns it after. The pool grows to the maximum keyed
        // nesting depth, then is reused forever.
        private readonly Stack<KeyedBundle> _bundles = new();

        // Accessed only by the enclosing FrameDiffer (which, as the containing type, reaches
        // these private members). Kept private so DiffScratch's public surface is just "an
        // opaque reusable token the caller threads back in".

        internal List<int> Path { get; } = new(8);

        internal bool UsedKeyedPath { get; set; }

        internal void ResetForDiff()
        {
            Path.Clear();
            UsedKeyedPath = false;
        }

        internal KeyedBundle RentBundle() => _bundles.Count > 0 ? _bundles.Pop() : new KeyedBundle();

        internal void ReturnBundle(KeyedBundle bundle)
        {
            bundle.Clear();
            _bundles.Push(bundle);
        }
    }

    // One keyed parent's working set. All collections start empty (a rented bundle is always
    // cleared on return) and are reused across renders. Nested in FrameDiffer so it can name
    // the private KeyedChild struct. Internal (not private) so DiffScratch's internal
    // Rent/ReturnBundle members don't trip CS0050/CS0051 — it never appears in a public signature.
    internal sealed class KeyedBundle
    {
        public readonly HashSet<int> LisSet = [];
        public readonly List<int> Live = [];

        // Flat [dst0, src0, dst1, src1, …] accumulator for the step-4 move run. Reused across
        // renders; the emitted PermutationBatch op gets a fresh copy (ToArray) so the consumer
        // can hold the path/moves past the next render's Clear().
        public readonly List<int> MovesBuffer = [];
        public readonly Dictionary<string, int> NewByKey = new(StringComparer.Ordinal);
        public readonly List<KeyedChild> NewKids = [];
        public readonly Dictionary<string, int> OldByKey = new(StringComparer.Ordinal);
        public readonly List<KeyedChild> OldKids = [];
        public readonly HashSet<string> Seen = new(StringComparer.Ordinal);
        public readonly List<KeyedChild> Surviving = [];

        public void Clear()
        {
            OldKids.Clear();
            NewKids.Clear();
            Seen.Clear();
            OldByKey.Clear();
            NewByKey.Clear();
            Surviving.Clear();
            Live.Clear();
            LisSet.Clear();
            MovesBuffer.Clear();
        }
    }

    internal struct KeyedChild
    {
        public string Key;
        public int FrameIndex;
    }
}
