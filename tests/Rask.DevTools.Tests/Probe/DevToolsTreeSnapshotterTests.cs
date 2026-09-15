using Rask.Core;
using Rask.Core.Live;
using Rask.DevTools.Panel;
using Rask.DevTools.Probe;
using Rask.TestSupport;

namespace Rask.DevTools.Tests.Probe;

/// <summary>
///     How a captured walk becomes the tree the panel shows: nested as the page is, with the elements between the
///     components read from the same frames the diff reads.
/// </summary>
public sealed class DevToolsTreeSnapshotterTests
{
    [Fact]
    public void A_component_sits_under_the_component_it_was_walked_inside_not_the_one_that_built_it()
    {
        var root = Stub();
        var wrapper = Stub();
        var inner = Stub();

        // The walk reports each component when it finishes, so the inner one comes first.
        var tree = Snapshot(root, frames: null, (inner, wrapper, -1, -1), (wrapper, root, -1, -1));

        var wrapperNode = Assert.Single(tree.Children);
        Assert.Equal(nameof(StubComponent), wrapperNode.Type);
        var innerNode = Assert.Single(wrapperNode.Children);
        Assert.Empty(innerNode.Children);
        Assert.NotEqual(wrapperNode.Id, innerNode.Id);
    }

    [Fact]
    public void The_elements_around_and_inside_a_component_come_from_the_frames()
    {
        var root = Stub();
        var child = Stub();

        // <div> [child: <p>hi</p>] <button></button> </div>
        using var frames = new FrameWriter();
        var div = frames.OpenElement("div", null, false, 0);
        var childStart = frames.Count;
        var p = frames.OpenElement("p", null, false, 5);
        frames.Text("hi", 8, 10);
        frames.CloseElement(p, 14);
        var childEnd = frames.Count;
        var button = frames.OpenElement("button", null, false, 14);
        frames.CloseElement(button, 31);
        frames.CloseElement(div, 37);

        var tree = Snapshot(root, frames, (child, root, childStart, childEnd));

        var divNode = Assert.Single(tree.Children);
        Assert.True(divNode.IsTag);
        Assert.Equal("div", divNode.Type);
        Assert.Collection(
            divNode.Children,
            c =>
            {
                Assert.False(c.IsTag);
                Assert.Equal("p", Assert.Single(c.Children).Type);
            },
            b =>
            {
                Assert.True(b.IsTag);
                Assert.Equal("button", b.Type);
            });
    }

    // What the page box is drawn from: the coordinates the diff patches by, for a component and for an element alike.
    [Fact]
    public void Every_node_says_where_it_is_on_the_page()
    {
        var root = Stub();
        var child = Stub();
        var empty = Stub();

        // <div> [child: <p>hi</p>] [empty: nothing] <button></button> </div>
        using var frames = new FrameWriter();
        var div = frames.OpenElement("div", null, false, 0);
        var childStart = frames.Count;
        var p = frames.OpenElement("p", null, false, 5);
        frames.Text("hi", 8, 10);
        frames.CloseElement(p, 14);
        var childEnd = frames.Count;
        var button = frames.OpenElement("button", null, false, 14);
        frames.CloseElement(button, 31);
        frames.CloseElement(div, 37);

        var tree = Snapshot(root, frames, (child, root, childStart, childEnd), (empty, root, childEnd, childEnd));

        // The root rendered the document; nobody hovers for that box.
        Assert.Null(tree.At);
        var divNode = Assert.Single(tree.Children);
        Assert.Equal("|0|1", divNode.At);
        var childNode = divNode.Children[0];
        // The component's one <p>: inside the <div> (slot 0 at the top), first slot 0, one node.
        Assert.Equal("0|0|1", childNode.At);
        Assert.Equal("0|0|1", Assert.Single(childNode.Children).At);
        // A component that rendered nothing has nothing to box.
        Assert.Null(divNode.Children[1].At);
        Assert.Equal("0|1|1", divNode.Children[2].At);
    }

