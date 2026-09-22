using Rask.Core.Live;
using Rask.DevTools.Panel;
using Rask.DevTools.Probe;
using Rask.Server.Tests.Infrastructure;

namespace Rask.DevTools.Tests.Panel;

/// <summary>
///     The component tree the panel shows: built from the inspected page's last render walk, and only while a panel is
///     watching.
/// </summary>
[Collection(DevToolsHookCollection.Name)]
public sealed class DevToolsTreeTabTests
{
    [Fact]
    public async Task A_page_that_renders_while_a_tab_watches_hands_it_a_tree()
    {
        using var host = Host();
        var (session, feed, socket, handlerId) = await LivePage(host);
        using var watch = feed.WatchTree();

        // The shape the client really sends for a click, so this drives the page's own dispatch path.
        await socket.SendJsonAsync(new { id = handlerId, type = "click" });

        // First that the page actually dispatched: a tree can only come from a render, and the wire feed says whether one
        // was asked for at all.
        var dispatched = await WaitFor(
            () => feed.WireSnapshot().Any(e => e.Kind == "click"), TimeSpan.FromSeconds(5));

        Assert.True(dispatched, "the page never received the click, so it never re-rendered");

        var tree = await WaitForTree(feed, TimeSpan.FromSeconds(5));

        Assert.NotNull(tree);
        // The app's own component, somewhere under the root the host wraps it in.
        Assert.Contains(nameof(DevToolsTestApp), Types(tree!));
        socket.Dispose();
    }

    [Fact]
    public async Task With_no_panel_watching_no_tree_is_built()
    {
        using var host = Host();
        var (_, feed, socket, handlerId) = await LivePage(host);

        // The shape the client really sends for a click, so this drives the page's own dispatch path.
        await socket.SendJsonAsync(new { id = handlerId, type = "click" });
        await Task.Delay(500);

        Assert.Null(feed.TreeSnapshot());
        socket.Dispose();
    }

