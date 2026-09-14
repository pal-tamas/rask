using System.Diagnostics;
using Rask.Core;
using Rask.Core.Diagnostics.DevTools;
using Rask.DevTools.Panel;
using Rask.DevTools.Probe;
using Rask.Testing;

namespace Rask.DevTools.Tests.Panel;

/// <summary>
///     The Perf tab over a feed filled by hand, and the component that takes the page's patch times.
/// </summary>
public sealed class DevToolsPerfTabTests
{
    private static readonly long Ms = Stopwatch.Frequency / 1000;

#pragma warning disable RASK014 // the tab, its receiver and components made by hand, the way the panel page and a walk would
    private static RenderedComponent Render(DevToolsFeed feed) => RaskTest.Render(new DevToolsPerfTab { Feed = feed });

    private static RenderedComponent Receiver(DevToolsFeed feed) => RaskTest.Render(new DevToolsPatchReceiver { Feed = feed });

    private static Component Child() => new DevToolsTestChild();
#pragma warning restore RASK014

    private static DevToolsFeed Feed()
    {
        var feed = new DevToolsFeed();
        feed.PerfInbound("click", 0);
        feed.PerfHandlerStarted("Board", 0);
        feed.PerfHandlerEnded(0, 2 * Ms, faulted: false);
        feed.PerfWalk(2 * Ms, 5 * Ms);
        feed.PerfFrameSent(2048, 5 * Ms);

        feed.PerfInbound("input", 10 * Ms);
        feed.PerfHandlerStarted("Field", 10 * Ms);
        feed.PerfHandlerEnded(10 * Ms, 11 * Ms, faulted: true);
        feed.PerfWalk(11 * Ms, 12 * Ms);
        feed.PerfFrameSent(64, 12 * Ms);
        return feed;
    }

    [Fact]
    public void An_empty_feed_says_how_to_fill_it()
    {
        var page = Render(new DevToolsFeed());

        Assert.Contains("No interactions yet", page.Html);
        Assert.Empty(page.FindAll("tbody tr"));
    }

    [Fact]
    public void Interactions_are_listed_newest_first_with_where_their_time_went()
    {
        var page = Render(Feed());

        var rows = page.FindAll("tbody tr").Select(r => r.Children.Select(td => td.TextContent.Trim()).ToArray()).ToList();
        Assert.Equal(2, rows.Count);
        // The input: its handler threw, and says so.
        Assert.Contains("input", rows[0][1]);
        Assert.Contains("Field", rows[0][1]);
        Assert.Contains("threw", rows[0][1]);
        Assert.Equal("64 B", rows[0][5]);
        // The click: 2 ms handler, 3 ms render, 2 KB, patch not yet reported.
        Assert.Contains("click", rows[1][1]);
        Assert.Equal("2.00 ms", rows[1][2]);
        Assert.Equal("3.00 ms", rows[1][3]);
        Assert.Equal("2.0 KB", rows[1][5]);
        Assert.Equal("…", rows[1][6]);
        Assert.Equal("5.00 ms", rows[1][7]);
    }

    [Fact]
    public void Server_percentiles_are_taken_over_every_interaction_held()
    {
        var interactions = Feed().InteractionsSnapshot();

        Assert.Equal(2 * Ms, DevToolsPerfTab.Percentile(interactions, 0.50));
        Assert.Equal(5 * Ms, DevToolsPerfTab.Percentile(interactions, 0.95));
    }

    [Fact]
    public void The_slowest_components_are_ordered_by_their_total_own_render_time()
    {
        var feed = Feed();
        var ids = new DevToolsTreeSnapshotter();
        var quick = Child();
        var slow = Child();
        feed.RecordCommit(
            [new DevToolsRenderItem(quick, RenderCause.Props, 1 * Ms), new DevToolsRenderItem(slow, RenderCause.Props, 9 * Ms)],
            2, ids, 1);
        feed.RecordCommit([new DevToolsRenderItem(quick, RenderCause.State, 2 * Ms)], 2, ids, 2);

        var slowest = DevToolsPerfTab.Slowest(feed.CommitsSnapshot());

        Assert.Equal([ids.IdOf(slow), ids.IdOf(quick)], slowest.Select(s => s.Id));
        Assert.Equal(2, slowest[1].Renders);
        Assert.Equal(3 * Ms, slowest[1].Ticks);
        Assert.Equal(2 * Ms, slowest[1].MaxTicks);
        Assert.Contains("Slowest components", Render(feed).Html);
    }

    [Fact]
    public async Task The_page_patch_times_reach_the_frames_waiting_for_them_and_garbage_is_ignored()
    {
        var feed = new DevToolsFeed();
        var now = Stopwatch.GetTimestamp();
        feed.PerfWalk(now, now);
        feed.PerfFrameSent(10, now);
        feed.PerfWalk(now, now);
        feed.PerfFrameSent(20, now);
        var page = Receiver(feed);

        await page.On("[data-rask-devtools-patch]")
            .RaiseAsync("keydown", "{\"key\":\"patch:nope@20,-3@20,1e9@20,2@x,2@-7,2,4.25@20\"}");
        await page.On("[data-rask-devtools-patch]").RaiseAsync("keydown", "{\"key\":\"Enter\"}");
        await page.On("[data-rask-devtools-patch]").RaiseAsync("keydown", "{\"key\":\"patch:1.5@10\"}");

        var items = feed.InteractionsSnapshot();
        // Matched by size, not by order: the 10-byte frame was sent first.
        Assert.Equal(1.5, items[0].PatchMilliseconds);
        Assert.Equal(4.25, items[1].PatchMilliseconds);
    }
}
