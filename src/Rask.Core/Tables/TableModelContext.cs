namespace Rask.Core.Tables;

// The typed view TableModel hands to its Render delegate. TableModel is fully controlled: every value
// here is derived from the props the host passed (Rows + Sort/PageIndex/SelectedKeys, …), and every
// action only *proposes* a change by invoking the matching event (OnSort / OnPage / OnSelect) — it
// never mutates the table. The host applies the proposal to its own state and re-renders, and the new
// props flow back down.
public sealed class TableModelContext<T>
{
    // Headers in column-definition order.
    public required IReadOnlyList<HeaderCell> Headers { get; init; }

    // The rows the host supplied (already sorted + sliced by the host), each wrapped with its identity
    // and selection flag.
    public required IReadOnlyList<TableRow<T>> Rows { get; init; }

    // ----- sorting (echoes the Sort prop) -----
    public required IReadOnlyList<ColumnSort> Sort { get; init; }

    // Propose the next sort for a column id: asc → desc → removed. Replaces the whole sort unless
    // MultiSort is set, in which case it appends/updates/removes that column while preserving the rest.
    public required Action<string> ToggleSort { get; init; }

    // Propose an empty sort.
    public required Action ClearSort { get; init; }

    // ----- pagination (echoes the page props) -----
    public required int PageIndex { get; init; }
    public required int PageCount { get; init; }
    public required int PageSize { get; init; }
    public required int TotalRowCount { get; init; }
    public required bool HasPrevPage { get; init; }
    public required bool HasNextPage { get; init; }

    // Propose a page index (clamped to [0, PageCount-1]); fires OnPage only when the clamped target
    // differs from the current PageIndex.
    public required Action<int> SetPage { get; init; }
    public required Action NextPage { get; init; }
    public required Action PrevPage { get; init; }

    // ----- selection (over the current Rows window) -----
    public required IReadOnlyCollection<object> SelectedKeys { get; init; }
    public required Func<T, bool> IsSelected { get; init; }

    // Propose toggling one row's key in/out of the selection set.
    public required Action<T> ToggleRow { get; init; }

    // Propose selecting every row in the current window, or clearing them when all are already
    // selected. Operates only on the rows the host passed (the current page) — cross-page select-all
    // is the host's concern.
    public required Action ToggleAll { get; init; }
    public required bool AllSelected { get; init; }
}
