using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Rask.Core.Live;
using Rask.Core.Virtualization;

namespace Rask.Core.Components;

// Headless list virtualization: renders no DOM of its own, takes a Render delegate, hands
// it a VirtualizationState describing the currently visible item window. The user wires
// the state's OnScroll handler into a scrollable container and reads OffsetBefore /
// OffsetAfter / VisibleValues to drive their own markup.
//
// To preserve row identity across scroll / reorder, set Data["rask-key"] = item.Index
// (or any stable per-row identity) on the row element. The client morph engages a keyed
// reconciliation path when any child carries data-rask-key, moving existing DOM nodes
// into place instead of replacing them — focus and scroll state survive the
// re-render. See /virtualize in the showcase for the pattern.
//
// Always invoked through the hand-written generic factory `Components.Virtualize<T>(...)`
// (see Virtualize.Generics.cs). The non-generic factory remains as the forwarding target.
//
// Items vs ItemsProvider — exactly one must be set:
//   • Items: IEnumerable in memory; window is sliced directly.
//   • ItemsProvider (typed as Func<ItemsProviderRequest, ValueTask<ItemsProviderResultErased>>):
//     async paging. Component caches loaded items by global index, requests missing windows
//     in the background, and re-renders on completion. The first render before the count
//     is known emits an all-placeholder window so the user can render a Loading view.
public sealed class Virtualize : Component
{
    private CancellationTokenSource? _activeFetch;
    private Dictionary<int, object?>? _cache;
    private int _clientHeight;
    private IEnumerable? _lastItems;
    private Delegate? _lastProvider;
    private Action<ScrollEvent>? _onScrollDelegate;
    private int _scrollTop;
    private int _totalCount;
    private bool _totalCountKnown;

    public IEnumerable? Items { get; set; }

    // Stored as a Delegate? so the non-generic factory's signature stays simple. The typed
    // Virtualize<T>(...) factory wraps the user's
    //   Func<ItemsProviderRequest, ValueTask<ItemsProviderResult<T>>>
    // into the erased
    //   Func<ItemsProviderRequest, ValueTask<ItemsProviderResultErased>>
    // before forwarding. Internally we cast back to the erased shape.
    public Delegate? ItemsProvider { get; set; }

    public int ItemSize { get; set; }
    public int OverscanCount { get; set; }

    // Pre-scroll initial viewport guess. Drives the first render's window size before any
    // scroll event has arrived to set _clientHeight. Should be the scrollable container's
    // approximate visible height in px.
    public int InitialClientHeight { get; set; }

    // The render fragment. Called with the type-erased VirtualizationState every render;
    // returns the user's chosen root Component for the virtualized region. Stored under the
    // name "Body" rather than "Render" to avoid colliding with Component.Render(). The
    // user-facing typed factory Virtualize<T>(...) exposes this parameter as "Render".
    public Func<VirtualizationState, Component>? Body { get; set; }

    // Virtualize reads mutable internal state (scroll position, item cache, total count
    // from the async provider) that the framework can't observe through props alone, so
    // every render must re-execute. Without this, the cached Div from the last render
    // would be returned even after a scroll event changed _scrollTop. Same reasoning as
    // Router/Outlet — see Component.BypassRenderCache.
    protected override bool BypassRenderCache => true;

    protected override void OnPropsChanged()
    {
        // Treat an Items or ItemsProvider reference swap as a full reset: the prior cache,
        // scroll position, and active fetch all belong to the old data source.
        var itemsSwapped = !ReferenceEquals(_lastItems, Items);
        var providerSwapped = !ReferenceEquals(_lastProvider, ItemsProvider);
        if (!itemsSwapped && !providerSwapped)
        {
            return;
        }

        _lastItems = Items;
        _lastProvider = ItemsProvider;
        _cache = null;
        _totalCount = 0;
        _totalCountKnown = false;
        CancelAndDisposeActiveFetch();
    }

    protected override void OnUnmount()
    {
        // Cancel any in-flight fetch when the component leaves the tree so the
        // provider's await unwinds promptly and the CTS doesn't leak through GC.
        // Without this, a Virtualize whose ItemsProvider is mid-`await` keeps the
        // CTS rooted via the captured request.CancellationToken and the
        // FetchAsync continuation still tries to update _cache + call
        // StateHasChanged after disposal — wasted work and a small unmanaged
        // resource leak per CTS.
        CancelAndDisposeActiveFetch();
    }

