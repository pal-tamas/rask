using System.Text.Json;
using Rask.Core.Tests.Live;
using Rask.Core.Virtualization;

#pragma warning disable RASK014 // test-defined StubComponent has no generated factory

namespace Rask.Core.Tests.Components;

public class VirtualizeTests
{
    [Fact]
    public void Render_HeadlessNoOwnDom_OnlyEmitsUserMarkup()
    {
        var view = new StubComponent(() => Virtualize(
            ctx => Div()["x"],
            new List<int> { 1, 2, 3 },
            ItemSize: 20,
            InitialClientHeight: 100));

        Assert.Equal("<div>x</div>", view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_SyncItems_VisibleWindowReflectsInitialScroll()
    {
        VirtualizationContext<int>? captured = null;
        var items = Enumerable.Range(0, 100).ToList();
        var view = new StubComponent(() => Virtualize(
            ctx =>
            {
                captured = ctx;
                return Div();
            },
            items,
            ItemSize: 20,
            OverscanCount: 2,
            InitialClientHeight: 100));

        view.RenderAsLiveRoot();
        Assert.NotNull(captured);

        // At scrollTop=0 with clientHeight=100, ItemSize=20 => 5 items visible.
        // Overscan adds 2 below; before is clamped at 0. Visible window = [0, 7).
        Assert.Equal(0, captured!.StartIndex);
        Assert.Equal(100, captured.TotalCount);
        Assert.Equal(0, captured.OffsetBefore);
        Assert.Equal(7, captured.VisibleItems.Count);
        Assert.Equal(0, captured.VisibleItems[0].Index);
        Assert.Equal(0, captured.VisibleItems[0].Value);
        Assert.False(captured.VisibleItems[0].IsPlaceholder);
        Assert.Equal(6, captured.VisibleItems[6].Index);
    }

    [Fact]
    public async Task OnScroll_UpdatesVisibleWindow_AndOffsets()
    {
        VirtualizationContext<int>? captured = null;
        var items = Enumerable.Range(0, 100).ToList();
        var view = new StubComponent(() => Virtualize(
            ctx =>
            {
                captured = ctx;
                return Div()[Div(OnScroll: ctx.OnScroll)];
            },
            items,
            ItemSize: 20,
            OverscanCount: 2,
            InitialClientHeight: 100));

        var html = view.RenderAsLiveRoot();
        var scrollHandlerId = Markup.Attr(html, "data-rask-on-scroll");
        Assert.NotNull(scrollHandlerId);

        // Fire a scroll event payload {scrollTop:200, clientHeight:100, scrollHeight:2000}.
        // Expected window: startIndex = 200/20 - 2 = 8, endIndex = (200+100)/20 + 2 = 17.
        // So visible = [8, 17) inclusive of overscan above + the partial row at top.
        using var doc = JsonDocument.Parse(
            "{\"scrollTop\":200,\"clientHeight\":100,\"scrollHeight\":2000}");
        await view.TryInvokeHandlerAsync(scrollHandlerId!, doc.RootElement);

        view.RenderAsLiveRoot();
        Assert.NotNull(captured);
        Assert.Equal(8, captured!.StartIndex);
        Assert.Equal(160, captured.OffsetBefore); // 8 * 20
        Assert.Equal(8, captured.VisibleItems[0].Value);
        // OffsetAfter = (TotalCount - endIndex) * ItemSize; endIndex = StartIndex + VisibleItems.Count.
        var expectedAfter = (100 - (8 + captured.VisibleItems.Count)) * 20;
        Assert.Equal(expectedAfter, captured.OffsetAfter);
    }

    [Fact]
    public void ItemsProvider_FirstRender_NoItems_SecondRender_LoadedFromCache()
    {
        // Synchronously-completing provider so cache fills inside the first Render(); the
        // second Render() then sees the populated cache + known total. Mirrors the runtime
        // flow where StateHasChanged would trigger the second render through the WS.
        var requests = new List<ItemsProviderRequest>();
        VirtualizationContext<string>? captured = null;
        var view = new StubComponent(() => Virtualize<string>(
            ctx =>
            {
                captured = ctx;
                return Div();
            },
            ItemsProvider: req =>
            {
                requests.Add(req);
                var items = Enumerable.Range(req.StartIndex, req.Count)
                    .Select(i => $"row-{i}")
                    .ToList();
                return new ValueTask<ItemsProviderResult<string>>(
                    new ItemsProviderResult<string>(items, 50));
            },
            ItemSize: 20,
            OverscanCount: 2,
            InitialClientHeight: 100));

        // First render: total unknown -> empty visible window, but kicks the probe fetch.
        view.RenderAsLiveRoot();
        Assert.NotNull(captured);
        Assert.Equal(0, captured!.TotalCount);
        Assert.Empty(captured.VisibleItems);
        Assert.Single(requests); // exactly one probe so far

        // Second render: cache populated from the synchronous probe — total now known,
        // visible items resolve to real values (or placeholders for indices not in the
        // probe window, which will be filled by a follow-up fetch on next paint).
        view.RenderAsLiveRoot();
        Assert.Equal(50, captured.TotalCount);
        var loaded = captured.VisibleItems.Where(v => !v.IsPlaceholder).ToList();
        Assert.NotEmpty(loaded);
        Assert.Equal("row-0", loaded[0].Value);
    }

    [Fact]
    public void Render_NoItemsAndNoProvider_Throws()
    {
        var view = new StubComponent(() => Virtualize<int>(
            ctx => Div(),
            ItemSize: 20));

        Assert.Throws<InvalidOperationException>(() => view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_BothItemsAndProvider_Throws()
    {
        var view = new StubComponent(() => Virtualize(
            ctx => Div(),
            new List<int> { 1 },
            req => ValueTask.FromResult(new ItemsProviderResult<int>(Array.Empty<int>(), 0)),
            20));

        Assert.Throws<InvalidOperationException>(() => view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_ItemSizeZero_Throws()
    {
        var view = new StubComponent(() => Virtualize(
            ctx => Div(),
            new List<int> { 1 },
            ItemSize: 0));

        Assert.Throws<InvalidOperationException>(() => view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OffsetAfter_ClampsAtZeroWhenAllVisible()
    {
        VirtualizationContext<int>? captured = null;
        var items = new List<int> { 1, 2, 3 };
        var view = new StubComponent(() => Virtualize(
            ctx =>
            {
                captured = ctx;
                return Div();
            },
            items,
            ItemSize: 20,
            OverscanCount: 0,
            InitialClientHeight: 500));

        view.RenderAsLiveRoot();
        Assert.NotNull(captured);
        Assert.Equal(0, captured!.OffsetAfter);
        Assert.Equal(3, captured.VisibleItems.Count);
    }

    [Fact]
    public void Render_UserSetsRaskKeyOnRow_AttributeFlowsToHtml()
    {
        // Documents the recommended keying pattern for Virtualize rows: setting
        // Data["rask-key"] makes the client-side morph engage its keyed reconciliation
        // path so reordered / scrolled rows keep their DOM identity (focus, scroll
        // state) across re-renders.
        var items = Enumerable.Range(0, 50).ToList();
        var view = new StubComponent(() => Virtualize(
            ctx => Div()[
                ctx.VisibleItems.Select(item =>
                    Div(Data: new Dictionary<string, string?> { ["rask-key"] = item.Index.ToString() })[
                        item.Value!])
            ],
            items,
            ItemSize: 20,
            InitialClientHeight: 100));

        var html = view.RenderAsLiveRoot();

        Assert.Contains("data-rask-key=\"0\"", html);
        Assert.Contains("data-rask-key=\"4\"", html);
    }

    [Fact]
    public async Task ItemsProvider_Unmount_CancelsInFlightFetch()
    {
        // Regression: Virtualize's in-flight ItemsProvider fetch must be
        // cancelled when the component leaves the parent's tree. Pre-fix the
        // _activeFetch CTS was orphaned on unmount — the provider's await kept
        // running until it produced its own result, then tried to update _cache
        // and call StateHasChanged on a disposed component.
        var fetchObservedCt = CancellationToken.None;
        var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Func<ItemsProviderRequest, ValueTask<ItemsProviderResult<string>>> provider = async req =>
        {
            fetchObservedCt = req.CancellationToken;
            providerStarted.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, req.CancellationToken); }
            catch (OperationCanceledException) { }
            return new ItemsProviderResult<string>(Array.Empty<string>(), 0);
        };

        var show = true;
        var view = new StubComponent(() => show
            ? Virtualize<string>(
                ctx => Div(),
                ItemsProvider: provider,
                ItemSize: 20,
                InitialClientHeight: 100)
            : Div());

        view.RenderAsLiveRoot();
        await providerStarted.Task;
        Assert.False(fetchObservedCt.IsCancellationRequested);

        // Stop rendering Virtualize — the framework's diff fires OnUnmount on
        // the Virtualize subtree, which should cancel the in-flight fetch.
        show = false;
        view.RenderAsLiveRoot();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!fetchObservedCt.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(fetchObservedCt.IsCancellationRequested);
    }
}
