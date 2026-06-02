using System.Text;
using Rask.Core.Live;
using C = Rask.Core.Components.Generated;

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
        var before = Frames(C.Div(Class: "row")[C.Span()["Item 5"]]);
        var after = Frames(C.Div(Class: "row")[C.Span()["Item 5"]]);

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
        var before = Frames(C.Div(Class: "counter")[C.Span()["5"]]);
        var after = Frames(C.Div(Class: "counter")[C.Span()["6"]]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        var update = Assert.Single(ops);
        Assert.Equal(EditOpKind.UpdateText, update.Kind);
        Assert.Equal("6", update.Value);
    }

    [Fact]
    public void Diff_AttributeValueChanged_ProducesSingleSetAttributeOp()
    {
        var before = Frames(C.Input("text", "f", "old", "edit"));
        var after = Frames(C.Input("text", "f", "new", "edit"));

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
        var unchecked_ = Frames(C.Input("checkbox", "n", List: "dl"));
        var checked_ = Frames(C.Input("checkbox", "n", Checked: true, List: "dl"));

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
        var before = Frames(C.Ul()[C.Li()["a"], C.Li()["b"]]);
        var after = Frames(C.Ul()[C.Li()["a"], C.Li()["b"], C.Li()["c"]]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        var insert = Assert.Single(ops);
        Assert.Equal(EditOpKind.InsertSubtree, insert.Kind);
    }

    [Fact]
    public void Diff_ChildRemoved_ProducesRemoveSubtreeOp()
    {
        var before = Frames(C.Ul()[C.Li()["a"], C.Li()["b"], C.Li()["c"]]);
        var after = Frames(C.Ul()[C.Li()["a"], C.Li()["b"]]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        var remove = Assert.Single(ops);
        Assert.Equal(EditOpKind.RemoveSubtree, remove.Kind);
    }

    [Fact]
    public void Diff_TextNodeChanged_PathLocatesTheTextNode()
    {
        // Verifies the DOM-path computation: changing the inner text of a deeply nested
        // span produces an UpdateText op whose Path walks: root-fragment-omitted →
        // div(0) → span(0) → text(0). The client uses this exact path to descend its
        // DOM tree and update the right node.
        var before = Frames(C.Div()[C.Span()["one"]]);
        var after = Frames(C.Div()[C.Span()["two"]]);

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
        var before = Frames(C.Div()[C.Div(Class: "a")["one"], C.Div(Class: "b")["two"]]);
        var after = Frames(C.Div()[C.Div(Class: "a")["one"], C.Div(Class: "z")["two"]]);

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
    public void Diff_ChildAdded_WithNewHtml_InsertSubtreeCarriesFragment()
    {
        // When the caller passes newHtml, FrameDiffer attaches the HTML slice for the
        // inserted subtree to op.Value. This is what makes the client-side InsertSubtree
        // applicable without a follow-up full render: the op carries everything needed.
        var before = Frames(C.Ul()[C.Li()["a"], C.Li()["b"]]);
        var (afterFrames, afterHtml) = FramesAndHtml(C.Ul()[C.Li()["a"], C.Li()["b"], C.Li()["c"]]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops, afterHtml);

        var insert = Assert.Single(ops);
        Assert.Equal(EditOpKind.InsertSubtree, insert.Kind);
        Assert.NotNull(insert.Value);
        Assert.Equal("<li>c</li>", insert.Value);
    }

    [Fact]
    public void Diff_WithoutNewHtml_InsertSubtreeOmitsFragment()
    {
        var before = Frames(C.Ul()[C.Li()["a"]]);
        var afterFrames = Frames(C.Ul()[C.Li()["a"], C.Li()["b"]]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops);

        var insert = Assert.Single(ops);
        Assert.Equal(EditOpKind.InsertSubtree, insert.Kind);
        Assert.Null(insert.Value);
    }

    // ----- Keyed-list path -------------------------------------------------------------
    // A parent whose every direct child is a keyed Element (data-rask-key) triggers
    // FrameDiffer's keyed-matching branch instead of the positional sibling walk. The
    // payoff is bounded by Blazor's KeyedList100Reorder / DeleteMiddleRow scenarios:
    // a row swap should be 2 MoveSubtree ops (not 4× SetAttribute + UpdateText), and
    // a middle-row delete should be a single RemoveSubtree (not 99 ops trip the gate
    // into full-HTML fallback).

    [Fact]
    public void Diff_KeyedList_RowsSwapped_EmitsTwoMoveOpsWithTrustedFlag()
    {
        var before = Frames(BuildKeyedRows(0, 1, 2, 3));
        var (afterFrames, afterHtml) = FramesAndHtml(BuildKeyedRows(0, 3, 2, 1));

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops, out var usedKeyed, afterHtml);

        Assert.True(usedKeyed);
        // Minimal-moves strategy: LIS length 2 of a 4-element permutation bounds the move
        // count at <= N - LIS = 2 ops, but a well-chosen LIS combined with the
        // `src == target` short-circuit can land in <= 1 op (some elements happen to
        // already be at their target slot). Either is correct — assert the upper bound.
        Assert.InRange(ops.Count, 1, 2);
        Assert.All(ops, op => Assert.Equal(EditOpKind.MoveSubtree, op.Kind));
        Assert.All(ops, op => Assert.True(op.Trusted, "Keyed moves must be marked Trusted so the live-session gate doesn't divert to full HTML."));
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
        Assert.NotNull(insert.Value);
        Assert.Contains("data-rask-key=\"3\"", insert.Value!);
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
        var before = Frames(C.Ul()[
            (Child)C.Li(Data: new Dictionary<string, string?> { ["rask-key"] = "a" })["one"],
            (Child)C.Li()["two"]
        ]);
        var afterFrames = Frames(C.Ul()[
            (Child)C.Li(Data: new Dictionary<string, string?> { ["rask-key"] = "a" })["one!"],
            (Child)C.Li()["two"]
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
        var before = Frames(C.Ul()[
            (Child)C.Li(Data: new Dictionary<string, string?> { ["rask-key"] = "dup" })["a"],
            (Child)C.Li(Data: new Dictionary<string, string?> { ["rask-key"] = "dup" })["b"]
        ]);
        var afterFrames = Frames(C.Ul()[
            (Child)C.Li(Data: new Dictionary<string, string?> { ["rask-key"] = "dup" })["a!"],
            (Child)C.Li(Data: new Dictionary<string, string?> { ["rask-key"] = "dup" })["b"]
        ]);

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, afterFrames, ops, out var usedKeyed);

        Assert.False(usedKeyed);
        var update = Assert.Single(ops);
        Assert.Equal(EditOpKind.UpdateText, update.Kind);
    }

    [Fact]
    public void Diff_KeyedList_SameKeyDifferentTag_EmitsTrustedRemoveAndInsert()
    {
        // Same data-rask-key but the element kind changed (Li → Span). The keyed branch
        // treats this as a fresh node: remove the old, insert the new at the same slot.
        // Both ops carry Trusted so the gate ships them as diff.
        var before = Frames(C.Ul()[
            (Child)C.Li(Data: new Dictionary<string, string?> { ["rask-key"] = "x" })["original"]
        ]);
        var (afterFrames, afterHtml) = FramesAndHtml(C.Ul()[
            (Child)C.Span(Data: new Dictionary<string, string?> { ["rask-key"] = "x" })["replaced"]
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
    public void Diff_KeyedList_HundredRowSwap_EmitsTwoMoves_NotNinetyRewrites()
    {
        // The headline scenario — KeyedList100Reorder: swap rows 5 and 95 in a 100-row
        // list. Positional diff emits 2 SetAttribute + 2 UpdateText (205 bytes vs
        // Blazor's 128). Keyed matching emits exactly 2 MoveSubtree ops.
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
        Assert.Equal(2, ops.Count);
        Assert.All(ops, op => Assert.Equal(EditOpKind.MoveSubtree, op.Kind));
        Assert.All(ops, op => Assert.True(op.Trusted));
    }

    [Theory]
    [InlineData(50, 1)]
    [InlineData(50, 2)]
    [InlineData(100, 7)]
    [InlineData(100, 13)]
    [InlineData(250, 99)]
    public void Diff_KeyedList_RandomPermutation_MoveOpsReproduceTargetOrder(int n, int seed)
    {
        // Strong correctness gate for the keyed MoveSubtree loop: for a random permutation
        // (same key set, no inserts/removes) the diff emits only MoveSubtree ops. Replaying
        // them against the before-order — exactly as the client interpreter does: detach at
        // `src` (op.Length), then insert before the post-detach node at `target`
        // (op.Path[^1]) — must reproduce the after-order. This pins the (src,target) move
        // semantics regardless of the algorithm that computes them, so it guards any future
        // rewrite of the loop (e.g. an O(N log N) order-statistics replacement).
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
        Assert.All(ops, op => Assert.Equal(EditOpKind.MoveSubtree, op.Kind));

        // Replay the move ops against a live list of keys, mirroring the DOM interpreter.
        var live = new List<int>(before);
        foreach (var op in ops)
        {
            var src = op.Length;
            var target = op.Path[^1];
            var moved = live[src];
            live.RemoveAt(src);
            live.Insert(target, moved);
        }

        Assert.Equal(after, live.ToArray());
    }

    private static Component BuildKeyedRows(params int[] keys)
    {
        var rows = new List<Child>(keys.Length);
        foreach (var k in keys)
        {
            rows.Add(C.Li(Data: new Dictionary<string, string?> { ["rask-key"] = k.ToString() })[
                $"Item {k}"
            ]);
        }

        return C.Ul()[rows];
    }

    // Same shape as BuildKeyedRows but keyed via the first-class Key property instead of a
    // Data["rask-key"] entry — the differ should treat them identically.
    private static Component BuildKeyedRowsViaKeyProp(params int[] keys)
    {
        var rows = new List<Child>(keys.Length);
        foreach (var k in keys)
        {
            rows.Add(C.Li(Key: k)[$"Item {k}"]);
        }

        return C.Ul()[rows];
    }

    private static Component BuildKeyedRowsWithLabel(params (string Key, string Label)[] rows)
    {
        var items = new List<Child>(rows.Length);
        foreach (var (key, label) in rows)
        {
            items.Add(C.Li(Data: new Dictionary<string, string?> { ["rask-key"] = key })[label]);
        }

        return C.Ul()[items];
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
        var rows = new List<Child>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            rows.Add(C.Div(Class: "row", Id: $"r{i}")[
                C.Span(Class: "label")[$"Item {i}"]
            ]);
        }

        return C.Div(Class: "container")[
            C.Div(Class: "counter")[C.Span(Class: "value")[counter.ToString()]],
            C.Div(Class: "body")[rows]
        ];
    }
}
