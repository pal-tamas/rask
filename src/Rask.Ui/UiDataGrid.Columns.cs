using System.Linq.Expressions;

namespace Rask;

public sealed partial class UiDataGrid<T, TKey>
{
    // The columns, built once per render. ONCE is load-bearing: each `c.Field(…)` takes the next entry
    // slot under this grid, so calling the factory a second time in one render would hand every column a
    // different instance from the one the first pass built — and grow the slot list every frame.
    private List<UiColumn<T>> ResolveColumns()
    {
        if (_columnFactory is not Func<UiDataGrid<T, TKey>, IEnumerable<Component?>> factory)
        {
            return [];
        }

        var columns = new List<UiColumn<T>>();
        foreach (var child in factory(this))
        {
            // Anything that is not a column is ignored rather than rejected, which is what lets one arm
            // of a conditional be null — `admin ? c.Field(…) : null`.
            if (child is UiColumn<T> column)
            {
                columns.Add(column);
            }
        }

        return columns;
    }

    private bool IsHidden(UiColumn<T> column) =>
        column.FieldName is { } token && CurrentHidden.Contains(token, StringComparer.Ordinal);

    // A grouped column is drawn as band headings rather than as a column of one repeated value, so it
    // leaves the table unless the caller asks for it back.
    private bool IsGroupedAway(UiColumn<T> column) =>
        ShowGroupedColumns is not true
        && column.FieldName is { } token
        && CurrentGrouped.Contains(token, StringComparer.Ordinal);

    private List<UiColumn<T>> VisibleColumns(List<UiColumn<T>> columns)
    {
        return [.. Ordered(columns).Where(column => !IsHidden(column) && !IsGroupedAway(column))];
    }

    // Columns the order names, in the order it names them; everything else after, in declared order. A
    // token the order does not mention is not an error — a chooser that has moved one column has said
    // nothing about the rest.
    private List<UiColumn<T>> Ordered(List<UiColumn<T>> columns)
    {
        var order = CurrentOrder;
        if (order.Count == 0)
        {
            return columns;
        }

        var ranked = order
            .SelectMany(token => columns.Where(column => string.Equals(column.FieldName, token, StringComparison.Ordinal)))
            .Distinct()
            .ToList();
        ranked.AddRange(columns.Except(ranked));
        return ranked;
    }

    private List<UiColumn<T>> GroupColumns(IReadOnlyList<UiColumn<T>> columns)
    {
        return [.. CurrentGrouped.Select(token => Find(columns, token)).OfType<UiColumn<T>>()];
    }
}
