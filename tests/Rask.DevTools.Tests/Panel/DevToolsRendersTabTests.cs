using Rask.Core;
using Rask.Core.Diagnostics.DevTools;
using Rask.DevTools.Panel;
using Rask.DevTools.Probe;
using Rask.Testing;

namespace Rask.DevTools.Tests.Panel;

/// <summary>
///     The Renders tab over a feed filled by hand: totals per component, the commit log, and Clear.
/// </summary>
public sealed class DevToolsRendersTabTests
{
#pragma warning disable RASK014 // components and the tab made by hand, the way the walk and the panel page would
    private static Component Child(string? key = null) => new DevToolsTestChild { Key = key };

    private static Component Board() => new DevToolsTestFrame();

    private static Page Render(DevToolsFeed feed) => Page.Render(new DevToolsRendersTab { Feed = feed });
#pragma warning restore RASK014

    // A board mounts with three rows; then two clicks re-render the board and one row.
    private static DevToolsFeed Feed()
    {
        var feed = new DevToolsFeed();
        var ids = new DevToolsTreeSnapshotter();
        var board = Board();
        var rows = new[] { Child("a"), Child("b"), Child("c") };

        List<DevToolsRenderItem> Items(params (Component, RenderCause)[] renders) =>
            renders.Select(r => new DevToolsRenderItem(r.Item1, r.Item2, SelfTicks: 5)).ToList();

        feed.RecordCommit(
            Items((board, RenderCause.Uncached), (rows[0], RenderCause.Props), (rows[1], RenderCause.Props),
                (rows[2], RenderCause.Props)),
            walked: 5, ids, timestamp: 1);
        feed.RecordCommit(Items((board, RenderCause.State), (rows[1], RenderCause.Props)), walked: 5, ids, timestamp: 2);
        feed.RecordCommit(Items((board, RenderCause.State)), walked: 5, ids, timestamp: 3);
        return feed;
    }

    [Fact]
    public void An_empty_log_says_how_to_fill_it()
    {
        var page = Render(new DevToolsFeed());

        Assert.Contains("No renders yet", page.Html);
        Assert.Empty(page.FindAll("tbody tr"));
    }

    [Fact]
    public void By_component_lists_each_instance_most_renders_first_with_why()
    {
        var page = Render(Feed());

        var rows = page.FindAll("tbody tr").Select(r => r.Children.Select(td => td.TextContent.Trim()).ToArray()).ToList();
        Assert.Equal(4, rows.Count);
        // The board: three renders, a mount and two for its state.
        Assert.Equal(nameof(DevToolsTestFrame), rows[0][0]);
        Assert.Equal("3", rows[0][1]);
        Assert.Contains("mount", rows[0][2]);
        Assert.Contains("state ×2", rows[0][2]);
        // Row b rendered twice — a mount, then new props — and is named with its key.
        Assert.Equal(nameof(DevToolsTestChild) + " b", rows[1][0]);
        Assert.Equal("2", rows[1][1]);
        Assert.Contains("props", rows[1][2]);
        Assert.DoesNotContain("props ×", rows[1][2]);
    }

    [Fact]
    public async Task By_commit_lists_the_newest_commit_first_with_repeats_folded()
    {
        var page = Render(Feed());

        await page.On("[aria-pressed=\"false\"]").ClickAsync();

        var rows = page.FindAll("tbody tr");
        Assert.Equal(3, rows.Count);
        Assert.Contains("1 of 5", rows[0].TextContent);
        // The mount commit: three rows rendered for the same reason, named once.
        Assert.Contains(nameof(DevToolsTestChild) + " ×3", rows[2].TextContent);
        Assert.Contains(nameof(DevToolsTestFrame), rows[2].TextContent);
        Assert.Contains("4 of 5", rows[2].TextContent);
    }

    [Fact]
    public async Task Clear_empties_the_tab()
    {
        var feed = Feed();
        var page = Render(feed);

        await page.On("button[title=\"Forget the renders counted so far\"]").ClickAsync();

        Assert.Empty(feed.CommitsSnapshot());
        Assert.Contains("No renders yet", page.Html);
    }
}