    [Fact]
    public async Task A_component_keeps_its_id_across_renders()
    {
        using var host = Host();
        var (_, feed, socket, handlerId) = await LivePage(host);
        using var watch = feed.WatchTree();

        // The shape the client really sends for a click, so this drives the page's own dispatch path.
        await socket.SendJsonAsync(new { id = handlerId, type = "click" });
        var first = await WaitForTree(feed, TimeSpan.FromSeconds(5));

        Assert.NotNull(first);

        // The shape the client really sends for a click, so this drives the page's own dispatch path.
        await socket.SendJsonAsync(new { id = handlerId, type = "click" });
        await Task.Delay(300);
        var second = feed.TreeSnapshot();

        // The id is what a panel keys its expanded branches on, so it must outlive the render it was taken in.
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);
        socket.Dispose();
    }

    [Fact]
    public async Task A_node_carries_the_props_its_component_was_given()
    {
        using var host = Host();
        var (_, feed, socket, handlerId) = await LivePage(host);
        using var watch = feed.WatchTree();

        // The shape the client really sends for a click, so this drives the page's own dispatch path.
        await socket.SendJsonAsync(new { id = handlerId, type = "click" });

        var tree = await WaitForTree(feed, TimeSpan.FromSeconds(5));

        Assert.NotNull(tree);

        // Read back through the snapshot: the value the parent passed, by name. The override that reads it is
        // written by the build and only when the build asks for the devtools — which this test project does.
        var child = Nodes(tree!).Single(n => n.Type == nameof(DevToolsTestChild));
        var caption = child.Props.Single(p => p.Name == nameof(DevToolsTestChild.Caption));
        Assert.Equal("hello", caption.Value);
        socket.Dispose();
    }

    // The first thing a developer does is open the panel on a page they have not touched yet. The page rendered once, for
    // the GET, and that render is what the tree is built from — not a render the panel would have to wait for.
    [Fact]
    public async Task A_panel_opened_before_any_interaction_gets_the_tree_the_page_was_served_with()
    {
        using var host = Host();
        var (_, feed, socket, _) = await LivePage(host);

        using var watch = feed.WatchTree();

        var tree = await WaitForTree(feed, TimeSpan.FromSeconds(5));

        Assert.NotNull(tree);
        Assert.Contains(nameof(DevToolsTestChild), Types(tree!));
        socket.Dispose();
    }

    [Fact]
    public async Task A_child_sits_under_the_component_it_is_rendered_inside()
    {
        using var host = Host();
        var (_, feed, socket, _) = await LivePage(host);
        using var watch = feed.WatchTree();

        var tree = await WaitForTree(feed, TimeSpan.FromSeconds(5));

        Assert.NotNull(tree);

        // The app built the child, and the frame renders it: the page's nesting puts it under the frame.
        var components = DevToolsTreeTab.WithoutTags([tree!]).Single();
        var frame = Nodes(components).Single(n => n.Type == nameof(DevToolsTestFrame));
        Assert.Equal(nameof(DevToolsTestChild), Assert.Single(frame.Children).Type);
        socket.Dispose();
    }

    [Fact]
    public async Task The_elements_between_components_are_in_the_tree_as_tags()
    {
        using var host = Host();
        var (_, feed, socket, _) = await LivePage(host);
        using var watch = feed.WatchTree();

        var tree = await WaitForTree(feed, TimeSpan.FromSeconds(5));

        Assert.NotNull(tree);

        // The frame's own <section>, and the child's <span> inside it.
        var frame = Nodes(tree!).Single(n => n.Type == nameof(DevToolsTestFrame));
        var section = Assert.Single(frame.Children);
        Assert.True(section.IsTag);
        Assert.Equal("section", section.Type);
        var child = Assert.Single(section.Children);
        Assert.Equal(nameof(DevToolsTestChild), child.Type);
        Assert.Equal("span", Assert.Single(child.Children).Type);
        socket.Dispose();
    }

    // A page made of the kit's own components, the way an app is: the tree must hold each element once, under the
    // component that rendered it. A pick matches the page against these nodes, so a copy elsewhere is a pick that lands on
    // a node the tree never shows.
    [Fact]
    public async Task A_kit_page_holds_every_node_once()
    {
        using var host = DevToolsLivePage.Host<DevToolsKitTestApp>();
        var (_, feed, socket, _) = await LivePage(host);
        using var watch = feed.WatchTree();

        var tree = await WaitForTree(feed, TimeSpan.FromSeconds(5));

        Assert.NotNull(tree);

        var all = Nodes(tree!).ToList();
        var duplicates = all.GroupBy(n => n.Id).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(n => n.Type))}").ToList();
        Assert.True(duplicates.Count == 0, "nodes in the tree twice: " + string.Join("; ", duplicates));
        var button = Assert.Single(all, n => n is { IsTag: true, Type: "button" });
        Assert.NotNull(button.At);
        socket.Dispose();
    }

    private static IEnumerable<DevToolsComponentNode> Nodes(DevToolsComponentNode node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Nodes))
        {
            yield return child;
        }
    }

    private static IEnumerable<string> Types(DevToolsComponentNode node)
    {
        yield return node.Type;
        foreach (var type in node.Children.SelectMany(Types))
        {
            yield return type;
        }
    }

    private static async Task<DevToolsComponentNode?> WaitForTree(DevToolsFeed feed, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (feed.TreeSnapshot() is { } tree)
            {
                return tree;
            }

            await Task.Delay(25);
        }

        return feed.TreeSnapshot();
    }

    private static RaskTestHost Host() => DevToolsLivePage.Host<DevToolsTestApp>();

    private static Task<(LiveSessionBase Session, DevToolsFeed Feed, System.Net.WebSockets.WebSocket Socket, string HandlerId)>
        LivePage(RaskTestHost host) => DevToolsLivePage.OpenAsync(host);

    private static Task<bool> WaitFor(Func<bool> until, TimeSpan timeout) => DevToolsLivePage.WaitFor(until, timeout);
}
