using System.Globalization;
using System.Text.Json;
using Rask.Core.Virtualization;

#pragma warning disable RASK014 // test-defined StubComponent has no generated factory

namespace Rask.Core.Tests.Components;

/// <summary>
///     A list drawn between the context's spacers (<c>ctx.Above</c>, <c>ctx.Below</c>): its rows are measured, it opens
///     at <c>InitialItemIndex</c>, follows its tail under <see cref="VirtualAnchor.End" /> and scrolls to a row from code.
///     The browser's halves — the measuring, the holding, the scrolling — are the E2E suites'; this is the server's.
/// </summary>
public partial class VirtualizeMeasuredTests : global::Rask.Core.RaskMarkup
{
    private VirtualizationContext<int>? _seen;

    [Fact]
    public void The_spacers_say_the_window_they_hold_and_are_as_tall_as_the_rows_not_drawn()
    {
        var view = List(Enumerable.Range(0, 100).ToList());

        var html = view.RenderAsLiveRoot();

        Assert.Contains("data-rask-virtual=\"0 5\"", html, StringComparison.Ordinal);
        Assert.Contains("height:1900px;flex-shrink:0;overflow-anchor:none", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-virtual-below", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-virtual-anchor", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_default_overscan_draws_ten_rows_past_either_edge_of_the_box()
    {
        var view = List(Enumerable.Range(0, 1000).ToList(), overscan: null);
        var box = Box(view.RenderAsLiveRoot());

        await Scroll(view, box, scrollTop: 2000, separation: 0, rows: 0, start: 0);
        view.RenderAsLiveRoot();

        Assert.Equal(90, _seen!.StartIndex);
        Assert.Equal(25, _seen.VisibleItems.Count);
    }

    [Fact]
    public async Task A_measured_window_makes_the_spacers_its_average_row_tall()
    {
        var view = List(Enumerable.Range(0, 100).ToList());
        var box = Box(view.RenderAsLiveRoot());

        await Scroll(view, box, scrollTop: 0, separation: 5 * 40, rows: 5, start: 0);
        view.RenderAsLiveRoot();

        Assert.Equal(0, _seen!.StartIndex);
        Assert.Equal((100 - _seen.VisibleItems.Count) * 40, _seen.OffsetAfter);
    }

    [Fact]
    public async Task Each_window_is_measured_once_however_often_its_box_scrolls()
    {
        var view = List(Enumerable.Range(0, 100).ToList());
        var box = Box(view.RenderAsLiveRoot());

        await Scroll(view, box, scrollTop: 0, separation: 5 * 40, rows: 5, start: 0);
        await Scroll(view, box, scrollTop: 0, separation: 5 * 10, rows: 5, start: 0);
        view.RenderAsLiveRoot();

        Assert.Equal((100 - _seen!.VisibleItems.Count) * 40, _seen.OffsetAfter);
    }

    [Fact]
    public async Task A_window_holding_placeholders_is_not_measured()
    {
        var view = new StubComponent(() => Virtualize.Items<int>(
            ctx =>
            {
                _seen = ctx;
                return Div.OnScroll(ctx.OnScroll)[Rows(ctx)];
            },
            ItemsProvider: _ => new ValueTask<ItemsProviderResult<int>>(new TaskCompletionSource<ItemsProviderResult<int>>().Task),
            ItemSize: 20,
            OverscanCount: 0,
            InitialClientHeight: 100,
            InitialTotalCount: 100));
        var box = Box(view.RenderAsLiveRoot());

        await Scroll(view, box, scrollTop: 0, separation: 5 * 40, rows: 5, start: 0);
        view.RenderAsLiveRoot();

        Assert.True(_seen!.VisibleItems[0].IsPlaceholder);
        Assert.Equal((100 - _seen.VisibleItems.Count) * 20, _seen.OffsetAfter);
    }

    [Fact]
    public async Task A_list_with_spacers_of_its_own_keeps_every_row_its_item_size_tall()
    {
        var view = new StubComponent(() => Virtualize.Items(
            ctx =>
            {
                _seen = ctx;
                return Div.OnScroll(ctx.OnScroll)[Div.Style("height:" + ctx.OffsetBefore + "px")];
            },
            Enumerable.Range(0, 100).ToList(),
            ItemSize: 20,
            OverscanCount: 0,
            InitialClientHeight: 100));
        var box = Box(view.RenderAsLiveRoot());

        await Scroll(view, box, scrollTop: 0, separation: 5 * 40, rows: 5, start: 0);
        view.RenderAsLiveRoot();

        Assert.Equal(95 * 20, _seen!.OffsetAfter);
    }

    [Fact]
    public void Initial_item_index_draws_its_row_at_the_top_of_the_first_render_and_asks_the_browser_to_scroll_there()
    {
        var view = List(Enumerable.Range(0, 1000).ToList(), overscan: 3, initial: 500);

        var html = view.RenderAsLiveRoot();

        Assert.Equal(497, _seen!.StartIndex);
        Assert.Equal(500, _seen.VisibleItems[3].Index);
        Assert.Contains("data-rask-virtual-to=\"1 500\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Initial_item_index_past_the_last_row_opens_at_the_last_row()
    {
        var view = List(Enumerable.Range(0, 100).ToList(), initial: 5000);

        var html = view.RenderAsLiveRoot();

        Assert.Contains("data-rask-virtual-to=\"1 99\"", html, StringComparison.Ordinal);
        Assert.Equal(99, _seen!.VisibleItems[^1].Index);
    }

    [Fact]
    public async Task The_window_stays_on_the_row_scrolled_to_until_the_browser_says_it_is_there()
    {
        var view = List(Enumerable.Range(0, 1000).ToList(), overscan: 3, initial: 500);
        var box = Box(view.RenderAsLiveRoot());

        await Scroll(view, box, scrollTop: 0, separation: 0, rows: 0, start: 497, done: 0);
        var waiting = view.RenderAsLiveRoot();
        var waitingStart = _seen!.StartIndex;
        await Scroll(view, box, scrollTop: 497 * 20, separation: 11 * 20, rows: 11, start: 497, done: 1);
        var landed = view.RenderAsLiveRoot();

        Assert.Equal(497, waitingStart);
        Assert.Contains("data-rask-virtual-to", waiting, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-virtual-to", landed, StringComparison.Ordinal);
        Assert.Equal(494, _seen.StartIndex);
    }

    [Fact]
    public async Task Scroll_to_finishes_once_the_browser_has_the_row_in_view_and_a_later_call_takes_over_an_earlier_one()
    {
        var scroller = VirtualScroller.New();
        var view = List(Enumerable.Range(0, 1000).ToList(), overscan: 3, scroller: scroller);
        var box = Box(view.RenderAsLiveRoot());

        var first = scroller.ScrollTo(300);
        var second = scroller.ScrollTo(700);
        var html = view.RenderAsLiveRoot();
        var waiting = second.IsCompleted;
        await Scroll(view, box, scrollTop: 697 * 20, separation: 11 * 20, rows: 11, start: 697, done: 2);

        await first.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await second.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(waiting);
        Assert.Contains("data-rask-virtual-to=\"2 700\"", html, StringComparison.Ordinal);
        Assert.Equal(697, _seen!.StartIndex);
    }

    [Fact]
    public void A_scroller_no_list_has_drawn_refuses_to_scroll()
    {
        var scroller = VirtualScroller.New();

        var refused = Assert.Throws<InvalidOperationException>(() => { _ = scroller.ScrollTo(3); });

        Assert.Contains("InitialItemIndex", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_end_anchored_list_opens_at_its_tail_follows_rows_added_there_and_stops_once_scrolled_up()
    {
        var rows = Enumerable.Range(0, 100).ToList();
        var view = List(rows, anchor: VirtualAnchor.End);

        var html = view.RenderAsLiveRoot();
        var openedAt = _seen!.VisibleItems[^1].Index;
        var box = Box(html);
        rows.AddRange(Enumerable.Range(100, 50));
        view.RenderAsLiveRoot();
        var followedTo = _seen.VisibleItems[^1].Index;
        await Scroll(view, box, scrollTop: 0, separation: 5 * 20, rows: 5, start: _seen.StartIndex, scrollHeight: 3000);
        rows.AddRange(Enumerable.Range(150, 50));
        view.RenderAsLiveRoot();

        Assert.Equal(99, openedAt);
        Assert.Equal(149, followedTo);
        Assert.Contains("data-rask-virtual-anchor=\"2\"", html, StringComparison.Ordinal);
        Assert.Equal(0, _seen.StartIndex);
    }

    [Fact]
    public void A_table_s_spacers_are_rows()
    {
        var view = new StubComponent(() => Virtualize.Items(
            ctx => Table.OnScroll(ctx.OnScroll)[Tbody[TableRows(ctx)]],
            Enumerable.Range(0, 100).ToList(),
            ItemSize: 20,
            OverscanCount: 0,
            InitialClientHeight: 100));

        var html = view.RenderAsLiveRoot();

        Assert.Contains("<tr style=\"height:0px;flex-shrink:0;overflow-anchor:none\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-key=\"rask:virtual-below\"", html, StringComparison.Ordinal);
    }

    private StubComponent List(
        List<int> rows,
        int? overscan = 0,
        int initial = 0,
        VirtualAnchor anchor = VirtualAnchor.Start,
        VirtualScroller? scroller = null) =>
        new(() => Virtualize.Items(
            ctx =>
            {
                _seen = ctx;
                return Div.OnScroll(ctx.OnScroll)[Rows(ctx)];
            },
            rows,
            ItemSize: 20,
            OverscanCount: overscan ?? 10,
            InitialClientHeight: 100,
            InitialItemIndex: initial,
            Anchor: anchor,
            Scroller: scroller));

    private static IEnumerable<Component> Rows(VirtualizationContext<int> ctx)
    {
        yield return ctx.Above;
        foreach (var item in ctx.VisibleItems)
        {
            yield return Div[item.Index.ToString(CultureInfo.InvariantCulture)];
        }

        yield return ctx.Below;
    }

    private static IEnumerable<Component> TableRows(VirtualizationContext<int> ctx)
    {
        yield return ctx.AboveRow;
        foreach (var item in ctx.VisibleItems)
        {
            yield return Tr[Td[item.Index.ToString(CultureInfo.InvariantCulture)]];
        }

        yield return ctx.BelowRow;
    }

    private static string Box(string html) => MarkupAssert.Attr(html, "data-rask-on-scroll")!;

    private static async Task Scroll(
        StubComponent view,
        string box,
        int scrollTop,
        double separation,
        int rows,
        int start,
        int done = -1,
        int scrollHeight = 100_000)
    {
        var target = string.Create(
            CultureInfo.InvariantCulture,
            $$$"""{"target":{"scrollTop":{{{scrollTop}}},"clientHeight":100,"scrollHeight":{{{scrollHeight}}},"raskVirtual":[0,{{{separation}}},{{{rows}}},{{{start}}},{{{done}}},-1,-1]}}""");
        using var doc = JsonDocument.Parse(target);
        await view.TryInvokeHandlerAsync(box, doc.RootElement);
    }
}
