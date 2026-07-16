using BenchmarkDotNet.Attributes;
using Rask.Bootstrap;
using Rask.Core;
using BS = Rask.Bootstrap.Generated;

namespace Rask.Benchmarks;

// BsDataGrid<T>'s live-render path. The grid is the framework's most render-heavy component and the one apps
// push the most rows through, so it gets the same before/after Allocated scrutiny as the Core hot paths —
// every feature added to it multiplies across rows × columns.
//
// RenderAsLiveRoot (not ToHtml) is the function under test: only the live path registers handlers, and
// handler registration is exactly what the per-cell features cost.
//
// The pairs are deliberate. Each "…Plain" benchmark is the baseline for the feature benchmark beside it, so a
// feature's true cost is one subtraction rather than a comparison against a different tree.
[MemoryDiagnoser]
public class BsDataGridBenchmarks
{
    private sealed record Product(string Name, string Category, int Stock, decimal Price, string Sku,
        string Supplier);

    private static readonly List<Product> Rows = Build(100);

    private static List<Product> Build(int n)
    {
        var rows = new List<Product>(n);
        for (var i = 0; i < n; i++)
        {
            // Category (7) and Supplier (3) are deliberately LOW cardinality — the shape people group by.
            // Grouping by a unique column (Sku) is a different measurement: one band per row, so the band
            // headers dominate and the number reports cardinality rather than the cost of a nesting level.
            rows.Add(new Product($"Product {i}", $"Cat {i % 7}", i % 50, 10m + i, $"SKU-{i:D4}",
                $"Supplier {i % 3}"));
        }

        return rows;
    }

    // Five Value columns: plain encoded text, the cheapest possible cell, so the deltas below are the
    // feature's cost rather than the cells'.
    private static BsColumn<Product>[] Columns() =>
    [
        new BsColumn<Product> { Title = "Name", Value = p => p.Name, Sortable = true },
        new BsColumn<Product> { Title = "Category", Value = p => p.Category, Sortable = true },
        new BsColumn<Product> { Title = "SKU", Value = p => p.Sku },
        new BsColumn<Product> { Title = "Stock", Value = p => p.Stock },
        new BsColumn<Product> { Title = "Price", Value = p => p.Price },
    ];

    // A grid nobody has opted into anything on: the floor, and the marker that the added features stay free
    // when unused. 100 rows unpaged — the shape that hurts most.
    [Benchmark(Baseline = true)]
    public string Grid100_Plain() =>
        BS.BsDataGrid(Data: Rows, Columns: Columns(), RowKey: p => p.Name).RenderAsLiveRoot();

    // The same grid with a row click. The delta against Grid100_Plain is the honest price of OnRowClick:
    // one handler id per CLICKABLE CELL (5 per row here, so ~500), not one per row. The row's callback
    // closure itself is built once per row and shared across its cells.
    [Benchmark]
    public string Grid100_RowClick() =>
        BS.BsDataGrid(Data: Rows, Columns: Columns(), RowKey: p => p.Name, OnRowClick: _ => { })
            .RenderAsLiveRoot();

    // RowClass runs a delegate per row and joins a class. Cheap by design; measured so it stays that way.
    [Benchmark]
    public string Grid100_RowClass() =>
        BS.BsDataGrid(Data: Rows, Columns: Columns(), RowKey: p => p.Name,
            RowClass: p => p.Stock == 0 ? "table-warning" : null).RenderAsLiveRoot();

    // The paged shape most list screens actually render: 20 of 100. Sorting/paging still walk the whole set
    // to count and slice, so this is not simply a fifth of the unpaged cost.
    [Benchmark]
    public string Grid100_Paged20() =>
        BS.BsDataGrid(Data: Rows, Columns: Columns(), RowKey: p => p.Name, PageSize: 20).RenderAsLiveRoot();

    // Selection adds ONE cell per row (not one per column, unlike OnRowClick) plus the select-all box, so the
    // delta against Grid100_Plain should stay roughly linear in rows.
    [Benchmark]
    public string Grid100_Selectable() =>
        BS.BsDataGrid(Data: Rows, Columns: Columns(), RowKey: p => p.Name, Selectable: true).RenderAsLiveRoot();

