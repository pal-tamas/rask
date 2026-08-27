using System.Text;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

// The diff boundary, which is the load-bearing rule for islands (Rask.Islands): everything below an
// opaque element belongs to a foreign renderer — React, Lit, Blazor — and Rask must never patch into
// it. Two writers on one subtree does not throw; it corrupts on the next parent re-render, so these
// tests are the regression guard for a failure that is otherwise silent.
//
// Deliberately written against a bare Component that overrides OpaqueSubtree rather than against a
// real island: the rule belongs to the render engine, and it should hold for anything that claims it.
public partial class OpaqueSubtreeTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Serialize_OpaqueComponent_WritesTheMarkerAttribute()
    {
        // The client morph reads the attribute; the diff reads the frame flag. Both are needed, so
        // both are pinned — this is the attribute half.
        var html = HtmlOf(new Host(opaque: true)[Span["mounted by react"]]);

        Assert.Contains("data-rask-opaque", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_OrdinaryComponent_DoesNotWriteTheMarker()
    {
        // Negative control for the assertion above: `Contains` on a marker that is always present
        // would pass whatever the flag did.
        var html = HtmlOf(new Host(opaque: false)[Span["ordinary"]]);

        Assert.DoesNotContain("data-rask-opaque", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_OpaqueComponent_SetsTheFrameFlag()
    {
        var frames = Frames(new Host(opaque: true));

        var element = Assert.Single(frames, f => f.Kind == RenderFrameKind.Element);
        Assert.True(element.Opaque);
    }

    [Fact]
    public void Diff_OpaqueSubtree_ChildrenAreNeverPatched()
    {
        // The children differ completely. Rask must still emit nothing: those nodes are React's, and
        // it is mid-reconcile on its own schedule.
        var before = Frames(new Host(opaque: true)[Span["one"], Div["two"]]);
        var after = Frames(new Host(opaque: true)[Span["ONE"], Div["THREE"], Span["four"]]);

        var ops = new List<EditOp>();
        var count = FrameDiffer.Diff(before, after, ops);

        Assert.Equal(0, count);
        Assert.Empty(ops);
    }

    [Fact]
    public void Diff_SameChildrenWithoutTheFlag_DoesProduceOps()
    {
        // The negative control that keeps the test above honest. Identical trees, flag off: if the
        // differ produced nothing here either, the zero-ops assertion would be proving nothing.
        var before = Frames(new Host(opaque: false)[Span["one"], Div["two"]]);
        var after = Frames(new Host(opaque: false)[Span["ONE"], Div["THREE"], Span["four"]]);

        var ops = new List<EditOp>();
        var count = FrameDiffer.Diff(before, after, ops);

        Assert.True(count > 0, "an ordinary subtree with changed children must still diff");
    }

    [Fact]
    public void Diff_OpaqueElement_PropsAttributeStillShips()
    {
        // Props are the ONE thing that crosses the boundary — a changed prop must reach the adapter,
        // and it travels the ordinary attribute-diff path so nothing new is needed on the wire.
        var before = Frames(new Host(opaque: true, props: """{"total":1}"""));
        var after = Frames(new Host(opaque: true, props: """{"total":2}"""));

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before, after, ops);

        var op = Assert.Single(ops);
        Assert.Equal(EditOpKind.SetAttribute, op.Kind);
        Assert.Equal("""{"total":2}""", op.Value);
    }

    [Fact]
    public void ReplayLeanFrames_PreservesOpacity()
    {
        // The retained clean-subtree cache round-trips through LeanFrame, which drops every field it
        // can. Dropping this one would silently un-protect a cached island: the replay writes frames
        // straight back into the live FrameWriter, so the next diff would walk into React's DOM.
        var captured = Frames(new Host(opaque: true)[Span["mounted"]]);

        var lean = new LeanFrame[captured.Length];
        for (var i = 0; i < captured.Length; i++)
        {
            lean[i] = new LeanFrame
            {
                Kind = captured[i].Kind,
                Name = captured[i].Name,
                Value = captured[i].Value,
                SubtreeLength = captured[i].SubtreeLength,
                SelfClosing = captured[i].SelfClosing,
                Opaque = captured[i].Opaque,
            };
        }

        var sb = new StringBuilder();
        using var writer = new FrameWriter();
        HtmlSerializer.ReplayLeanFrames(lean, sb, writer);

        // Frame 0 is the host; the Span child is an Element too, and must NOT inherit the flag.
        var replayedFrames = writer.WrittenSpan.ToArray();
        Assert.Equal(RenderFrameKind.Element, replayedFrames[0].Kind);
        Assert.True(replayedFrames[0].Opaque, "replayed island frame lost its diff boundary");
        Assert.DoesNotContain(replayedFrames[1..], f => f.Opaque);
        Assert.Contains("data-rask-opaque", sb.ToString(), StringComparison.Ordinal);
    }

    // A minimal stand-in for an island host: an element-shaped component that owns its own subtree.
    private sealed class Host(bool opaque, string? props = null) : Component
    {
        protected override string? TagName => "rask-island";

        protected override bool OpaqueSubtree => opaque;

        protected override void WriteAttributes(StringBuilder sb)
        {
            if (props is not null)
            {
                // AppendAttr, not sb.Append: it writes the markup AND registers the Attribute frame.
                // Writing the StringBuilder directly produces correct HTML with no frame behind it, so
                // the value renders once and then never diffs again — which for an island would mean
                // props silently stop reaching the adapter after the first paint.
                AppendAttr(sb, "props", props);
            }
        }
    }

    private static RenderFrame[] Frames(Component tree) => Capture(tree).Frames;

    private static string HtmlOf(Component tree) => Capture(tree).Html;

    private static (RenderFrame[] Frames, string Html) Capture(Component tree)
    {
        var sb = new StringBuilder();
        using var fw = new FrameWriter();
        using (FrameSinkScope.Push(fw))
        {
            HtmlSerializer.Serialize(tree, sb);
        }

        return (fw.WrittenSpan.ToArray(), sb.ToString());
    }
}
