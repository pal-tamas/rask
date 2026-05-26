using System.Text;
using Rask.Core.Live;
using C = Rask.Core.Components.Components;

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
