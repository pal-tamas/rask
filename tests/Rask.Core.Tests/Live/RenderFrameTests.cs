using System.Text;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// Phase 1 PR #1: verify FrameWriter emits the expected sequence when HtmlSerializer
// runs under a FrameSinkScope. The frame stream is the data the diff codec will
// consume, so the contract here pins down what every later phase relies on.
public class RenderFrameTests
{
    [Fact]
    public void Serialize_EmitsElementOpenAndCloseFrames_WithSubtreeLength()
    {
        // <div class="x"><span>hi</span></div>
        // Expected frames: Element(div) [SubtreeLength=4]
        //                    Attribute(class, x)
        //                    Element(span) [SubtreeLength=2]
        //                      Text(hi)
        var tree = Div(Class: "x")[Span()["hi"]];

        var frames = RenderAndCaptureFrames(tree);

        Assert.Equal(4, frames.Length);
        Assert.Equal(RenderFrameKind.Element, frames[0].Kind);
        Assert.Equal("div", frames[0].Name);
        Assert.Equal(4, frames[0].SubtreeLength);
        Assert.Equal(RenderFrameKind.Attribute, frames[1].Kind);
        Assert.Equal("class", frames[1].Name);
        Assert.Equal("x", frames[1].Value);
        Assert.Equal(RenderFrameKind.Element, frames[2].Kind);
        Assert.Equal("span", frames[2].Name);
        Assert.Equal(2, frames[2].SubtreeLength);
        Assert.Equal(RenderFrameKind.Text, frames[3].Kind);
        Assert.Equal("hi", frames[3].Name);
    }

    [Fact]
    public void Serialize_NoFrameScope_LeavesHtmlOutputUnchanged()
    {
        // Sanity: the frame-producing path must be invisible when no scope is active.
        // If a future change inadvertently activates the scope unconditionally, this
        // test catches the leak.
        var tree = Div(Class: "x", Id: "y")[Span()["hi"]];

        var sb = new StringBuilder();
        HtmlSerializer.Serialize(tree, sb);

        // Per CLAUDE.md: rendered attribute order is id, class, style, data-*, tag-specific.
        Assert.Equal("<div id=\"y\" class=\"x\"><span>hi</span></div>", sb.ToString());
        Assert.Null(FrameSinkScope.Current);
    }

    [Fact]
    public void Serialize_DoctypeAndFragment_EmitsDoctypeFrameAndWalksFragmentChildren()
    {
        var tree = Fragment()[Doctype(), Div()["hi"]];

        var frames = RenderAndCaptureFrames(tree);

        Assert.Equal(3, frames.Length);
        Assert.Equal(RenderFrameKind.Doctype, frames[0].Kind);
        Assert.Equal(RenderFrameKind.Element, frames[1].Kind);
        Assert.Equal("div", frames[1].Name);
        Assert.Equal(2, frames[1].SubtreeLength);
        Assert.Equal(RenderFrameKind.Text, frames[2].Kind);
    }

    [Fact]
    public void Serialize_SelfClosingElement_SubtreeLengthIsOne_AndSelfClosingFlagSet()
    {
        var tree = Br();

        var frames = RenderAndCaptureFrames(tree);

        Assert.Single(frames);
        Assert.Equal(RenderFrameKind.Element, frames[0].Kind);
        Assert.Equal("br", frames[0].Name);
        Assert.Equal(1, frames[0].SubtreeLength);
        Assert.True(frames[0].SelfClosing);
    }

    [Fact]
    public void Serialize_NestedAttributes_PreservedInOrder()
    {
        // Element + multiple attributes: SubtreeLength must include all of them so a
        // diff consumer can skip the whole element by jumping (i + SubtreeLength).
        var tree = A("https://example.com", "_blank", "noopener", Class: "lnk", Id: "go")["open"];

        var frames = RenderAndCaptureFrames(tree);

        var anchor = frames[0];
        Assert.Equal(RenderFrameKind.Element, anchor.Kind);
        Assert.Equal("a", anchor.Name);
        // 1 element + N attributes + 1 text = anchor.SubtreeLength
        var attrCount = 0;
        for (var i = 1; i < frames.Length - 1; i++)
        {
            if (frames[i].Kind == RenderFrameKind.Attribute)
            {
                attrCount++;
            }
        }

        Assert.Equal(frames.Length, anchor.SubtreeLength);
        Assert.True(attrCount >= 3, $"expected at least 3 attributes (id/class + href), got {attrCount}");
        Assert.Equal(RenderFrameKind.Text, frames[^1].Kind);
        Assert.Equal("open", frames[^1].Name);
    }

    private static RenderFrame[] RenderAndCaptureFrames(Component tree)
    {
        var sb = new StringBuilder();
        var frames = new FrameWriter();
        using (FrameSinkScope.Push(frames))
        {
            HtmlSerializer.Serialize(tree, sb);
        }

        return frames.WrittenSpan.ToArray();
    }
}
