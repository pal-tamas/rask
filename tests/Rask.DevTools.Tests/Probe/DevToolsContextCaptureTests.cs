using Rask.DevTools.Probe;
using Rask.Server.Tests.Infrastructure;

namespace Rask.DevTools.Tests.Probe;

/// <summary>
///     What a real page's context puts into the tree, through the probe the host installs: recorded from the first render a
///     panel watches, never before.
/// </summary>
[Collection(DevToolsHookCollection.Name)]
public sealed class DevToolsContextCaptureTests
{
    [Fact]
    public async Task While_a_panel_shows_the_tree_each_component_carries_what_it_provides_and_reads()
    {
        using var host = DevToolsLivePage.Host<DevToolsContextTestApp>();
        var (_, feed, socket, handlerId) = await DevToolsLivePage.OpenAsync(host);
        using var watch = feed.WatchTree();

        // Built from the render before anyone watched: the tree is there, its context is not known yet.
        Assert.True(await DevToolsLivePage.WaitFor(() => feed.TreeSnapshot() is not null, TimeSpan.FromSeconds(5)));
        Assert.Null(Node(feed, nameof(DevToolsContextReader))?.Reads);

        // The shape the client really sends for a click, so the next render is the page's own.
        await socket.SendJsonAsync(new { id = handlerId, type = "click" });
        Assert.True(await DevToolsLivePage.WaitFor(
            () => Node(feed, nameof(DevToolsContextReader))?.Reads is not null,
            TimeSpan.FromSeconds(5)), "no render recorded context after the click");

        var shell = Node(feed, nameof(DevToolsContextShell))!;
        Assert.Equal(
            [new DevToolsProvidedContext(nameof(DevToolsTestTheme), null, "DevToolsTestTheme { Name = dark }", false),
             new DevToolsProvidedContext("String", "api-token", "••••", true)],
            shell.Provides!);

        var reader = Node(feed, nameof(DevToolsContextReader))!;
        Assert.Equal(
            [new DevToolsReadContext(nameof(DevToolsTestTheme), null, true, shell.Id, nameof(DevToolsContextShell)),
             new DevToolsReadContext("Int32", "page-size", false, null, null),
             new DevToolsReadContext("String", "api-token", true, shell.Id, nameof(DevToolsContextShell))],
            reader.Reads!);
        Assert.Empty(Node(feed, nameof(DevToolsContextTestApp))!.Reads!);
        socket.Dispose();
    }

    [Fact]
    public async Task Without_a_panel_watching_no_context_is_recorded()
    {
        using var host = DevToolsLivePage.Host<DevToolsContextTestApp>();
        var (_, feed, socket, handlerId) = await DevToolsLivePage.OpenAsync(host);
        var before = feed.CommitsSnapshot().Length;

        await socket.SendJsonAsync(new { id = handlerId, type = "click" });
        Assert.True(await DevToolsLivePage.WaitFor(() => feed.CommitsSnapshot().Length > before, TimeSpan.FromSeconds(5)));

        using var watch = feed.WatchTree();
        Assert.True(await DevToolsLivePage.WaitFor(() => feed.TreeSnapshot() is not null, TimeSpan.FromSeconds(5)));
        Assert.Null(Node(feed, nameof(DevToolsContextShell))?.Provides);
        socket.Dispose();
    }

    private static DevToolsComponentNode? Node(DevToolsFeed feed, string type) =>
        feed.TreeSnapshot() is { } tree ? Nodes(tree).FirstOrDefault(n => n.Type == type) : null;

    private static IEnumerable<DevToolsComponentNode> Nodes(DevToolsComponentNode node) =>
        node.Children.SelectMany(Nodes).Prepend(node);
}
