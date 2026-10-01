using System.Linq.Expressions;

namespace Rask;

public sealed partial class UiDataGrid<T, TKey>
{
    // Whichever the caller supplied. Both set would be a call-site bug, and the async one wins because
    // it is the one that does work.
    // One handler, either shape. This used to take the sync and async halves of a pair and decide which
    // won; the carrier holds exactly one, and `Invoke` hands back a completed ValueTask when it was the
    // synchronous one — `AsTask()` turns that into the cached completed task, no state machine created.
    private static Task Raise<TArg>(Callback<TArg> handler, TArg arg) => handler.Invoke(arg).AsTask();

    // Ascending, descending, then off. The third state is not decoration: it is the only way back to the
    // order the source itself chose, which for a query is whatever the store returns and for a list is
    // the order the caller put them in.
    private async Task ToggleSortAsync(UiColumn<T> column)
    {
        var token = column.FieldName;
        if (token is null)
        {
            return;
        }

        var (field, descending) = (string.Equals(CurrentSort, token, StringComparison.Ordinal), CurrentSortDescending) switch
        {
            (false, _) => (token, false),
            (true, false) => (token, true),
            _ => ((string?)null, false),
        };

        if (!SortControlled)
        {
            _sortField = field;
            _sortDescending = descending;
            // A new sort re-pages from the top: page four of the old order names different rows.
            _page = 0;
        }

        await Raise(OnSortChange, new UiGridSort(field, descending))
            .ConfigureAwait(false);
        await FetchAsync().ConfigureAwait(false);
    }

    private async Task GoToPageAsync(int page, int pageCount)
    {
        var target = Math.Clamp(page, 0, Math.Max(pageCount - 1, 0));
        if (target == CurrentPage)
        {
            return;
        }

        if (!PageControlled)
        {
            _page = target;
        }

        await Raise(OnPageChange, target).ConfigureAwait(false);
        await FetchAsync().ConfigureAwait(false);
    }

    private void ToggleExpand(object key)
    {
        if (!_expanded.Remove(key))
        {
            _expanded.Add(key);
        }
    }

    private void ToggleBand(string path)
    {
        if (!_collapsed.Remove(path))
        {
            _collapsed.Add(path);
        }
    }

    private Task SetGroupedAsync(IReadOnlyList<string> next)
    {
        if (!GroupControlled)
        {
            _grouped.Clear();
            _grouped.AddRange(next);
            // Bands are addressed by the path of keys above them, and regrouping renames every path.
            _collapsed.Clear();
            _page = 0;
        }

        return Raise(OnGroupedChange, next);
    }

    private Task SetHiddenAsync(IReadOnlyList<string> next)
    {
        if (!HideControlled)
        {
            _hidden.Clear();
            _hidden.AddRange(next);
        }

        return Raise(OnHiddenColumnsChange, next);
    }

    private Task SetOrderAsync(IReadOnlyList<string> next)
    {
        if (!OrderControlled)
        {
            _order.Clear();
            _order.AddRange(next);
        }

        return Raise(OnColumnOrderChange, next);
    }

    private Task ToggleHiddenAsync(string token)
    {
        var next = new List<string>(CurrentHidden);
        if (!next.Remove(token))
        {
            next.Add(token);
        }

        return SetHiddenAsync(next);
    }

    private Task GroupByAsync(string token)
    {
        if (CurrentGrouped.Contains(token, StringComparer.Ordinal))
        {
            return Task.CompletedTask;
        }

        return SetGroupedAsync([.. CurrentGrouped, token]);
    }

    private Task UngroupAsync(string token)
    {
        var next = new List<string>(CurrentGrouped);
        return next.Remove(token) ? SetGroupedAsync(next) : Task.CompletedTask;
    }

    private static Task MoveAsync(IReadOnlyList<string> list, string token, int delta,
        Func<IReadOnlyList<string>, Task> commit)
    {
        var next = new List<string>(list);
        var from = next.IndexOf(token);
        if (from < 0)
        {
            return Task.CompletedTask;
        }

        var to = from + delta;
        if (to < 0 || to >= next.Count)
        {
            return Task.CompletedTask;
        }

        next.RemoveAt(from);
        next.Insert(to, token);
        return commit(next);
    }

    private Task MoveGroupAsync(string token, int delta) =>
        MoveAsync(CurrentGrouped, token, delta, SetGroupedAsync);

    // Reordering a column needs the FULL order to move within, not the sparse one the parent may have
    // given: a token nobody has named yet still has a position, and moving its neighbour past it has to
    // move it too.
    private Task MoveColumnAsync(IReadOnlyList<UiColumn<T>> columns, string token, int delta) =>
        MoveAsync(EffectiveOrder(columns), token, delta, SetOrderAsync);

    private List<string> EffectiveOrder(IReadOnlyList<UiColumn<T>> columns)
    {
        var order = new List<string>(CurrentOrder);
        foreach (var column in columns)
        {
            if (column.FieldName is { } token && !order.Contains(token, StringComparer.Ordinal))
            {
                order.Add(token);
            }
        }

        return order;
    }
}
