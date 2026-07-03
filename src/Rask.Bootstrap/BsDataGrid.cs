using Rask.Core;

namespace Rask.Bootstrap;

// One column of a BsDataGrid<T>. Value gives the cell text (ToString'd); Template overrides it with a
// custom cell. Sortable makes the header a sort toggle, ordering by SortKey (falling back to Value when
// it is IComparable). Class is applied to both the header and the cells (e.g. Txt.End() for numbers).
public sealed class BsColumn<T>
{
    public string Title { get; init; } = "";
    public Func<T, object?>? Value { get; init; }
    public Func<T, Component>? Template { get; init; }
    public bool Sortable { get; init; }
    public Func<T, IComparable?>? SortKey { get; init; }
    public string? Class { get; init; }

    internal IComparable? Sort(T row) =>
        SortKey is not null ? SortKey(row) : Value?.Invoke(row) as IComparable;

    internal Component Cell(T row) =>
        Template is not null ? Template(row) : (Value?.Invoke(row)?.ToString() ?? "");
}

// A data grid over an in-memory sequence: typed columns, click-to-sort headers, and optional client-side
// paging (PageSize > 0). Server-rendered — sorting and paging mutate component state and re-render, so it
// suits small/medium result sets; for large sets, page in the query and pass one page as Data. Cells bind
// straight to T (no view-model). The richer feature set (filtering, selection, master-detail) is layered
// on later; this is the BsTable replacement for list screens.
public sealed class BsDataGrid<T> : Component
{
    public IReadOnlyList<T>? Data { get; set; }
    public IReadOnlyList<BsColumn<T>>? Columns { get; set; }

    // Rows per page; 0 (default) shows everything with no pager.
    public int PageSize { get; set; } = 0;

    public bool Striped { get; set; } = true;
    public bool Hover { get; set; } = true;
    public bool Small { get; set; } = false;
    public bool Responsive { get; set; } = true;

    // Stable per-row key (defaults to the row index) and an optional empty-state placeholder.
    public Func<T, object?>? RowKey { get; set; }
    public Component? Empty { get; set; }

    private int _page;
    private int _sortColumn = -1;
    private bool _sortDescending;

    private void ToggleSort(int column)
    {
        if (_sortColumn == column)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            _sortDescending = false;
        }

        _page = 0;
    }

    protected override Component? Render()
    {
        var columns = Columns ?? [];
        var data = Data ?? [];

        if (data.Count == 0 && Empty is not null)
            return Empty;

        IEnumerable<T> view = data;
        if (_sortColumn >= 0 && _sortColumn < columns.Count && columns[_sortColumn].Sortable)
        {
            var column = columns[_sortColumn];
            view = _sortDescending
                ? data.OrderByDescending(column.Sort)
                : data.OrderBy(column.Sort);
        }

        var rows = view.ToList();
        var pageCount = PageSize > 0 ? (rows.Count + PageSize - 1) / PageSize : 1;
        if (_page >= pageCount)
            _page = Math.Max(0, pageCount - 1);

        var pageRows = PageSize > 0
            ? rows.Skip(_page * PageSize).Take(PageSize).ToList()
            : rows;

        return
        [
            BsTable(Striped: Striped, Hover: Hover, Small: Small, Responsive: Responsive)[
                Thead()[Tr()[HeaderCells(columns)]],
                Tbody()[BodyRows(columns, pageRows)]
            ],
            PageSize > 0 && pageCount > 1 ? Pager(pageCount, rows.Count) : null
        ];
    }

    private IEnumerable<Component> HeaderCells(IReadOnlyList<BsColumn<T>> columns)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            if (!column.Sortable)
            {
                yield return Th(Class: column.Class)[column.Title];
                continue;
            }

            var index = i;
            var caret = _sortColumn == index
                ? BsIcon(Name: _sortDescending ? BsIconName.CaretDownFill : BsIconName.CaretUpFill,
                    Class: Margin.Start(1))
                : null;
            yield return Th(Class: column.Class)[
                Button(Class: Bs.Join("btn btn-sm btn-link text-decoration-none", Padding.All(0), Font.Semibold),
                    OnClick: () => ToggleSort(index))[column.Title, caret]
            ];
        }
    }

    private IEnumerable<Component> BodyRows(IReadOnlyList<BsColumn<T>> columns, IReadOnlyList<T> pageRows)
    {
        for (var r = 0; r < pageRows.Count; r++)
        {
            var row = pageRows[r];
            var key = RowKey?.Invoke(row) ?? r;
            yield return Tr(Key: key)[Cells(columns, row)];
        }
    }

    private static IEnumerable<Component> Cells(IReadOnlyList<BsColumn<T>> columns, T row)
    {
        for (var c = 0; c < columns.Count; c++)
            yield return Td(Class: columns[c].Class)[columns[c].Cell(row)];
    }

    // A range summary next to a compact pager (prev / windowed page numbers / next), all via Bs components
    // and utilities.
    private Component Pager(int pageCount, int total)
    {
        var from = _page * PageSize + 1;
        var to = Math.Min((_page + 1) * PageSize, total);

        return Div(Class: Bs.Join(Display.Flex(), Flex.Justify(BsJustify.Between), Flex.Align(BsAlign.Center)))[
            Span(Class: Bs.Join(Txt.Color(BsColor.Secondary), Font.Small))[$"{from}-{to} / {total}"],
            BsPagination(Size: BsSize.Sm, Class: Margin.Bottom(0))[PageItems(pageCount)]
        ];
    }

    private IEnumerable<Component> PageItems(int pageCount)
    {
        yield return BsPageItem(Key: "prev", Disabled: _page == 0, OnClick: () => _page--)[
            BsIcon(Name: BsIconName.ChevronLeft)];

        // A small sliding window around the current page keeps the pager compact for many pages.
        var start = Math.Max(0, _page - 2);
        var end = Math.Min(pageCount - 1, start + 4);
        start = Math.Max(0, end - 4);

        for (var p = start; p <= end; p++)
        {
            var target = p;
            yield return BsPageItem(Key: p, Active: p == _page, OnClick: () => _page = target)[(p + 1).ToString()];
        }

        yield return BsPageItem(Key: "next", Disabled: _page == pageCount - 1, OnClick: () => _page++)[
            BsIcon(Name: BsIconName.ChevronRight)];
    }
}
