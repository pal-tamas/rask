using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Core.Virtualization;

namespace Rask.Core.Components;

// Headless list virtualization: renders no DOM of its own, takes a Render delegate, hands
// it a VirtualizationState describing the currently visible item window. The user wires
// the state's OnScroll handler into a scrollable container and reads OffsetBefore /
// OffsetAfter / VisibleValues to drive their own markup.
//
// To preserve row identity across scroll / reorder, chain `.Key(item.Index)`
// (or any stable per-row identity) on the row element. The client morph engages a keyed
// reconciliation path when any child carries data-rask-key, moving existing DOM nodes
// into place instead of replacing them — focus and scroll state survive the
// re-render. See /virtualize in the showcase for the pattern.
//
// Always invoked through the hand-written generic factory `Components.VirtualizeModel<T>(...)`
// (see Virtualize.cs). The non-generic factory remains as the forwarding target.
//
// Items vs ItemsProvider — exactly one must be set:
//   • Items: IEnumerable in memory; window is sliced directly.
//   • ItemsProvider (typed as Func<ItemsProviderRequest, ValueTask<ItemsProviderResultErased>>):
//     async paging. Component caches loaded items by global index, requests missing windows
//     in the background, and re-renders on completion. The first render before the count
//     is known emits an all-placeholder window so the user can render a Loading view.

/// <summary>
///     Renders only the rows near the viewport, so a list of any length costs the same to render. Supply
///     <c>Items</c>, or an <c>ItemsProvider</c> that fetches a window on demand, plus the row height in
///     <c>ItemSize</c>.
/// </summary>
public sealed class VirtualizeModel : Component
{
    private readonly VirtualScroll _scroll = new();
    private readonly RowHeights _heights = new();

    // The spacer above the rows: the browser finds the box by it, to scroll it to a row (ScrollTo).
    private readonly ElementRef _above = ElementRef.New();

    // What the last render drew, as the browser's next report is checked against: the window, the row height its
    // spacers were drawn at, and whether any row in it was a placeholder.
    private VirtualWindow _drawn;
    private double _drawnAverage;
    private bool _drawnPlaceholders;

    // The last window measured. Each window counts once, however often it is drawn or its box scrolls: counted again
    // and again, the average would creep towards those rows and move the spacers a pixel at a time, for ever.
    private (int Start, int Count)? _lastMeasured;

    // Whether the render draws the context's spacers, so the browser measures the rows. Taken to be so until a render
    // shows otherwise: the first one is drawn before the body has said, and with nothing measured yet both ways count
    // the same rows — but only this one opens at InitialItemIndex or at the tail.
    private bool _measured = true;

    // InitialItemIndex and the End anchor apply once, when the row count is first known.
    private bool _started;
    private bool _following;

    // A scroll to a row the browser has still to carry out (data-rask-virtual-to), and who waits on it.
    private bool _aiming;
    private int _aimSeq;
    private int _aimIndex;
    private TaskCompletionSource? _aimDone;
    private CancellationTokenSource? _activeFetch;
    private Dictionary<int, object?>? _cache;
    private IEnumerable? _lastItems;
    private Delegate? _lastProvider;
    private Action<Event>? _onScrollDelegate;
    private int _totalCount;
    private bool _totalCountKnown;

    /// <summary>
    ///     The full set of items. For a set too large to hold in memory, use <c>ItemsProvider</c> instead.
    /// </summary>
    public IEnumerable? Items { get; set; }

    // Stored as a Delegate? so the non-generic factory's signature stays simple. The typed
    // VirtualizeModel<T>(...) factory wraps the user's
    //   Func<ItemsProviderRequest, ValueTask<ItemsProviderResult<T>>>
    // into the erased
    //   Func<ItemsProviderRequest, ValueTask<ItemsProviderResultErased>>
    // before forwarding. Internally we cast back to the erased shape.

    /// <summary>
    ///     Fetches one window of items on demand, for a list too large — or too remote — to materialize up
    ///     front.
    /// </summary>
    public Delegate? ItemsProvider { get; set; }

    /// <summary>
    ///     A row's height in pixels: the starting estimate of a list drawn between the context's spacers, which
    ///     measures its rows from then on, and the exact height of every row in a list with spacers of its own.
    /// </summary>
    public int ItemSize { get; set; }

    /// <summary>
    ///     How many extra rows to render beyond the viewport, trading a little work for fewer blank rows
    ///     during a fast scroll.
    /// </summary>
    public int? OverscanCount { get; set; }