    // Controlled selection with half the rows picked. This is the one that would show a regression if the
    // selection were tested with a LINQ Contains over the key list instead of a HashSet built once per render:
    // that is O(rows × selected) per render, and it would grow quadratically here rather than linearly.
    [Benchmark]
    public string Grid100_Selected50() =>
        BS.BsDataGrid(Data: Rows, Columns: Columns(), RowKey: p => p.Name,
            SelectedKeys: SelectedHalf, OnSelectionChange: _ => { }).RenderAsLiveRoot();

    private static readonly IReadOnlyList<object> SelectedHalf =
        Rows.Where((_, i) => i % 2 == 0).Select(p => (object)p.Name).ToList();

    // Grouping re-orders the whole set (group keys first, user sort within) and boxes a key per row per level
    // to detect the band runs. 7 categories over 100 rows.
    [Benchmark]
    public string Grid100_Grouped() =>
        BS.BsDataGrid(Data: Rows, Columns: GroupableColumns(), RowKey: p => p.Name,
            Grouped: ["category"], OnGroupedChange: _ => { }).RenderAsLiveRoot();

    // Two levels: the second re-bands inside each band, so the key boxing doubles. The delta against
    // Grid100_Grouped is what a nesting level actually costs.
    [Benchmark]
    public string Grid100_GroupedNested() =>
        BS.BsDataGrid(Data: Rows, Columns: GroupableColumns(), RowKey: p => p.Name,
            Grouped: ["category", "supplier"], OnGroupedChange: _ => { }).RenderAsLiveRoot();

    private static BsColumn<Product>[] GroupableColumns() =>
    [
        new BsColumn<Product> { Title = "Name", Value = p => p.Name, Field = p => p.Name, Sortable = true },
        new BsColumn<Product>
        {
            Title = "Category", Value = p => p.Category, Field = p => p.Category, Groupable = true,
        },
        new BsColumn<Product>
        {
            Title = "Supplier", Value = p => p.Supplier, Field = p => p.Supplier, Groupable = true,
        },
        new BsColumn<Product> { Title = "Stock", Value = p => p.Stock },
        new BsColumn<Product> { Title = "Price", Value = p => p.Price },
    ];

    // The column chooser turned on but its menu closed — the common resting state. The delta against
    // Grid100_Plain is the cost of making every header a drag source (four drag handlers per header) plus the
    // one toolbar button. It does NOT touch the per-cell path, so it should stay roughly flat in rows.
    [Benchmark]
    public string Grid100_ColumnChooser() =>
        BS.BsDataGrid(Data: Rows, Columns: NamedColumns(), RowKey: p => p.Name, ColumnChooser: true)
            .RenderAsLiveRoot();

    // The chooser actually in use: one column hidden and the order reversed. This is the path that allocates —
    // VisibleColumns reorders and filters — so the delta against Grid100_ColumnChooser is what a laid-out grid
    // costs over an idle one. Fewer rendered columns (one hidden) partly offsets it.
    [Benchmark]
    public string Grid100_HiddenReordered() =>
        BS.BsDataGrid(Data: Rows, Columns: NamedColumns(), RowKey: p => p.Name, ColumnChooser: true,
            HiddenColumns: ["category"], ColumnOrder: ["price", "stock", "sku", "category", "name"],
            OnHiddenColumnsChange: _ => { }, OnColumnOrderChange: _ => { }).RenderAsLiveRoot();

    // Every column named, so both the chooser and reorder can address all five.
    private static BsColumn<Product>[] NamedColumns() =>
    [
        new BsColumn<Product> { Title = "Name", Value = p => p.Name, Field = p => p.Name, Sortable = true },
        new BsColumn<Product> { Title = "Category", Value = p => p.Category, Field = p => p.Category },
        new BsColumn<Product> { Title = "SKU", Value = p => p.Sku, Field = p => p.Sku },
        new BsColumn<Product> { Title = "Stock", Value = p => p.Stock, Field = p => p.Stock },
        new BsColumn<Product> { Title = "Price", Value = p => p.Price, Field = p => p.Price },
    ];
}
