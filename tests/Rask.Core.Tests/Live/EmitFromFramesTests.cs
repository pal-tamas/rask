using System.Text;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined component subtrees have no generated factories

namespace Rask.Core.Tests.Live;

// Phase B clean-subtree replay: ReplayLeanFrames must reconstruct a subtree's HTML byte-for-byte
// from its captured (leaned-down) frame span, matching what HtmlSerializer.Serialize emitted — and
// re-produce a frame stream identical to a fresh walk's. This is the path that lets a clean component
// re-emit without retaining its Element object graph.
public partial class EmitFromFramesTests : global::Rask.Core.RaskMarkup
{
    // Capture full frames by serializing the tree, lean them down (as the retained cache does), then
    // replay: assert the replayed HTML is byte-identical AND the replayed frame stream matches the
    // original walk's (same kinds/names/values/subtree lengths) so the diff sees no change.
    private static void AssertReplayMatches(Component tree)
    {
        var writer = new FrameWriter();
        var direct = new StringBuilder();
        using (FrameSinkScope.Push(writer))
        {
            HtmlSerializer.Serialize(tree, direct);
        }

        var original = writer.WrittenSpan.ToArray();
        var lean = new LeanFrame[original.Length];
        for (var i = 0; i < original.Length; i++)
        {
            lean[i] = new LeanFrame
            {
                Kind = original[i].Kind,
                Name = original[i].Name,
                Value = original[i].Value,
                SubtreeLength = original[i].SubtreeLength,
                SelfClosing = original[i].SelfClosing
            };
        }

        var replayWriter = new FrameWriter();
        var replayed = new StringBuilder();
        using (FrameSinkScope.Push(null))
        {
            HtmlSerializer.ReplayLeanFrames(lean, replayed, replayWriter);
        }

        Assert.Equal(direct.ToString(), replayed.ToString());

        // The re-written frame stream matches the original walk's structure + values.
        var reFrames = replayWriter.WrittenSpan;
        Assert.Equal(original.Length, reFrames.Length);
        for (var i = 0; i < original.Length; i++)
        {
            Assert.Equal(original[i].Kind, reFrames[i].Kind);
            Assert.Equal(original[i].Name, reFrames[i].Name);
            Assert.Equal(original[i].Value, reFrames[i].Value);
            Assert.Equal(original[i].SubtreeLength, reFrames[i].SubtreeLength);
            Assert.Equal(original[i].SelfClosing, reFrames[i].SelfClosing);
        }
    }

    [Fact]
    public void A_plain_div_with_text_replays_identically() => AssertReplayMatches(Div["hello"]);

    [Fact]
    public void Nested_elements_replay_identically() =>
        AssertReplayMatches(Div.Class("wrap")[
            Div.Class("line")[Span["a"], Span["b"]],
            Div.Class("line")[Span["c"]]
        ]);

    [Fact]
    public void All_universal_attributes_replay_identically() =>
        AssertReplayMatches(Div
            .Id("i")
            .Class("c")
            .Style("color:red")
            .Data(new Dictionary<string, string?> { ["row"] = "7", ["x"] = "y" })
            .Role("button")
            .TabIndex(3)
            .Aria(new Dictionary<string, string?> { ["label"] = "Close", ["hidden"] = "true" })["body"]);

    [Fact]
    public void A_keyed_element_replays_identically() => AssertReplayMatches(Li.Key(42).Class("item")["row"]);

    [Fact]
    public void Self_closing_elements_replay_identically() =>
        AssertReplayMatches(Div[
            Br,
            Hr.Class("sep"),
            Img.Src("/logo.png").Alt("logo").Class("logo")
        ]);

    [Fact]
    public void An_anchor_with_an_href_replays_identically() => AssertReplayMatches(A.Href("/item/5").Class("lnk")["open 5"]);

    [Fact]
    public void Raw_markup_replays_identically() => AssertReplayMatches(Div[Raw.Value("<b>bold</b> & <i>x</i>")]);

    [Fact]
    public void Coalesced_adjacent_text_replays_identically() => AssertReplayMatches(Div["Score: ", 42, " pts"]);

    [Fact]
    public void Encoded_text_and_attribute_values_replay_identically() =>
        AssertReplayMatches(Div.Id("a+b").Class("x{y}")["<tag> & 'quote' \"dq\" + [brackets]"]);

    [Fact]
    public void The_draggable_and_valueless_attribute_shape_replays_identically() =>
        AssertReplayMatches(Div.Draggable(true).Class("drag")["x"]);

    [Fact]
    public void A_deep_list_of_200_rows_replays_identically()
    {
        var rows = new List<Component>(200);
        for (var i = 0; i < 200; i++)
        {
            rows.Add(Div.Key(i).Class("line").Id($"r{i}")[
                Span.Class("label")[$"Item {i}"],
                A.Href($"/item/{i}").Class("lnk")[$"open {i}"]
            ]);
        }

        AssertReplayMatches(Div.Class("body")[rows]);
    }

    [Fact]
    public void Fragment_children_are_flat_in_the_frames() =>
        AssertReplayMatches(Div[Fragment[Span["a"], Span["b"]], Span["c"]]);
}