    // Pre-scroll initial viewport guess. Drives the first render's window size before any
    // scroll event has arrived to set _clientHeight. Should be the scrollable container's
    // approximate visible height in px.

    /// <summary>
    ///     The viewport height to assume for the first render, before the browser has reported the real
    ///     one.
    /// </summary>
    public int? InitialClientHeight { get; set; }

    /// <summary>
    ///     The item count to assume before an <see cref="ItemsProvider" /> has reported the real one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The provider's sibling to <see cref="InitialClientHeight" />, and for the same reason: the
    ///         first render happens before the thing that knows the answer has answered. Without it a
    ///         provider-backed list renders <b>no rows at all</b> until the first fetch resolves -- an
    ///         empty box that pops into a full table, which is a layout shift on every load and, on a
    ///         prerendered page, markup whose very shape depends on when it was sampled.
    ///     </para>
    ///     <para>
    ///         The rows it produces are ordinary placeholders: the window renders at its real size with
    ///         every row flagged <c>IsPlaceholder</c>, so a body already written to show a pending row
    ///         needs no new branch. An estimate is fine and is the point -- the real count replaces it as
    ///         soon as the provider answers, and only the spacer heights change. Ignored entirely when
    ///         <see cref="Items" /> is used, where the count is never in doubt.
    ///     </para>
    /// </remarks>
    public int? InitialTotalCount { get; set; }

    /// <summary>
    ///     The row the list opens at: it is drawn at the top of the box on the first render, and the browser scrolls
    ///     there. Applied once, clamped to the rows there are. Needs the context's spacers.
    /// </summary>
    public int? InitialItemIndex { get; set; }

    /// <summary>
    ///     The edge the box holds on to as rows come and go; <see cref="VirtualAnchor.Start" /> when unset. Needs the
    ///     context's spacers.
    /// </summary>
    public VirtualAnchor? Anchor { get; set; }

    /// <summary>Scrolls the list to a row from code (<see cref="VirtualScroller.ScrollTo" />). Needs the context's spacers.</summary>
    public VirtualScroller? Scroller { get; set; }

    // The render fragment. Called with the type-erased VirtualizationState every render, and
    // returns the user's chosen root Component for the virtualized region. Stored under the
    // name "Body" rather than "Render" to avoid colliding with Component.Render(). The
    // user-facing typed factory VirtualizeModel<T>(...) exposes this parameter as "Render".

    /// <summary>Renders one row, given the virtualization state.</summary>
    public new Fn<VirtualizationState, Component>? Body { get; set; }

    // VirtualizeModel reads mutable internal state (scroll position, item cache, total count
    // from the async provider) that the framework can't observe through props alone, so
    // every render must re-execute. Without this, the cached Div from the last render
    // would be returned even after a scroll event moved the window. Same reasoning as
    // Router/Outlet — see Component.BypassRenderCache.
    protected override bool BypassRenderCache => true;

    protected override Task OnUpdated()
    {
        // Treat an Items or ItemsProvider reference swap as a full reset: the prior cache,
        // scroll position, and active fetch all belong to the old data source.
        var itemsSwapped = !ReferenceEquals(_lastItems, Items);
        var providerSwapped = !ReferenceEquals(_lastProvider, ItemsProvider);
        if (!itemsSwapped && !providerSwapped)
        {
            return Task.CompletedTask;
        }

        _lastItems = Items;
        if (providerSwapped && _lastProvider is not null)
        {
            // A new provider is a refresh: its rows are measured afresh, as .NET's RefreshDataAsync forgets them.
            _heights.Reset();
        }

        _lastProvider = ItemsProvider;
        _cache = null;
        _totalCount = 0;
        _totalCountKnown = false;
        CancelAndDisposeActiveFetch();
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        // Cancel any in-flight fetch when the component leaves the tree so the
        // provider's await unwinds promptly and the CTS doesn't leak through GC.
        // Without this, a VirtualizeModel whose ItemsProvider is mid-`await` keeps the
        // CTS rooted via the captured request.CancellationToken and the
        // FetchAsync continuation still tries to update _cache + call
        // StateHasChanged after disposal — wasted work and a small unmanaged
        // resource leak per CTS.
        CancelAndDisposeActiveFetch();
        Land();
        return Task.CompletedTask;
    }

