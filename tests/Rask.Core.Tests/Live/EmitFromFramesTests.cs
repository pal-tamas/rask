using System.Text;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined component subtrees have no generated factories

namespace Rask.Core.Tests.Live;

// Phase B clean-subtree replay: EmitFromFrames must reconstruct a subtree's HTML byte-for-byte
// from its captured RenderFrame span, matching what HtmlSerializer.Serialize emitted. This is the
// path that lets a clean component re-emit without retaining its Element object graph.
public class EmitFromFramesTests
{
    // Capture frames by serializing the tree with a frame sink active, then replay from the frames
    // and assert the two HTML strings are identical.
    private static void AssertReplayMatches(Component tree)
    {
        var writer = new FrameWriter();
        var direct = new StringBuilder();
        using (FrameSinkScope.Push(writer))
        {
            HtmlSerializer.Serialize(tree, direct);
        }

        var replayed = new StringBuilder();
        HtmlSerializer.EmitFromFrames(writer.WrittenSpan, replayed);

        Assert.Equal(direct.ToString(), replayed.ToString());
    }

    [Fact]
    public void PlainDivWithText() => AssertReplayMatches(Div()["hello"]);

    [Fact]
    public void NestedElements() =>
        AssertReplayMatches(Div(Class: "container")[
            Div(Class: "row")[Span()["a"], Span()["b"]],
            Div(Class: "row")[Span()["c"]]
        ]);

    [Fact]
    public void AllUniversalAttributes() =>
        AssertReplayMatches(Div(
            Id: "i", Class: "c", Style: "color:red",
            Data: new Dictionary<string, string?> { ["row"] = "7", ["x"] = "y" },
            Role: "button", TabIndex: 3,
            Aria: new Dictionary<string, string?> { ["label"] = "Close", ["hidden"] = "true" })["body"]);

    [Fact]
    public void KeyedElement() => AssertReplayMatches(Li(Key: 42, Class: "item")["row"]);

    [Fact]
    public void SelfClosingElements() =>
        AssertReplayMatches(Div()[
            Br(),
            Hr(Class: "sep"),
            Img(Src: "/logo.png", Alt: "logo", Class: "logo")
        ]);

    [Fact]
    public void AnchorWithHref() => AssertReplayMatches(A("/item/5", Class: "lnk")["open 5"]);

    [Fact]
    public void RawMarkup() => AssertReplayMatches(Div()[Raw("<b>bold</b> & <i>x</i>")]);

    [Fact]
    public void AdjacentTextCoalesced() => AssertReplayMatches(Div()["Score: ", 42, " pts"]);

    [Fact]
    public void EncodedTextAndAttributeValues() =>
        AssertReplayMatches(Div(Id: "a+b", Class: "x{y}")["<tag> & 'quote' \"dq\" + [brackets]"]);

    [Fact]
    public void DraggableAndValuelessShape() =>
        AssertReplayMatches(Div(Draggable: true, Class: "drag")["x"]);

    [Fact]
    public void DeepList200Rows()
    {
        var rows = new List<Component>(200);
        for (var i = 0; i < 200; i++)
        {
            rows.Add(Div(Key: i, Class: "row", Id: $"r{i}")[
                Span(Class: "label")[$"Item {i}"],
                A($"/item/{i}", Class: "lnk")[$"open {i}"]
            ]);
        }

        AssertReplayMatches(Div(Class: "body")[rows]);
    }

    [Fact]
    public void FragmentChildrenAreFlatInFrames() =>
        AssertReplayMatches(Div()[Fragment()[Span()["a"], Span()["b"]], Span()["c"]]);
}
