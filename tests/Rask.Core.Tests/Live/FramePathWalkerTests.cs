using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

/// <summary>
///     <see cref="FramePathWalker" /> turns a component's frame span into the DOM location the client patches.
///     A wrong slot highlights the wrong element, so each rule the differ uses is pinned here.
/// </summary>
/// <remarks>
///     Every text frame is written with its own non-empty HTML range (<c>frames.Count * 16</c> to one past it).
///     <see cref="FrameWriter.Text" /> follows the browser: text that emitted no HTML produces no frame, and text
///     whose range starts where the previous text's ended is merged into it. A fixture writing <c>(0, 0)</c> therefore
///     writes nothing at all — which is how the first cut of these tests failed while the walker was right.
/// </remarks>
public sealed class FramePathWalkerTests
{
    [Fact]
    public void A_root_level_text_node_is_slot_zero_of_the_root()
    {
        using var frames = new FrameWriter();
        frames.Text("hello", 0, 5);

        Assert.True(Resolve(frames, 0, 1, out var path, out var slot, out var count));
        Assert.Empty(path);
        Assert.Equal(0, slot);
        Assert.Equal(1, count);
    }

    [Fact]
    public void A_nested_element_is_addressed_through_its_parent_and_skips_attributes()
    {
        // <div class="a">x<span>y</span></div>
        using var frames = new FrameWriter();
        var div = frames.OpenElement("div", null, selfClosing: false, htmlStart: 0);
        frames.Attribute("class", "a");
        frames.Text("x", frames.Count * 16, frames.Count * 16 + 1);
        var span = frames.OpenElement("span", null, selfClosing: false, htmlStart: 0);
        frames.Text("y", frames.Count * 16, frames.Count * 16 + 1);
        frames.CloseElement(span, 0);
        frames.CloseElement(div, 0);

        // The span: second child of the div (the text "x" is the first), one DOM node.
        Assert.True(Resolve(frames, span, span + 2, out var spanPath, out var spanSlot, out var spanCount));
        Assert.Equal([0], spanPath);
        Assert.Equal(1, spanSlot);
        Assert.Equal(1, spanCount);

        // The text inside the span.
        Assert.True(Resolve(frames, span + 1, span + 2, out var textPath, out var textSlot, out var textCount));
        Assert.Equal([0, 1], textPath);
        Assert.Equal(0, textSlot);
        Assert.Equal(1, textCount);
    }

    [Fact]
    public void A_span_that_renders_nothing_cannot_be_located()
    {
        // <div class="a">{an empty component}</div>: the component's span is [2, 2), and 2 is also where a sibling
        // AFTER the div would start. The frame stream cannot tell "last inside the div" from "next after it", and an
        // empty span has no DOM node to point at anyway — so the walker refuses rather than guess a level.
        using var frames = new FrameWriter();
        var div = frames.OpenElement("div", null, selfClosing: false, htmlStart: 0);
        frames.Attribute("class", "a");
        var emptyChildAt = frames.Count;
        frames.CloseElement(div, 0);

        Assert.Equal(frames.Count, emptyChildAt);
        Assert.False(Resolve(frames, emptyChildAt, emptyChildAt, out _, out _, out _));
    }

    [Fact]
    public void A_span_of_several_siblings_counts_every_dom_node_but_not_attributes_or_nested_nodes()
    {
        // <ul><li>1</li><li>2</li></ul> followed by text
        using var frames = new FrameWriter();
        var ul = frames.OpenElement("ul", null, selfClosing: false, htmlStart: 0);
        var li1 = frames.OpenElement("li", null, selfClosing: false, htmlStart: 0);
        frames.Text("1", frames.Count * 16, frames.Count * 16 + 1);
        frames.CloseElement(li1, 0);
        var li2 = frames.OpenElement("li", null, selfClosing: false, htmlStart: 0);
        frames.Text("2", frames.Count * 16, frames.Count * 16 + 1);
        frames.CloseElement(li2, 0);
        frames.CloseElement(ul, 0);
        frames.Text("tail", frames.Count * 16, frames.Count * 16 + 1);

        Assert.True(Resolve(frames, 0, frames.Count, out var path, out var slot, out var count));
        Assert.Empty(path);
        Assert.Equal(0, slot);
        Assert.Equal(2, count);
    }

    [Fact]
    public void A_span_starting_inside_attributes_cannot_be_located()
    {
        using var frames = new FrameWriter();
        var div = frames.OpenElement("div", null, selfClosing: false, htmlStart: 0);
        frames.Attribute("id", "x");
        frames.CloseElement(div, 0);

        Assert.False(Resolve(frames, div + 1, div + 2, out _, out _, out _));
    }