    private void CancelAndDisposeActiveFetch()
    {
        var fetch = _activeFetch;
        if (fetch is null)
        {
            return;
        }

        _activeFetch = null;
        try
        {
            fetch.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The fetch already finished and disposed its token: nothing left to cancel.
        }

        fetch.Dispose();
    }

    protected override Component? Render()
    {
        var body = RequireValidProps();
        Scroller?.Hold(this);

        var itemSize = ItemSize;
        var initialClientHeight = InitialClientHeight ?? 0;
        var overscan = Math.Max(0, OverscanCount ?? 0);
        var anchor = Anchor ?? VirtualAnchor.Start;
        _heights.From(itemSize);

        var totalCount = CurrentTotalCount();
        StartOnce(totalCount, anchor);

        var average = _heights.Average;
        var window = WindowFor(totalCount, overscan, initialClientHeight, average);
        var startIndex = window.Start;
        var endIndex = window.End;

        var visibleCount = endIndex - startIndex;
        var values = new object?[visibleCount];
        var indices = new int[visibleCount];
        var placeholders = new bool[visibleCount];

        if (Items is not null)
        {
            FillFromItems(Items, startIndex, visibleCount, values, indices, placeholders);
        }
        else
        {
            FillFromCache(startIndex, visibleCount, values, indices, placeholders);
            ScheduleAsyncFetch(startIndex, visibleCount, placeholders, overscan, _scroll.Height(itemSize, initialClientHeight), itemSize);
        }

        _onScrollDelegate ??= HandleScroll;

        var offsetBefore = window.OffsetBefore;
        var offsetAfter = window.OffsetAfter;

        var state = new VirtualizationState(
            values,
            indices,
            placeholders,
            startIndex,
            totalCount,
            itemSize,
            offsetBefore,
            offsetAfter,
            _onScrollDelegate)
        {
            Marks = new VirtualMarks(anchor, _aiming ? _aimSeq : 0, _aimIndex),
            AboveRef = _above,
        };

        var root = body.Invoke(state)!;
        Drew(window, average, Array.IndexOf(placeholders, true) >= 0, state.SpacersDrawn);
        return root;
    }

    // Once the row count is known: open at InitialItemIndex, or — anchored to the end — at the tail.
    private void StartOnce(int totalCount, VirtualAnchor anchor)
    {
        if (_started || !_totalCountKnown)
        {
            return;
        }

        _started = true;
        var initial = InitialItemIndex ?? 0;
        _following = anchor.HasFlag(VirtualAnchor.End) && initial <= 0;
        if (initial > 0 && totalCount > 0 && !_aiming)
        {
            Aim(Math.Min(initial, totalCount - 1));
        }
    }

    // The rows to draw. A list whose rows are measured finds them where the browser said they stand, or where a
    // scroll to a row or the tail puts them; one with spacers of its own keeps the fixed-height maths.
    private VirtualWindow WindowFor(int totalCount, int overscan, int initialClientHeight, double average)
    {
        if (!_measured)
        {
            return _scroll.Window(ItemSize, overscan, totalCount, initialClientHeight);
        }

        var tall = _scroll.RowsTall(average, initialClientHeight);
        int first, last;
        if (_aiming)
        {
            first = Math.Clamp(_aimIndex, 0, Math.Max(0, totalCount - 1));
            last = first + tall;
        }
        else if (_following)
        {
            first = totalCount - tall;
            last = totalCount;
        }
        else
        {
            (first, last) = _scroll.Shown(
                totalCount, average, initialClientHeight, _drawn, _drawnAverage > 0 ? _drawnAverage : average);
        }

        return VirtualScroll.Around(first, last, overscan, totalCount, average);
    }

    // What this render drew, for the browser's next report to be read against.
    private void Drew(VirtualWindow window, double average, bool placeholders, bool spacersDrawn)
    {
        _drawn = window;
        _drawnAverage = average;
        _drawnPlaceholders = placeholders;
        _measured = spacersDrawn;
        if (!spacersDrawn)
        {
            // No spacer to say it on: nothing in the browser can carry a scroll to a row out.
            _following = false;
            Land();
        }
    }

    // Scrolls to row `index`, the latest call winning; finishes once the browser has the row in view.
    internal Task ScrollTo(int index)
    {
        var superseded = _aimDone;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _aimDone = done;
        superseded?.TrySetResult();
        Aim(Math.Max(0, index));
#pragma warning disable S6966 // request the paint, never wait for the frame: the browser says when the row is in view
        StateHasChanged();
#pragma warning restore S6966
        _ = WaitForTheBrowserAsync(_aimSeq, done);
        return done.Task;
    }