    private void CancelAndDisposeActiveFetch()
    {
        var fetch = _activeFetch;
        if (fetch is null)
        {
            return;
        }

        _activeFetch = null;
        try { fetch.Cancel(); }
        catch (ObjectDisposedException) { }

        fetch.Dispose();
    }

    protected override Component Render()
    {
        if (Body is null)
        {
            throw new InvalidOperationException("Virtualize: Render delegate is required.");
        }

        if (Items is null == ItemsProvider is null)
        {
            throw new InvalidOperationException(
                "Virtualize: provide exactly one of Items or ItemsProvider.");
        }

        if (ItemSize <= 0)
        {
            throw new InvalidOperationException("Virtualize: ItemSize must be greater than zero.");
        }

        var itemSize = ItemSize;
        var clientHeight = _clientHeight > 0
            ? _clientHeight
            : Math.Max(InitialClientHeight, itemSize);
        var overscan = Math.Max(0, OverscanCount);

        int totalCount;
        if (Items is not null)
        {
            totalCount = CountItems(Items);
            _totalCount = totalCount;
            _totalCountKnown = true;
        }
        else
        {
            totalCount = _totalCountKnown ? _totalCount : 0;
        }

        var startIndex = Math.Max(0, (_scrollTop / itemSize) - overscan);
        var endIndex = Math.Min(
            totalCount,
            ((_scrollTop + clientHeight + itemSize - 1) / itemSize) + overscan);
        if (endIndex < startIndex)
        {
            endIndex = startIndex;
        }

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
            ScheduleAsyncFetch(startIndex, visibleCount, placeholders, overscan, clientHeight, itemSize);
        }

        _onScrollDelegate ??= HandleScroll;

        var offsetBefore = startIndex * itemSize;
        var offsetAfter = Math.Max(0, totalCount - endIndex) * itemSize;

        var state = new VirtualizationState(
            values,
            indices,
            placeholders,
            startIndex,
            totalCount,
            itemSize,
            offsetBefore,
            offsetAfter,
            _onScrollDelegate);

        return Body(state);
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

    private void HandleScroll(ScrollEvent e)
    {
        _scrollTop = Math.Max(0, e.ScrollTop);
        if (e.ClientHeight > 0)
        {
            _clientHeight = e.ClientHeight;
        }
        // The dispatcher's post-handler render already picks up this state mutation —
        // the owner is marked _stateDirty when TryInvokeHandlerAsync looks up the handler.
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
        Justification = "ItemsProvider is set by Virtualize<T>(...) which wraps the typed provider " +
                        "into the exact erased delegate shape we cast to here. No reflection.")]
    private async Task FetchAsync(int startIndex, int count)
    {
        if (ItemsProvider is not Func<ItemsProviderRequest, ValueTask<ItemsProviderResultErased>> provider)
        {
            return;
        }

        // Cancel any prior fetch so rapid scroll doesn't keep stale work running. Take the new
        // CTS before cancelling so a continuation observing _activeFetch can't race onto the
        // cancelled one. Also dispose the prior CTS — without this, every fetch leaks one
        // CancellationTokenSource (rapid scrolling = dozens per second) until GC reclaims it.
        var prev = _activeFetch;
        var cts = new CancellationTokenSource();
        _activeFetch = cts;
        if (prev is not null)
        {
            try { prev.Cancel(); }
            catch (ObjectDisposedException) { }

            prev.Dispose();
        }

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
            Console.Error.WriteLine($"Rask Virtualize: ItemsProvider threw: {ex}");
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

        StateHasChanged();
    }
}

// Type-erased ItemsProvider result. The typed Virtualize<T>(...) factory boxes the user's
// IReadOnlyList<T> into IReadOnlyList<object?> before handing the closure off to the
// non-generic component. Kept internal — users only see the typed ItemsProviderResult<T>.
internal sealed record ItemsProviderResultErased(IReadOnlyList<object?> Items, int TotalItemCount);