    [Fact]
    public void An_opaque_subtree_belongs_to_another_renderer_and_is_not_entered()
    {
        using var frames = new FrameWriter();
        var island = frames.OpenElement("rask-external", null, selfClosing: false, htmlStart: 0, opaque: true);
        frames.Text("react owns this", frames.Count * 16, frames.Count * 16 + 1);
        frames.CloseElement(island, 0);

        Assert.False(Resolve(frames, island + 1, island + 2, out _, out _, out _));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 5)]
    [InlineData(1, 0)]
    public void A_span_outside_the_stream_cannot_be_located(int start, int end)
    {
        using var frames = new FrameWriter();
        frames.Text("x", 0, 1);

        Assert.False(Resolve(frames, start, end, out _, out _, out _));
    }

    [Fact]
    public void A_text_node_resolves_to_the_path_the_differ_patches_it_at()
    {
        // The walker exists so the devtools highlight the node the diff actually touches. If the two ever disagree
        // on a slot, a flash lands on the wrong element — so the differ's own op path is the reference.
        using var before = new FrameWriter();
        using var after = new FrameWriter();
        var textIndex = WriteRow(before, "old");
        WriteRow(after, "new");

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before.WrittenSpan, after.WrittenSpan, ops);

        var update = Assert.Single(ops, op => op.Kind == EditOpKind.UpdateText);
        Assert.True(Resolve(after, textIndex, textIndex + 1, out var path, out var slot, out _));
        int[] walkerPath = [.. path, slot];
        Assert.Equal(update.Path, walkerPath);
    }

    [Fact]
    public void A_deeply_nested_text_node_resolves_to_the_path_the_differ_patches_it_at()
    {
        using var before = new FrameWriter();
        using var after = new FrameWriter();
        var textIndex = WriteNested(before, "old");
        WriteNested(after, "new");

        var ops = new List<EditOp>();
        FrameDiffer.Diff(before.WrittenSpan, after.WrittenSpan, ops);

        var update = Assert.Single(ops, op => op.Kind == EditOpKind.UpdateText);
        Assert.True(Resolve(after, textIndex, textIndex + 1, out var path, out var slot, out _));
        int[] walkerPath = [.. path, slot];
        Assert.Equal(update.Path, walkerPath);
    }

    // <p class="row">label: <b>{value}</b></p> — returns the index of the {value} text frame.
    private static int WriteRow(FrameWriter frames, string value)
    {
        var p = frames.OpenElement("p", null, selfClosing: false, htmlStart: 0);
        frames.Attribute("class", "row");
        frames.Text("label: ", frames.Count * 16, frames.Count * 16 + 1);
        var b = frames.OpenElement("b", null, selfClosing: false, htmlStart: 0);
        var textIndex = frames.Count;
        frames.Text(value, frames.Count * 16, frames.Count * 16 + 1);
        frames.CloseElement(b, 0);
        frames.CloseElement(p, 0);
        return textIndex;
    }

    // <main><h1>t</h1><section id="s"><ul><li>a</li><li>{value}</li></ul></section></main>
    private static int WriteNested(FrameWriter frames, string value)
    {
        var main = frames.OpenElement("main", null, selfClosing: false, htmlStart: 0);
        var h1 = frames.OpenElement("h1", null, selfClosing: false, htmlStart: 0);
        frames.Text("t", frames.Count * 16, frames.Count * 16 + 1);
        frames.CloseElement(h1, 0);
        var section = frames.OpenElement("section", null, selfClosing: false, htmlStart: 0);
        frames.Attribute("id", "s");
        var ul = frames.OpenElement("ul", null, selfClosing: false, htmlStart: 0);
        var li1 = frames.OpenElement("li", null, selfClosing: false, htmlStart: 0);
        frames.Text("a", frames.Count * 16, frames.Count * 16 + 1);
        frames.CloseElement(li1, 0);
        var li2 = frames.OpenElement("li", null, selfClosing: false, htmlStart: 0);
        var textIndex = frames.Count;
        frames.Text(value, frames.Count * 16, frames.Count * 16 + 1);
        frames.CloseElement(li2, 0);
        frames.CloseElement(ul, 0);
        frames.CloseElement(section, 0);
        frames.CloseElement(main, 0);
        return textIndex;
    }

    private static bool Resolve(FrameWriter frames, int start, int end, out List<int> path, out int slot, out int count)
    {
        path = [];
        return FramePathWalker.TryResolve(frames.WrittenSpan, start, end, path, out slot, out count);
    }
}
