using Rask.DevTools.Probe;
using Rask.Server.Tests.Infrastructure;

namespace Rask.DevTools.Tests.Probe;

/// <summary>
///     What a real page's renders put into the feed, through the probe the host installs: the page served, then a click.
/// </summary>
[Collection(DevToolsHookCollection.Name)]
public sealed class DevToolsRenderCaptureTests
{
    [Fact]
    public async Task The_page_the_host_served_is_logged_as_mounts()
    {
        using var host = DevToolsLivePage.Host<DevToolsTestApp>();
        var (_, feed, socket, _) = await DevToolsLivePage.OpenAsync(host);

        var first = feed.CommitsSnapshot().FirstOrDefault();
        Assert.NotNull(first);
        var app = Assert.Single(first!.Renders, r => r.Type == nameof(DevToolsTestApp));
        Assert.Equal(DevToolsRenderReason.Mount, app.Reason);
        Assert.Contains(first.Renders, r => r.Type == nameof(DevToolsTestChild));
        // Its own Render() was timed; the walk passed through at least every component that rendered.
        Assert.True(app.SelfTicks >= 0);
        Assert.True(first.Walked >= first.Renders.Length);
        socket.Dispose();
    }

    [Fact]
    public async Task A_click_renders_the_component_whose_state_it_changed_for_its_state()
    {
        using var host = DevToolsLivePage.Host<DevToolsTestApp>();
        var (_, feed, socket, handlerId) = await DevToolsLivePage.OpenAsync(host);
        var before = feed.CommitsSnapshot().Length;

        // The shape the client really sends for a click, so this drives the page's own dispatch path.
        await socket.SendJsonAsync(new { id = handlerId, type = "click" });

        var rendered = await DevToolsLivePage.WaitFor(
            () => feed.CommitsSnapshot().Skip(before).SelectMany(c => c.Renders)
                .Any(r => r.Type == nameof(DevToolsTestApp)),
            TimeSpan.FromSeconds(5));
        Assert.True(rendered, "no commit after the click rendered the app");

        var app = feed.CommitsSnapshot().Skip(before).SelectMany(c => c.Renders).First(r => r.Type == nameof(DevToolsTestApp));
        Assert.Equal(DevToolsRenderReason.State, app.Reason);
        socket.Dispose();
    }

    [Fact]
    public async Task A_render_names_the_component_by_the_id_the_tree_shows()
    {
        using var host = DevToolsLivePage.Host<DevToolsTestApp>();
        var (_, feed, socket, _) = await DevToolsLivePage.OpenAsync(host);
        using var watch = feed.WatchTree();

        Assert.True(await DevToolsLivePage.WaitFor(() => feed.TreeSnapshot() is not null, TimeSpan.FromSeconds(5)));
        var child = Nodes(feed.TreeSnapshot()!).Single(n => n.Type == nameof(DevToolsTestChild));
        var render = feed.CommitsSnapshot().SelectMany(c => c.Renders).First(r => r.Type == nameof(DevToolsTestChild));

        Assert.Equal(child.Id, render.Id);
        socket.Dispose();
    }

    private static IEnumerable<DevToolsComponentNode> Nodes(DevToolsComponentNode node) =>
        node.Children.SelectMany(Nodes).Prepend(node);
}
