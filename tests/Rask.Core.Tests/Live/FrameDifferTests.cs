using System.Text;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// Phase 1 PR #2: pin down the diff codec's behavior on the three headline scenarios.
// Each test renders two trees (the "before" and "after" state), captures frame
// streams, runs the diff, and asserts the edit-op count + shape. The acceptance
// criteria from the plan (≤ 200 bytes for CounterOnLargePage, etc.) starts with
// these ops — minimal ops yield minimal wire bytes.
public class FrameDifferTests
{
    [Fact]
    public void Diff_IdenticalTrees_ProducesZeroOps()
    {
        var before = Frames(Div(Class: "row")[Span()["Item 5"]]);
        var after = Frames(Div(Class: "row")[Span()["Item 5"]]);

        var ops = new List<EditOp>();
        var count = FrameDiffer.Diff(before, after, ops);

        Assert.Equal(0, count);
        Assert.Empty(ops);
    }

    [Fact]
    public void Diff_TextNodeChanged_ProducesSingleUpdateTextOp()
    {
        // The CounterOnLargePage / TextNodeUpdate headline scenario in miniature: one
        // text node changes deep in an otherwise-identical tree. The diff should be
        // a single UpdateText op — not a full RemoveSubtree + InsertSubtree of the
        // surrounding element.
        var before = Frames(Div(Class: "counter")[Span()["5"]]);
        var after = Frames(Div(Class: "counter")[Span()["6"]]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        var update = Assert.Single(ops);
        Assert.Equal(EditOpKind.UpdateText, update.Kind);
        Assert.Equal("6", update.Value);
    }

    [Fact]
    public void Frames_AdjacentTextChildren_CoalesceIntoOneFrame()
    {
        // The browser merges adjacent text into ONE DOM node; the frame model must match or
        // the diff's per-frame domSlot walk drifts past the real childNodes. Two string children
        // with nothing between them must emit a single Text frame holding the concatenation.
        var frames = Frames(Div()["Toggle ?tab=", "profile"]);

        var text = Assert.Single(frames, f => f.Kind == RenderFrameKind.Text);
        Assert.Equal("Toggle ?tab=profile", text.Name);
    }

    [Fact]
    public void Diff_AdjacentTextChanged_ProducesSingleUpdateTextWithMergedValue()
    {
        // Regression (the "Switch user" toggle button): a label literal sits directly next to a
        // dynamic value — `[<icon/>, "Toggle ?tab=", value]`. The browser coalesces the two texts
        // into one node, so the changed value must ship as ONE UpdateText carrying the full merged
        // string, targeting the single coalesced slot — not an op at a domSlot that doesn't exist.
        var before = Frames(Div()[Span(), "Toggle ?tab=", "profile"]);
        var after = Frames(Div()[Span(), "Toggle ?tab=", "activity"]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        var update = Assert.Single(ops);
        Assert.Equal(EditOpKind.UpdateText, update.Kind);
        Assert.Equal("Toggle ?tab=activity", update.Value);
    }

    [Fact]
    public void Frames_AdjacentTextAcrossFragmentBoundary_Coalesces()
    {
        // A Fragment is transparent — it emits no HTML of its own — so text on either side of it
        // is DOM-adjacent and coalesces. The contiguity (HtmlEnd == htmlStart) check catches this
        // case that a children-list-level merge would miss.
        var frames = Frames(Div()["a", ["b"]]);

        var text = Assert.Single(frames, f => f.Kind == RenderFrameKind.Text);
        Assert.Equal("ab", text.Name);
    }

    [Fact]
    public void Frames_TextSeparatedByElement_StaysDistinct()
    {
        // An element between two texts breaks DOM adjacency (`<span></span>` advances the HTML), so
        // the frames must stay separate — merging them would mis-map the diff onto the real DOM.
        var frames = Frames(Div()["a", Span(), "b"]).Where(f => f.Kind == RenderFrameKind.Text).ToArray();

        Assert.Equal(2, frames.Length);
        Assert.Equal("a", frames[0].Name);
        Assert.Equal("b", frames[1].Name);
    }

    [Fact]
    public void Frames_TextAfterChildElement_NotMergedWithInnerText()
    {
        // The inner text "a" closes with `</span>` before the sibling text "b" starts, so they are
        // not contiguous and must not merge — even though "a" is the most recently emitted frame.
        var frames = Frames(Div()[Span()["a"], "b"]).Where(f => f.Kind == RenderFrameKind.Text).ToArray();

        Assert.Equal(2, frames.Length);
        Assert.Equal("a", frames[0].Name);
        Assert.Equal("b", frames[1].Name);
    }

    [Fact]
    public void Frames_EmptyTextBetweenElements_EmitsNoFrame()
    {
        // An empty string child produces no HTML and so no DOM node. If it emitted a Text frame,
        // the diff would count a node the browser never created and every following sibling's
        // domSlot would drift by one — the trailing Span here would be patched at the wrong index.
        var (frames, html) = FramesAndHtml(Div()[Span(Id: "a"), "", Span(Id: "b")]);

        Assert.DoesNotContain(frames, f => f.Kind == RenderFrameKind.Text);
        Assert.Equal("<div><span id=\"a\"></span><span id=\"b\"></span></div>", html);
    }

    [Fact]
    public void Frames_EmptyTextBetweenTexts_StillCoalescesNeighbours()
    {
        // An empty text in the middle of two real texts must drop out without breaking the
        // coalescing of the survivors — the DOM has the single node "ab".
        var frames = Frames(Div()["a", "", "b"]);

        var text = Assert.Single(frames, f => f.Kind == RenderFrameKind.Text);
        Assert.Equal("ab", text.Name);
    }

    [Fact]
    public void Diff_RawValueUnchanged_ProducesZeroOps()
    {
        // An identical Raw value must produce no ops — the verbatim markup is the same
        // string, so there is nothing to patch (and certainly no spurious UpdateText).
        var before = Frames(Code()[Raw("<span class=\"keyword\">class</span> Foo")]);
        var after = Frames(Code()[Raw("<span class=\"keyword\">class</span> Foo")]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        Assert.Empty(ops);
    }

    [Fact]
    public void Diff_RawValueChanged_ProducesRemoveAndInsert_NotUpdateText()
    {
        // Regression: switching a syntax-highlight code tab swaps one Raw value
        // (highlighted C#) for another (highlighted CSS). A Raw's markup parses into a
        // variable run of sibling DOM nodes, so it must NOT ship as an in-place UpdateText
        // (which sets textContent — escaping the markup into literal <span> text and only
        // touching the first node). It must ship as a Remove + Insert that routes to the
        // full-HTML morph so the browser reparses the new markup.
        var before = Frames(Code()[Raw("<span class=\"keyword\">class</span> Foo")]);
        var (after, afterHtml) = FramesAndHtml(Code()[Raw("<span class=\"cssSelector\">.box </span>{ }")]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops, afterHtml);

        Assert.DoesNotContain(ops, o => o.Kind == EditOpKind.UpdateText);
        Assert.Contains(ops, o => o.Kind == EditOpKind.RemoveSubtree);
        Assert.Contains(ops, o => o.Kind == EditOpKind.InsertSubtree);

        // The replace ops are untrusted positional structural ops, so the live session
        // routes the whole render to the full-HTML morph rather than applying them directly
        // — the morph reparses the new markup instead of escaping it.
        Assert.False(LiveDiffGate.DiffOpsAreClientSupported(ops));
    }

    [Fact]
    public void Diff_RawWithChangingSibling_ForcesFullHtmlMorph()
    {
        // A Raw's markup parses into an unknown number of DOM nodes, so a sibling after it can't be
        // patched positionally (the domSlot index assumes Raw == 1 node). Changing such a sibling
        // must route to the full-HTML morph — NOT ship a mis-targeted, ungated UpdateText.
        var before = Frames(Div()[Raw("<a></a><b></b>"), Span()["x"]]);
        var (after, afterHtml) = FramesAndHtml(Div()[Raw("<a></a><b></b>"), Span()["y"]]);

        var ops = new List<EditOp>();
        var scratch = new FrameDiffer.DiffScratch();
        FrameDiffer.Diff(before, after, ops, scratch, out _, afterHtml);

        Assert.True(scratch.ForceFullHtml);
    }

    [Fact]
    public void Diff_RawWithSiblingsUnchanged_DoesNotForceFullHtml()
    {
        // The morph fallback only fires when something actually changed at the Raw-tainted level —
        // an idle re-render of a page that happens to contain a Raw-with-siblings still ships nothing.
        var before = Frames(Div()[Raw("<a></a><b></b>"), Span()["x"]]);
        var (after, afterHtml) = FramesAndHtml(Div()[Raw("<a></a><b></b>"), Span()["x"]]);

        var ops = new List<EditOp>();
        var scratch = new FrameDiffer.DiffScratch();
        FrameDiffer.Diff(before, after, ops, scratch, out _, afterHtml);

        Assert.Empty(ops);
        Assert.False(scratch.ForceFullHtml);
    }

    [Fact]
    public void Diff_RawAsSoleChild_StaysOnDiffPath()
    {
        // A solitary Raw spans the whole parent — no sibling index follows it — so a sole-child Raw
        // is safe and must not trip the morph fallback. (A CHANGED sole Raw still ships Remove+Insert
        // via SiblingMatches, covered by Diff_RawValueChanged_*; here the surrounding text changes.)
        var before = Frames(Div()[Code()[Raw("<i>x</i>")], Span()["a"]]);
        var (after, afterHtml) = FramesAndHtml(Div()[Code()[Raw("<i>x</i>")], Span()["b"]]);

        var ops = new List<EditOp>();
        var scratch = new FrameDiffer.DiffScratch();
        FrameDiffer.Diff(before, after, ops, scratch, out _, afterHtml);

        var update = Assert.Single(ops);
        Assert.Equal(EditOpKind.UpdateText, update.Kind);
        Assert.Equal("b", update.Value);
        Assert.False(scratch.ForceFullHtml);
    }

    [Fact]
    public void Diff_AttributeValueChanged_ProducesSingleSetAttributeOp()
    {
        var before = Frames(Input<string>(InputType.Text, "f", "old", "edit"));
        var after = Frames(Input<string>(InputType.Text, "f", "new", "edit"));

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        var set = Assert.Single(ops);
        Assert.Equal(EditOpKind.SetAttribute, set.Kind);
        Assert.Equal("value", set.Name);
        Assert.Equal("new", set.Value);
    }

    [Fact]
    public void Diff_MidListAttributeToggled_PreservesTrailingAttributes()
    {
        // Regression: a conditionally-present attribute emitted mid-list (like `checked`
        // on a checkbox, which precedes the trailing attributes — and in the live runtime,
        // the data-rask-on-change handler) must not disturb attributes that follow it. A
        // POSITIONAL attribute diff mis-pairs names across the index shift and emits ops
        // that clobber/remove the trailing attributes — that surfaced as a toggling
        // checkbox losing its event handler (so it stopped responding after a click) and
        // gaining a spurious value="". Name-keyed diffing emits exactly one op for the
        // toggled attribute and leaves `list` (emitted AFTER `checked`) untouched.
        var unchecked_ = Frames(Input<string>(InputType.Checkbox, "n", List: "dl"));
        var checked_ = Frames(Input<string>(InputType.Checkbox, "n", Checked: true, List: "dl"));

        var on = new List<EditOp>();
        FrameDiffer.Diff(unchecked_, checked_, on);
        var addedOp = Assert.Single(on);
        Assert.Equal(EditOpKind.SetAttribute, addedOp.Kind);
        Assert.Equal("checked", addedOp.Name);

        var off = new List<EditOp>();
        FrameDiffer.Diff(checked_, unchecked_, off);
        var removedOp = Assert.Single(off);
        Assert.Equal(EditOpKind.RemoveAttribute, removedOp.Kind);
        Assert.Equal("checked", removedOp.Name);
    }

    [Fact]
    public void Diff_ChildAdded_ProducesInsertSubtreeOp()
    {
        var before = Frames(Ul()[Li()["a"], Li()["b"]]);
        var after = Frames(Ul()[Li()["a"], Li()["b"], Li()["c"]]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        var insert = Assert.Single(ops);
        Assert.Equal(EditOpKind.InsertSubtree, insert.Kind);
    }

    [Fact]
    public void Diff_ChildRemoved_ProducesRemoveSubtreeOp()
    {
        var before = Frames(Ul()[Li()["a"], Li()["b"], Li()["c"]]);
        var after = Frames(Ul()[Li()["a"], Li()["b"]]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        var remove = Assert.Single(ops);
        Assert.Equal(EditOpKind.RemoveSubtree, remove.Kind);
    }

    // --- Trusted tail insert/remove carve-out --------------------------------------------------
    // A pure tail append/truncate at a NESTED, replace-free level applies identically under the
    // client's positional applyDiff and the full-HTML morph, so it ships as a trusted diff instead of
    // forcing the whole page. These pin the exact boundary of that carve-out.

    [Fact]
    public void Diff_NestedTailInsert_IsTrustedAndClientSupported()
    {
        var before = Frames(Ul()[Li()["a"], Li()["b"]]);
        var after = Frames(Ul()[Li()["a"], Li()["b"], Li()["c"]]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        var insert = Assert.Single(ops);
        Assert.Equal(EditOpKind.InsertSubtree, insert.Kind);
        Assert.True(insert.Trusted, "a nested, replace-free tail insert is safe to ship as a diff");
        Assert.True(LiveDiffGate.DiffOpsAreClientSupported(ops));
    }

    [Fact]
    public void Diff_NestedTailRemove_IsTrusted()
    {
        var before = Frames(Ul()[Li()["a"], Li()["b"], Li()["c"]]);
        var after = Frames(Ul()[Li()["a"], Li()["b"]]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        var remove = Assert.Single(ops);
        Assert.Equal(EditOpKind.RemoveSubtree, remove.Kind);
        Assert.True(remove.Trusted, "a nested, replace-free tail truncate is safe to ship as a diff");
    }

    [Fact]
    public void Diff_InsertIntoEmptyNestedParent_IsTrusted()
    {
        // The form-validation pattern: a message container gains its first (text) child. Pure tail
        // insert into an empty nested parent — production should ship the diff, not the whole form.
        var before = Frames(Div(Class: "field")[Div(Class: "msg")]);
        var after = Frames(Div(Class: "field")[Div(Class: "msg")["required"]]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        var insert = Assert.Single(ops);
        Assert.Equal(EditOpKind.InsertSubtree, insert.Kind);
        Assert.True(insert.Trusted);
        Assert.True(LiveDiffGate.DiffOpsAreClientSupported(ops));
    }

    [Fact]
    public void Diff_TopLevelTailInsert_IsNotTrusted()
    {
        // Top-level siblings (path empty) are where the WASM shell's comment nodes live, so the
        // raw-childNodes slot the client uses can diverge from the server's relevant-node index —
        // keep those structural ops untrusted (full-HTML morph).
        var before = Frames([Ul()["a"], Ul()["b"]]);
        var after = Frames([Ul()["a"], Ul()["b"], Ul()["c"]]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        var insert = Assert.Single(ops);
        Assert.Equal(EditOpKind.InsertSubtree, insert.Kind);
        Assert.False(insert.Trusted, "top-level structural ops must stay untrusted");
        Assert.False(LiveDiffGate.DiffOpsAreClientSupported(ops));
    }

    [Fact]
    public void Diff_MidListReplaceThenTail_KeepsTailUntrusted()
    {
        // A tag mismatch mid-level (span -> div) is a replace — the divergence-prone case — so even a
        // trailing insert at that same level stays untrusted.
        var before = Frames(Ul()[Span()["a"], Li()["b"]]);
        var after = Frames(Ul()[Div()["a"], Li()["b"], Li()["c"]]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        Assert.Contains(ops, o => o.Kind == EditOpKind.InsertSubtree);
        Assert.False(LiveDiffGate.DiffOpsAreClientSupported(ops),
            "a level with a replace stays fully untrusted, tail included");
    }

    [Fact]
    public void Diff_TextNodeChanged_PathLocatesTheTextNode()
    {
        // Verifies the DOM-path computation: changing the inner text of a deeply nested
        // span produces an UpdateText op whose Path walks: root-fragment-omitted →
        // div(0) → span(0) → text(0). The client uses this exact path to descend its
        // DOM tree and update the right node.
        var before = Frames(Div()[Span()["one"]]);
        var after = Frames(Div()[Span()["two"]]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        var update = Assert.Single(ops);
        Assert.Equal(EditOpKind.UpdateText, update.Kind);
        // Path[0] = div is child 0 at the root level
        // Path[1] = span is child 0 of div
        // Path[2] = text node is child 0 of span
        Assert.Equal(new[] { 0, 0, 0 }, update.Path);
    }

    [Fact]
    public void Diff_AttributeOnLaterChild_PathPointsToCorrectElement()
    {
        // The element being changed is the SECOND div of the parent. Verifies the
        // sibling slot counter advances correctly past the first sibling.
        var before = Frames(Div()[Div(Class: "a")["one"], Div(Class: "b")["two"]]);
        var after = Frames(Div()[Div(Class: "a")["one"], Div(Class: "z")["two"]]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        var set = Assert.Single(ops);
        Assert.Equal(EditOpKind.SetAttribute, set.Kind);
        Assert.Equal("class", set.Name);
        Assert.Equal("z", set.Value);
        // outer div(0) → second child div(1)
        Assert.Equal(new[] { 0, 1 }, set.Path);
    }

    [Fact]
    public void Diff_LargePageWithCounterUpdate_ProducesO1Ops_NotProportionalToPageSize()
    {
        // The headline metric: bytes-per-update should be O(1) in changed nodes, not
        // O(page size). A 200-row "static body" with one counter cell that bumps from
        // 1→2 must produce exactly ONE op — UpdateText("2") — regardless of the 200
        // surrounding rows. This is the diff codec's reason to exist.
        var before = Frames(BuildLargePageWithCounter(1));
        var after = Frames(BuildLargePageWithCounter(2));

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        Assert.Single(ops);
        Assert.Equal(EditOpKind.UpdateText, ops[0].Kind);
        Assert.Equal("2", ops[0].Value);
    }

    [Fact]
    public void Diff_ChildAdded_WithNewHtml_InsertSubtreeCarriesFragmentRange()
    {
        // When the caller passes newHtml, FrameDiffer records the inserted subtree's char range
        // (HtmlStart/HtmlEnd) instead of allocating a Value string. The wire codec slices the
        // fragment from the same HTML at write time — verified here by both the range and a full
        // BuildPayloadUtf8Diff round-trip, which is what the client actually receives.
        var before = Frames(Ul()[Li()["a"], Li()["b"]]);
        var (afterFrames, afterHtml) = FramesAndHtml(Ul()[Li()["a"], Li()["b"], Li()["c"]]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops, afterHtml);

        var insert = Assert.Single(ops);
        Assert.Equal(EditOpKind.InsertSubtree, insert.Kind);
        Assert.Null(insert.Value);
        Assert.True(insert.HtmlStart >= 0 && insert.HtmlEnd > insert.HtmlStart);
        Assert.Equal("<li>c</li>", afterHtml.Substring(insert.HtmlStart, insert.HtmlEnd - insert.HtmlStart));
        Assert.Equal("<li>c</li>", WireInsertHtml(ops, afterHtml));
    }

    [Fact]
    public void Diff_WithoutNewHtml_InsertSubtreeOmitsFragment()
    {
        var before = Frames(Ul()[Li()["a"]]);
        var afterFrames = Frames(Ul()[Li()["a"], Li()["b"]]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops);

        var insert = Assert.Single(ops);
        Assert.Equal(EditOpKind.InsertSubtree, insert.Kind);
        Assert.Null(insert.Value);
        // No render HTML supplied → sentinel range, so the wire carries a null fragment and the
        // caller routes the payload through the full-HTML fallback.
        Assert.True(insert.HtmlStart < 0);
    }

    // Build the diff payload exactly as the live session does and pull the first InsertSubtree
    // op's html field ([kind, path, html, domCount]) back out of the wire JSON.
    private static string? WireInsertHtml(IReadOnlyList<EditOp> ops, string newHtml)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        LivePayload.BuildPayloadUtf8Diff(buffer, ops, newHtml: newHtml);
        using var doc = System.Text.Json.JsonDocument.Parse(buffer.WrittenMemory);
        foreach (var op in doc.RootElement.GetProperty("ops").EnumerateArray())
        {
            if (op[0].GetInt32() == (int)EditOpKind.InsertSubtree)
            {
                return op[2].ValueKind == System.Text.Json.JsonValueKind.Null ? null : op[2].GetString();
            }
        }

        return null;
    }

    // ----- Keyed-list path -------------------------------------------------------------
    // A parent whose every direct child is a keyed Element (data-rask-key) triggers
    // FrameDiffer's keyed-matching branch instead of the positional sibling walk. The
    // payoff is bounded by Blazor's KeyedList100Reorder / DeleteMiddleRow scenarios:
    // a row swap should be 2 MoveSubtree ops (not 4× SetAttribute + UpdateText), and
    // a middle-row delete should be a single RemoveSubtree (not 99 ops trip the gate
    // into full-HTML fallback).

    [Fact]
    public void Diff_KeyedList_RowsSwapped_EmitsSinglePermutationBatchWithTrustedFlag()
    {
        var before = Frames(BuildKeyedRows(0, 1, 2, 3));
        var (afterFrames, afterHtml) = FramesAndHtml(BuildKeyedRows(0, 3, 2, 1));

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops, out var usedKeyed, afterHtml);

        Assert.True(usedKeyed);
        // The whole move run under one keyed parent collapses into a single PermutationBatch
        // op carrying the shared parent path once. LIS length 2 of a 4-element permutation
        // bounds the move count at <= N - LIS = 2 pairs (4 ints), flattened into op.Moves.
        var batch = Assert.Single(ops);
        Assert.Equal(EditOpKind.PermutationBatch, batch.Kind);
        Assert.True(batch.Trusted,
            "Keyed moves must be marked Trusted so the live-session gate doesn't divert to full HTML.");
        Assert.NotNull(batch.Moves);
        Assert.True(batch.Moves!.Length is 2 or 4, "Expected 1–2 (dst,src) pairs.");
        Assert.Equal(0, batch.Moves.Length % 2);
    }

    [Fact]
    public void Diff_MixedKeyedAndUnkeyedSiblings_FallsBackToPositional_NoTrustedOps()
    {
        // The keyed reconciliation path requires EVERY direct child to carry data-rask-key. A
        // mix of keyed and unkeyed siblings must fall back to the positional sibling walk
        // (usedKeyed == false) — it must never emit a trusted PermutationBatch/Move that would
        // misalign the unkeyed rows on the client.
        var before = Frames(BuildMixedRows((0, true), (1, false), (2, true)));
        var (afterFrames, afterHtml) = FramesAndHtml(BuildMixedRows((2, true), (1, false), (0, true)));

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops, out var usedKeyed, afterHtml);

        Assert.False(usedKeyed);
        Assert.All(ops, op => Assert.False(op.Trusted, "mixed keyed/unkeyed siblings must not produce trusted ops"));
    }

    [Fact]
    public void Diff_KeyedList_ViaKeyProperty_UsesKeyedPathWithTrustedRemove()
    {
        // The first-class Key property emits the same data-rask-key the differ keys on, so a
        // middle-row delete takes the trusted keyed path exactly like the Data["rask-key"] form.
        var before = Frames(BuildKeyedRowsViaKeyProp(0, 1, 2, 3, 4));
        var afterFrames = Frames(BuildKeyedRowsViaKeyProp(0, 1, 3, 4));

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops, out var usedKeyed);

        Assert.True(usedKeyed);
        var remove = Assert.Single(ops);
        Assert.Equal(EditOpKind.RemoveSubtree, remove.Kind);
        Assert.True(remove.Trusted);
    }

    [Fact]
    public void Diff_KeyedList_MiddleRowDeleted_EmitsSingleTrustedRemove()
    {
        var before = Frames(BuildKeyedRows(0, 1, 2, 3, 4));
        var afterFrames = Frames(BuildKeyedRows(0, 1, 3, 4));

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops, out var usedKeyed);

        Assert.True(usedKeyed);
        var remove = Assert.Single(ops);
        Assert.Equal(EditOpKind.RemoveSubtree, remove.Kind);
        Assert.True(remove.Trusted);
        Assert.Equal(1, remove.Length);
        // Path = [0 (Ul), 2 (slot of k2 in old)].
        Assert.Equal(new[] { 0, 2 }, remove.Path);
    }

    [Fact]
    public void Diff_KeyedList_RowAppended_EmitsSingleTrustedInsertWithHtml()
    {
        var before = Frames(BuildKeyedRows(0, 1, 2));
        var (afterFrames, afterHtml) = FramesAndHtml(BuildKeyedRows(0, 1, 2, 3));

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops, out var usedKeyed, afterHtml);

        Assert.True(usedKeyed);
        var insert = Assert.Single(ops);
        Assert.Equal(EditOpKind.InsertSubtree, insert.Kind);
        Assert.True(insert.Trusted);
        // The fragment travels as a deferred char range, sliced into the wire payload at write
        // time — assert via the actual BuildPayloadUtf8Diff output the client receives.
        Assert.Null(insert.Value);
        Assert.Contains("data-rask-key=\"3\"", WireInsertHtml(ops, afterHtml)!);
    }

    [Fact]
    public void Diff_KeyedList_RowPrepended_EmitsSingleTrustedInsertAtSlotZero()
    {
        var before = Frames(BuildKeyedRows(1, 2, 3));
        var (afterFrames, afterHtml) = FramesAndHtml(BuildKeyedRows(0, 1, 2, 3));

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops, out var usedKeyed, afterHtml);

        Assert.True(usedKeyed);
        var insert = Assert.Single(ops);
        Assert.Equal(EditOpKind.InsertSubtree, insert.Kind);
        Assert.True(insert.Trusted);
        // Path = [0 (Ul), 0 (insert before slot 0)].
        Assert.Equal(new[] { 0, 0 }, insert.Path);
    }

    [Fact]
    public void Diff_KeyedList_IdenticalRows_ProducesZeroOps()
    {
        var before = Frames(BuildKeyedRows(0, 1, 2, 3));
        var afterFrames = Frames(BuildKeyedRows(0, 1, 2, 3));

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops, out var usedKeyed);

        // Even when no ops are emitted, the keyed path was probed and ran — usedKeyedPath
        // reflects "did keyed matching get used", not "did it produce structural ops".
        Assert.True(usedKeyed);
        Assert.Empty(ops);
    }

    [Fact]
    public void Diff_KeyedList_SameRowInnerTextChanged_RecursesAtNewSlotPath()
    {
        // k1's inner text changes from "Item 1" → "Item 1!". The keyed match recognises
        // k1 stays at slot 1 and recurses for the inner UpdateText. Path coords reference
        // the NEW slot, so the client's post-permutation DOM walk lands on the right node.
        var before = Frames(BuildKeyedRowsWithLabel(("0", "Item 0"), ("1", "Item 1"), ("2", "Item 2")));
        var afterFrames = Frames(BuildKeyedRowsWithLabel(("0", "Item 0"), ("1", "Item 1!"), ("2", "Item 2")));

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops, out var usedKeyed);

        Assert.True(usedKeyed);
        var update = Assert.Single(ops);
        Assert.Equal(EditOpKind.UpdateText, update.Kind);
        Assert.Equal("Item 1!", update.Value);
        // Path: [0=Ul, 1=second Li, 0=its text child].
        Assert.Equal(new[] { 0, 1, 0 }, update.Path);
    }

    [Fact]
    public void Diff_KeyedList_PartialKeys_FallsBackToPositional()
    {
        // One child without data-rask-key drops the whole parent back to the positional
        // walk. Mirrors the morph engine's all-or-nothing keyed detection so the diff
        // codec doesn't disagree with the client about which path applied.
        var before = Frames(Ul()[
            Li(Data: new Dictionary<string, string?> { ["rask-key"] = "a" })["one"],
            Li()["two"]
        ]);
        var afterFrames = Frames(Ul()[
            Li(Data: new Dictionary<string, string?> { ["rask-key"] = "a" })["one!"],
            Li()["two"]
        ]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops, out var usedKeyed);

        Assert.False(usedKeyed);
        // Positional walk still produces the correct in-place update.
        var update = Assert.Single(ops);
        Assert.Equal(EditOpKind.UpdateText, update.Kind);
        Assert.Equal("one!", update.Value);
    }

    [Fact]
    public void Diff_KeyedList_DuplicateKeys_FallsBackToPositional()
    {
        var before = Frames(Ul()[
            Li(Data: new Dictionary<string, string?> { ["rask-key"] = "dup" })["a"],
            Li(Data: new Dictionary<string, string?> { ["rask-key"] = "dup" })["b"]
        ]);
        var afterFrames = Frames(Ul()[
            Li(Data: new Dictionary<string, string?> { ["rask-key"] = "dup" })["a!"],
            Li(Data: new Dictionary<string, string?> { ["rask-key"] = "dup" })["b"]
        ]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops, out var usedKeyed);

        Assert.False(usedKeyed);
        var update = Assert.Single(ops);
        Assert.Equal(EditOpKind.UpdateText, update.Kind);
    }

    [Fact]
    public void Diff_KeyedList_DuplicateKeys_ReportsTheOffendingKey()
    {
        var before = Frames(Ul()[
            Li(Data: new Dictionary<string, string?> { ["rask-key"] = "row-7" })["a"],
            Li(Data: new Dictionary<string, string?> { ["rask-key"] = "row-7" })["b"]
        ]);
        var after = Frames(Ul()[
            Li(Data: new Dictionary<string, string?> { ["rask-key"] = "row-7" })["a!"],
            Li(Data: new Dictionary<string, string?> { ["rask-key"] = "row-7" })["b"]
        ]);

        var reported = new List<string>();
        var previous = FrameDiffer.OnDuplicateKey;
        FrameDiffer.OnDuplicateKey = reported.Add;
        try
        {
            FrameDiffer.Diff(before, after, new List<EditOp>(), out var usedKeyed);
            Assert.False(usedKeyed);
            Assert.Contains("row-7", reported);
        }
        finally
        {
            FrameDiffer.OnDuplicateKey = previous;
        }
    }

    [Fact]
    public void Diff_KeyedList_UniqueKeys_DoesNotReport()
    {
        var before = Frames(Ul()[
            Li(Data: new Dictionary<string, string?> { ["rask-key"] = "a" })["1"],
            Li(Data: new Dictionary<string, string?> { ["rask-key"] = "b" })["2"]
        ]);
        var after = Frames(Ul()[
            Li(Data: new Dictionary<string, string?> { ["rask-key"] = "a" })["1!"],
            Li(Data: new Dictionary<string, string?> { ["rask-key"] = "b" })["2"]
        ]);

        var reported = new List<string>();
        var previous = FrameDiffer.OnDuplicateKey;
        FrameDiffer.OnDuplicateKey = reported.Add;
        try
        {
            FrameDiffer.Diff(before, after, new List<EditOp>(), out var usedKeyed);
            Assert.True(usedKeyed);
            Assert.Empty(reported);
        }
        finally
        {
            FrameDiffer.OnDuplicateKey = previous;
        }
    }

    [Fact]
    public void Diff_KeyedList_SameKeyDifferentTag_EmitsTrustedRemoveAndInsert()
    {
        // Same data-rask-key but the element kind changed (Li → Span). The keyed branch
        // treats this as a fresh node: remove the old, insert the new at the same slot.
        // Both ops carry Trusted so the gate ships them as diff.
        var before = Frames(Ul()[
            Li(Data: new Dictionary<string, string?> { ["rask-key"] = "x" })["original"]
        ]);
        var (afterFrames, afterHtml) = FramesAndHtml(Ul()[
            Span(Data: new Dictionary<string, string?> { ["rask-key"] = "x" })["replaced"]
        ]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops, out var usedKeyed, afterHtml);

        Assert.True(usedKeyed);
        Assert.Equal(2, ops.Count);
        Assert.Equal(EditOpKind.RemoveSubtree, ops[0].Kind);
        Assert.Equal(EditOpKind.InsertSubtree, ops[1].Kind);
        Assert.All(ops, op => Assert.True(op.Trusted));
    }

    [Fact]
    public void Diff_KeyedList_HundredRowSwap_EmitsSingleBatchOfTwoMoves_NotNinetyRewrites()
    {
        // The headline scenario — KeyedList100Reorder: swap rows 5 and 95 in a 100-row
        // list. Positional diff emits 2 SetAttribute + 2 UpdateText (205 bytes vs
        // Blazor's 128). Keyed matching emits one PermutationBatch carrying exactly 2 moves
        // (4 ints) and the shared parent path once.
        var orderBefore = new int[100];
        var orderAfter = new int[100];
        for (var i = 0; i < 100; i++)
        {
            orderBefore[i] = i;
            orderAfter[i] = i;
        }

        (orderAfter[5], orderAfter[95]) = (orderAfter[95], orderAfter[5]);

        var before = Frames(BuildKeyedRows(orderBefore));
        var afterFrames = Frames(BuildKeyedRows(orderAfter));

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops, out var usedKeyed);

        Assert.True(usedKeyed);
        var batch = Assert.Single(ops);
        Assert.Equal(EditOpKind.PermutationBatch, batch.Kind);
        Assert.True(batch.Trusted);
        Assert.Equal(4, batch.Moves!.Length); // 2 (dst,src) pairs
    }

    [Theory]
    [InlineData(50, 1)]
    [InlineData(50, 2)]
    [InlineData(100, 7)]
    [InlineData(100, 13)]
    [InlineData(250, 99)]
    public void Diff_KeyedList_RandomPermutation_MoveOpsReproduceTargetOrder(int n, int seed)
    {
        // Strong correctness gate for the keyed move loop: for a random permutation (same key
        // set, no inserts/removes) the diff emits a single PermutationBatch op. Replaying its
        // flat [dst0,src0,dst1,src1,…] pairs against the before-order — exactly as the client
        // interpreter does: detach at `src`, then insert before the post-detach node at `dst` —
        // must reproduce the after-order. This pins the (dst,src) move semantics regardless of
        // the algorithm that computes them, so it guards any future rewrite of the loop.
        var rng = new Random(seed);
        var before = new int[n];
        for (var i = 0; i < n; i++)
        {
            before[i] = i;
        }

        // Fisher–Yates shuffle into the after-order.
        var after = (int[])before.Clone();
        for (var i = n - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (after[i], after[j]) = (after[j], after[i]);
        }

        var beforeFrames = Frames(BuildKeyedRows(before));
        var afterFrames = Frames(BuildKeyedRows(after));

        var ops = new List<EditOp>();
        FrameDiffer.Diff(beforeFrames, afterFrames, ops, out var usedKeyed);

        Assert.True(usedKeyed);
        // A non-identity permutation produces exactly one batch op (identity → zero ops).
        Assert.True(ops.Count <= 1);

        // Replay the batch's (dst,src) pairs against a live list of keys, mirroring the DOM
        // interpreter (detach at src, insert at dst in the post-detach list).
        var live = new List<int>(before);
        if (ops.Count == 1)
        {
            var batch = ops[0];
            Assert.Equal(EditOpKind.PermutationBatch, batch.Kind);
            var moves = batch.Moves!;
            for (var m = 0; m + 1 < moves.Length; m += 2)
            {
                var dst = moves[m];
                var src = moves[m + 1];
                var moved = live[src];
                live.RemoveAt(src);
                live.Insert(dst, moved);
            }
        }

        Assert.Equal(after, live.ToArray());
    }

    [Theory]
    [InlineData(50)]
    [InlineData(500)]
    [InlineData(1000)]
    public void Diff_KeyedList_FullReverse_MoveOpsReproduceTargetOrder(int n)
    {
        // The worst case for the keyed move loop: a fully reversed list has an LIS of length 1, so
        // n-1 rows are off-LIS and each emits a move. The RandomPermutation gate above (n ≤ 250)
        // rarely lands on a clean reverse, and the FrameDiffer ReverseReorder benchmark measures
        // this shape's *cost* — this test pins its *correctness*: replaying the single
        // PermutationBatch's (dst,src) pairs against the before-order must reproduce the reverse.
        // Covers large n so the move semantics stay correct if the O(n²) loop is ever reworked.
        var before = new int[n];
        for (var i = 0; i < n; i++)
        {
            before[i] = i;
        }

        var after = (int[])before.Clone();
        Array.Reverse(after);

        var beforeFrames = Frames(BuildKeyedRows(before));
        var afterFrames = Frames(BuildKeyedRows(after));

        var ops = new List<EditOp>();
        FrameDiffer.Diff(beforeFrames, afterFrames, ops, out var usedKeyed);

        Assert.True(usedKeyed);
        var batch = Assert.Single(ops);
        Assert.Equal(EditOpKind.PermutationBatch, batch.Kind);
        Assert.True(batch.Trusted);

        // Replay the (dst,src) pairs exactly as the DOM interpreter does: detach at src, insert at
        // dst in the post-detach list.
        var live = new List<int>(before);
        var moves = batch.Moves!;
        for (var m = 0; m + 1 < moves.Length; m += 2)
        {
            var moved = live[moves[m + 1]];
            live.RemoveAt(moves[m + 1]);
            live.Insert(moves[m], moved);
        }

        Assert.Equal(after, live.ToArray());
    }

    [Fact]
    public void Diff_NestedKeyedList_OuterReorderAndInnerReorder_EmitsTrustedBatchesAtBothDepths()
    {
        // Recursion-safety guard for the scratch-pooling optimisation. The outer keyed list's
        // DiffKeyedSiblings call is still LIVE — its key map and child lists are read in the
        // step-5 inner-diff loop — while it recurses into a kept row whose own children form a
        // SECOND keyed list, re-entering DiffKeyedSiblings. Any scratch shared across that
        // recursion must not be clobbered (including the MovesBuffer the batch accumulates into).
        // This pins the exact ops: the outer row reorder and the inner span reorder each emit a
        // single trusted PermutationBatch, at the right parent depths and non-interleaved.
        static Component Row(int key, bool innerSwapped)
        {
            Component a = Span(Key: $"{key}a")["A"];
            Component b = Span(Key: $"{key}b")["B"];
            var li = Li(Key: key);
            return innerSwapped ? li[b, a] : li[a, b];
        }

        var before = Frames(Ul()[new List<Component> { Row(0, false), Row(1, false) }]);
        var (afterFrames, afterHtml) = FramesAndHtml(Ul()[new List<Component> { Row(1, false), Row(0, true) }]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops, out var usedKeyed, afterHtml);

        Assert.True(usedKeyed);
        Assert.Equal(2, ops.Count);
        Assert.All(ops, op => Assert.Equal(EditOpKind.PermutationBatch, op.Kind));
        Assert.All(ops, op => Assert.True(op.Trusted));
        // The batch op's Path is the PARENT (no trailing dst slot — that now lives in Moves).
        // Outer batch is emitted first (step 4 precedes the step-5 inner recursion).
        Assert.Equal(new[] { 0 }, ops[0].Path); // outer: rows reordered under the Ul (slot 0)
        Assert.Equal(new[] { 0, 1 }, ops[1].Path); // inner: spans reordered inside row 0 at its new slot 1
        Assert.All(ops, op => Assert.Equal(2, op.Moves!.Length)); // one (dst,src) pair each
    }

    private static Component BuildKeyedRows(params int[] keys)
    {
        var rows = new List<Component>(keys.Length);
        foreach (var k in keys)
        {
            rows.Add(Li(Data: new Dictionary<string, string?> { ["rask-key"] = k.ToString() })[
                $"Item {k}"
            ]);
        }

        return Ul()[rows];
    }

    private static Component BuildMixedRows(params (int Key, bool Keyed)[] rows)
    {
        var children = new List<Component>(rows.Length);
        foreach (var (key, keyed) in rows)
        {
            children.Add(keyed
                ? Li(Data: new Dictionary<string, string?> { ["rask-key"] = key.ToString() })[$"Item {key}"]
                : Li()[$"Item {key}"]);
        }

        return Ul()[children];
    }

    // Same shape as BuildKeyedRows but keyed via the first-class Key property instead of a
    // Data["rask-key"] entry — the differ should treat them identically.
    private static Component BuildKeyedRowsViaKeyProp(params int[] keys)
    {
        var rows = new List<Component>(keys.Length);
        foreach (var k in keys)
        {
            rows.Add(Li(Key: k)[$"Item {k}"]);
        }

        return Ul()[rows];
    }

    private static Component BuildKeyedRowsWithLabel(params (string Key, string Label)[] rows)
    {
        var items = new List<Component>(rows.Length);
        foreach (var (key, label) in rows)
        {
            items.Add(Li(Data: new Dictionary<string, string?> { ["rask-key"] = key })[label]);
        }

        return Ul()[items];
    }

    private static RenderFrame[] Frames(Component tree)
    {
        var (frames, _) = FramesAndHtml(tree);
        return frames;
    }

    private static (RenderFrame[] Frames, string Html) FramesAndHtml(Component tree)
    {
        var sb = new StringBuilder();
        var fw = new FrameWriter();
        using (FrameSinkScope.Push(fw))
        {
            HtmlSerializer.Serialize(tree, sb);
        }

        return (fw.WrittenSpan.ToArray(), sb.ToString());
    }

    private static Component BuildLargePageWithCounter(int counter)
    {
        const int rowCount = 200;
        var rows = new List<Component>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            rows.Add(Div(Class: "row", Id: $"r{i}", Key: i)[
                Span(Class: "label")[$"Item {i}"]
            ]);
        }

        return Div(Class: "container")[
            Div(Class: "counter")[Span(Class: "value")[counter.ToString()]],
            Div(Class: "body")[rows]
        ];
    }
}