    // The browser's word that scroll `seq` is carried out: asked over JS interop, whose answer is heard while the handler
    // that called ScrollTo still runs — the box's own scroll, which says so too, is an event queued behind it. With no
    // browser to ask (a test, a render outside a session) the box's scroll alone finishes it.
    private async Task WaitForTheBrowserAsync(int seq, TaskCompletionSource done)
    {
        if (_above.TryRuntime is not { } js)
        {
            return;
        }

        try
        {
            await js.InvokeVoidAsync("__raskVirtual.to", _above, seq).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or OperationCanceledException)
        {
            // The page went, or the box with it: nothing is left to wait for.
        }

        done.TrySetResult();
    }

    private void Aim(int index)
    {
        _aiming = true;
        _aimSeq++;
        _aimIndex = index;
        _following = false;
    }

    // The scroll to a row is over: the browser carried it out, or nothing can.
    private void Land()
    {
        _aiming = false;
        var done = _aimDone;
        _aimDone = null;
        done?.TrySetResult();
    }

    // The props a render cannot proceed without, checked with messages that say what to write instead.
    private Fn<VirtualizationState, Component> RequireValidProps()
    {
        if (Body is not { } body)
        {
            throw new InvalidOperationException(
                "VirtualizeModel has no Body, so there is nothing for it to render. Body receives the virtualization state — wire its OnScroll to your scroll "
                + "container and render state.Items inside it: "
                + "VirtualizeModel.Body(state => Div.OnScroll(state.OnScroll)[ … ]).Items(rows).ItemSize(32).");
        }

        if (Items is null == ItemsProvider is null)
        {
            throw new InvalidOperationException(
                "VirtualizeModel: provide exactly one of Items or ItemsProvider.");
        }

        if (ItemSize <= 0)
        {
            // ArgumentOutOfRangeException, not InvalidOperationException: the offending value belongs in
            // the exception rather than only in the prose, and this IS an out-of-range argument.
#pragma warning disable MA0015, S3928 // the argument out of range is the .ItemSize(...) chain step's (#611)
            throw new ArgumentOutOfRangeException(
                nameof(ItemSize), ItemSize,
                "VirtualizeModel.ItemSize is the pixel height of one row and drives every scroll "
                + "calculation, so it must be greater than zero. Pass the row height you render, e.g. "
                + "ItemSize: 32.");
#pragma warning restore MA0015, S3928
        }

        return body;
    }

    private int CurrentTotalCount()
    {
        if (Items is not null)
        {
            _totalCount = CountItems(Items);
            _totalCountKnown = true;
            return _totalCount;
        }

        // Nothing has answered yet, so fall back to the caller's estimate. Zero -- the previous
        // unconditional answer -- means no window, hence no rows, hence a box that pops.
        return _totalCountKnown ? _totalCount : Math.Max(0, InitialTotalCount ?? 0);
    }

    private static void FillFromItems(
        IEnumerable source,
        int startIndex,
        int count,
        object?[] values,
        int[] indices,
        bool[] placeholders)
    {
        if (source is IList list)
        {
            var take = Math.Min(count, Math.Max(0, list.Count - startIndex));
            for (var i = 0; i < take; i++)
            {
                indices[i] = startIndex + i;
                values[i] = list[startIndex + i];
                placeholders[i] = false;
            }

            return;
        }

        var idx = 0;
        var taken = 0;
        foreach (var item in source)
        {
            if (taken >= count)
            {
                break;
            }

            if (idx >= startIndex)
            {
                indices[taken] = idx;
                values[taken] = item;
                placeholders[taken] = false;
                taken++;
            }

            idx++;
        }
    }

    private void FillFromCache(
        int startIndex,
        int count,
        object?[] values,
        int[] indices,
        bool[] placeholders)
    {
        for (var i = 0; i < count; i++)
        {
            var globalIdx = startIndex + i;
            indices[i] = globalIdx;
            if (_cache is not null && _cache.TryGetValue(globalIdx, out var v))
            {
                values[i] = v;
                placeholders[i] = false;
            }
            else
            {
                values[i] = null;
                placeholders[i] = true;
            }
        }
    }

