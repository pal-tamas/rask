using System.Diagnostics;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Tests.Probe;

/// <summary>
///     How the feed turns hook calls into interactions: what starts one, what ends one, where a render inside a handler's
///     time is counted, and which frame the page's patch time belongs to.
/// </summary>
public sealed class DevToolsInteractionTests
{
    private static readonly long Ms = Stopwatch.Frequency / 1000;

    [Fact]
    public void A_click_whose_handler_renders_is_one_interaction_ended_by_its_frame()
    {
        var feed = new DevToolsFeed();

        feed.PerfInbound("click", 0);
        feed.PerfHandlerStarted("Board", 10 * Ms);
        feed.PerfHandlerEnded(10 * Ms, 14 * Ms, faulted: false);
        feed.PerfWalk(15 * Ms, 18 * Ms);
        feed.PerfDiff(18 * Ms, 19 * Ms);
        feed.PerfFrameSent(512, 19 * Ms);

        var item = Assert.Single(feed.InteractionsSnapshot());
        Assert.Equal("click", item.Trigger);
        Assert.Equal("Board", item.Target);
        Assert.Equal(4 * Ms, item.HandlerTicks);
        Assert.Equal(3 * Ms, item.RenderTicks);
        Assert.Equal(1 * Ms, item.DiffTicks);
        Assert.Equal(1, item.Frames);
        Assert.Equal(512, item.Bytes);
        Assert.Equal(8 * Ms, item.ServerTicks);
        Assert.False(item.Faulted);

        // Ended by its frame: the next render nothing asked for is an interaction of its own.
        feed.PerfWalk(40 * Ms, 41 * Ms);
        feed.PerfFrameSent(100, 41 * Ms);
        var items = feed.InteractionsSnapshot();
        Assert.Equal(2, items.Length);
        Assert.Equal("render", items[1].Trigger);
        Assert.Null(items[1].HandlerTicks);
    }

    [Fact]
    public void A_render_an_async_handler_asks_for_mid_await_is_render_time_not_handler_time()
    {
        var feed = new DevToolsFeed();

        feed.PerfInbound("click", 0);
        feed.PerfHandlerStarted("Board", 0);
        feed.PerfWalk(2 * Ms, 7 * Ms);
        feed.PerfFrameSent(200, 7 * Ms); // still running: does not end the interaction
        feed.PerfHandlerEnded(0, 20 * Ms, faulted: true);
        feed.PerfWalk(21 * Ms, 22 * Ms);
        feed.PerfFrameSent(300, 22 * Ms);

        var item = Assert.Single(feed.InteractionsSnapshot());
        Assert.Equal(15 * Ms, item.HandlerTicks);
        Assert.Equal(6 * Ms, item.RenderTicks);
        Assert.Equal(2, item.Frames);
        Assert.Equal(500, item.Bytes);
        Assert.True(item.Faulted);
    }

    [Fact]
    public void A_navigation_is_an_interaction_without_a_handler()
    {
        var feed = new DevToolsFeed();

        feed.PerfInbound("navigate", 0);
        feed.PerfWalk(1 * Ms, 3 * Ms);
        feed.PerfFrameSent(900, 3 * Ms);

        var item = Assert.Single(feed.InteractionsSnapshot());
        Assert.Equal("navigate", item.Trigger);
        Assert.Null(item.Target);
        Assert.Null(item.HandlerTicks);
    }

    [Fact]
    public void A_patch_time_goes_to_the_oldest_waiting_frame_of_its_size_and_never_to_a_stale_one()
    {
        var feed = new DevToolsFeed();
        var window = (long)(DevToolsFeed.PatchWindow.TotalSeconds * Stopwatch.Frequency);

        // A frame long before the panel could report anything, then three in a row.
        feed.PerfWalk(0, 1);
        feed.PerfFrameSent(20, 1);
        var later = window * 5;
        feed.PerfWalk(later, later + 1);
        feed.PerfFrameSent(20, later + 1);
        feed.PerfWalk(later + 2, later + 3);
        feed.PerfFrameSent(30, later + 3);
        feed.PerfWalk(later + 4, later + 5);
        feed.PerfFrameSent(20, later + 5);

        // The page applied the 20-byte frames and the 30-byte one; its reports can arrive out of that order.
        feed.PerfPatch(2.0, 30, later + 10);
        feed.PerfPatch(4.5, 20, later + 11);
        feed.PerfPatch(1.5, 20, later + 12);
        // Nothing of that size left waiting: a stray report changes nothing.
        feed.PerfPatch(99, 20, later + 13);

        var items = feed.InteractionsSnapshot();
        Assert.Null(items[0].PatchMilliseconds);
        Assert.Equal(4.5, items[1].PatchMilliseconds);
        Assert.Equal(2.0, items[2].PatchMilliseconds);
        Assert.Equal(1.5, items[3].PatchMilliseconds);
    }

    [Fact]
    public void A_patch_time_without_a_size_goes_to_the_oldest_waiting_frame()
    {
        var feed = new DevToolsFeed();
        feed.PerfWalk(0, 1);
        feed.PerfFrameSent(20, 1);
        feed.PerfWalk(2, 3);
        feed.PerfFrameSent(30, 3);

        feed.PerfPatch(3.0, -1, 10);

        var items = feed.InteractionsSnapshot();
        Assert.Equal(3.0, items[0].PatchMilliseconds);
        Assert.Null(items[1].PatchMilliseconds);
    }

    [Fact]
    public void A_handler_without_a_frame_before_it_leaves_its_interaction_behind_as_it_was()
    {
        var feed = new DevToolsFeed();

        // The page event dispatched, the render deduplicated, nothing sent; then another click.
        feed.PerfInbound("input", 0);
        feed.PerfHandlerStarted("Field", 0);
        feed.PerfHandlerEnded(0, Ms, faulted: false);
        feed.PerfInbound("click", 2 * Ms);
        feed.PerfHandlerStarted("Button", 2 * Ms);

        var items = feed.InteractionsSnapshot();
        Assert.Equal(["input", "click"], items.Select(i => i.Trigger));
        Assert.Equal(0, items[0].Frames);
    }

    [Fact]
    public void Past_the_capacity_the_oldest_interactions_go_and_Clear_forgets_them_all()
    {
        var feed = new DevToolsFeed();
        for (var i = 0; i < DevToolsFeed.InteractionCapacity + 5; i++)
        {
            feed.PerfWalk(i, i + 1);
            feed.PerfFrameSent(i, i + 1);
        }

        var items = feed.InteractionsSnapshot();
        Assert.Equal(DevToolsFeed.InteractionCapacity, items.Length);
        Assert.Equal(5, items[0].Bytes);

        feed.ClearInteractions();
        Assert.Empty(feed.InteractionsSnapshot());
    }
}