    [Fact]
    public void Ids_survive_a_second_snapshot_for_components_and_their_elements_alike()
    {
        var root = Stub();
        using var frames = new FrameWriter();
        var ul = frames.OpenElement("ul", null, false, 0);
        var li = frames.OpenElement("li", null, false, 4);
        frames.Text("one", 8, 11);
        frames.CloseElement(li, 16);
        frames.CloseElement(ul, 21);

        var snapshots = new DevToolsTreeSnapshotter();
        var capture = new DevToolsTreeCapture();
        capture.Record(root, [], frames);

        var first = snapshots.Snapshot(capture)!;
        var second = snapshots.Snapshot(capture)!;

        // What a panel keys its opened branches on: a <ul> the developer opened stays open when the page renders again.
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Children[0].Id, second.Children[0].Id);
        Assert.Equal(first.Children[0].Children[0].Id, second.Children[0].Children[0].Id);
        Assert.NotEqual(first.Children[0].Id, first.Children[0].Children[0].Id);
    }

    [Fact]
    public void Leaving_the_tags_out_lifts_what_was_inside_them_up_to_the_component()
    {
        var root = Stub();
        var child = Stub();
        using var frames = new FrameWriter();
        var div = frames.OpenElement("div", null, false, 0);
        var childStart = frames.Count;
        var span = frames.OpenElement("span", null, false, 5);
        frames.Text("x", 11, 12);
        frames.CloseElement(span, 19);
        var childEnd = frames.Count;
        frames.CloseElement(div, 25);

        var tree = Snapshot(root, frames, (child, root, childStart, childEnd));
        var components = DevToolsTreeTab.WithoutTags(tree.Children);

        var only = Assert.Single(components);
        Assert.False(only.IsTag);
        Assert.Empty(only.Children);
    }

    // A snapshot reads what a component IS, never renders it: the walk already happened.
#pragma warning disable RASK014 // a test stand-in the snapshot names by identity; no chain builds a capture by hand
    [Fact]
    public void An_island_or_a_blazor_component_is_badged_from_the_element_it_renders_and_nothing_else_is()
    {
        var root = Stub();
        var island = Stub();
        var blazor = Stub();
        var plain = Stub();

        // <section> [island: <rask-external runtime=lit>] [blazor: <rask-blazor>] [plain: <p>] </section>
        using var frames = new FrameWriter();
        var section = frames.OpenElement("section", null, false, 0);
        var islandStart = frames.Count;
        var host = frames.OpenElement("rask-external", null, false, 9, opaque: true);
        frames.Attribute("name", "Chart");
        frames.Attribute("runtime", "lit");
        frames.CloseElement(host, 60);
        var islandEnd = frames.Count;
        var blazorStart = frames.Count;
        var blazorHost = frames.OpenElement("rask-blazor", null, false, 60);
        frames.CloseElement(blazorHost, 90);
        var blazorEnd = frames.Count;
        var plainStart = frames.Count;
        var p = frames.OpenElement("p", null, false, 90);
        frames.CloseElement(p, 97);
        var plainEnd = frames.Count;
        frames.CloseElement(section, 107);

        var tree = Snapshot(root, frames,
            (island, root, islandStart, islandEnd), (blazor, root, blazorStart, blazorEnd), (plain, root, plainStart, plainEnd));

        var components = Assert.Single(tree.Children).Children;
        Assert.Equal(["Lit", "Blazor", null], components.Select(c => c.Badge));
        // The island still has a place on the page, so hovering and picking reach it.
        Assert.NotNull(components[0].At);
    }

    private static StubComponent Stub() => new(() => throw new InvalidOperationException("a snapshot never renders"));
#pragma warning restore RASK014

    private static DevToolsComponentNode Snapshot(
        Component root, FrameWriter? frames, params (Component Component, Component Parent, int Start, int End)[] walk)
    {
        var capture = new DevToolsTreeCapture();
        capture.Record(root, walk.Select(w => new DevToolsWalkItem(w.Component, w.Parent, w.Start, w.End)).ToList(), frames);
        return new DevToolsTreeSnapshotter().Snapshot(capture)!;
    }
}