    private void ScheduleAsyncFetch(
        int startIndex,
        int count,
        bool[] placeholders,
        int overscan,
        int clientHeight,
        int itemSize)
    {
        // Initial probe: total-count unknown, fetch a window large enough to cover the
        // initial viewport so the next render can paint real items without a second round
        // trip. Sized from clientHeight rather than overscan alone so a 400px viewport with
        // ItemSize=20 doesn't probe just 4 items.
        if (!_totalCountKnown)
        {
            var visibleRows = (clientHeight + itemSize - 1) / itemSize;
            var probeCount = Math.Max(visibleRows + (overscan * 2), 1);
            _ = FetchAsync(0, probeCount);
            return;
        }

        // Coalesce contiguous placeholder runs into a single fetch range. Avoids spamming
        // the provider with N single-item requests on first render.
        var missingStart = -1;
        var missingEnd = -1;
        for (var i = 0; i < count; i++)
        {
            if (!placeholders[i])
            {
                continue;
            }

            if (missingStart < 0)
            {
                missingStart = startIndex + i;
            }

            missingEnd = startIndex + i + 1;
        }

        if (missingStart >= 0)
        {
            _ = FetchAsync(missingStart, missingEnd - missingStart);
        }
    }

    // The dispatcher's post-handler render already picks up this state mutation — the owner is marked _stateDirty
    // when TryInvokeHandlerAsync looks up the handler.
    private void HandleScroll(Event e)
    {
        _scroll.Read(e);
        var report = _scroll.Report;

        // One measurement per window, and none of a window holding placeholders: what stands between the spacers is
        // then real rows alone, as .NET's Virtualize accumulates.
        if (_measured && report is { } measured && measured.Of(_drawn) && !_drawnPlaceholders && _lastMeasured != (_drawn.Start, _drawn.Count)
            && _heights.Add(measured.Separation, measured.Rows))
        {
            _lastMeasured = (_drawn.Start, _drawn.Count);
        }

        if (_aiming)
        {
            if (report is not { } said || said.Done < _aimSeq)
            {
                // The browser has not carried the scroll to the row out yet: the window stays on that row.
                return;
            }

            Land();
        }

        _following = (Anchor ?? VirtualAnchor.Start).HasFlag(VirtualAnchor.End) && _scroll.AtEnd;
    }

    private static int CountItems(IEnumerable source)
    {
        if (source is ICollection c)
        {
            return c.Count;
        }

        var n = 0;
        foreach (var _ in source)
        {
            n++;
        }

        return n;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "ItemsProvider is set by VirtualizeModel<T>(...) which wraps the typed provider " +
                        "into the exact erased delegate shape we cast to here. No reflection.")]
    private async Task FetchAsync(int startIndex, int count)
    {
        if (ItemsProvider is not Func<ItemsProviderRequest, ValueTask<ItemsProviderResultErased>> provider)
        {
            return;
        }

        var cts = await SupersedeActiveFetchAsync().ConfigureAwait(false);

        ItemsProviderResultErased result;
        try
        {
            result = await provider(new ItemsProviderRequest(startIndex, count, cts.Token))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Error,
                "Rask.Virtualize",
                "Rask VirtualizeModel: ItemsProvider threw",
                ex);
            return;
        }

        // If we were superseded by a newer fetch (or unmounted) while awaiting,
        // _activeFetch no longer points at our cts. Drop the result so we don't
        // clobber the cache with a stale window.
        if (cts.IsCancellationRequested || !ReferenceEquals(_activeFetch, cts))
        {
            return;
        }

        _cache ??= new Dictionary<int, object?>();
        for (var i = 0; i < result.Items.Count; i++)
        {
            _cache[startIndex + i] = result.Items[i];
        }

        _totalCount = result.TotalItemCount;
        _totalCountKnown = true;

#pragma warning disable S6966 // request the paint, never wait for the frame: nothing after this needs it sent
        StateHasChanged();
#pragma warning restore S6966
    }

    // Cancel any prior fetch so rapid scroll doesn't keep stale work running. Take the new
    // CTS before cancelling so a continuation observing _activeFetch can't race onto the
    // cancelled one. Also dispose the prior CTS — without this, every fetch leaks one
    // CancellationTokenSource (rapid scrolling = dozens per second) until GC reclaims it.
    private async Task<CancellationTokenSource> SupersedeActiveFetchAsync()
    {
        var prev = _activeFetch;
        var cts = new CancellationTokenSource();
        _activeFetch = cts;
        if (prev is not null)
        {
            try
            {
                await prev.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Already disposed by an unmount racing this fetch: nothing left to cancel.
            }

            prev.Dispose();
        }

        return cts;
    }
}
