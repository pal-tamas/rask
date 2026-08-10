using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Testing;

#pragma warning disable RASK014 // test-defined Component subclasses are constructed directly

namespace Rask.Bootstrap.Tests;

// The feature-by-feature suites each cover one axis. This one covers where the axes CROSS — an IQueryable with
// a controlled sort, footers over a store-side page, master-detail over a query, a page index out of range —
// plus the degenerate inputs a real app eventually passes. Combinations are where a grid actually breaks.
public class BsDataGridMatrixTests
{
    private sealed record Row(int Id, string Name, int Qty);

    private static readonly List<Row> All =
    [
        new(1, "Banana", 3),
        new(2, "Apple", 5),
        new(3, "Cherry", 1),
        new(4, "Date", 9),
        new(5, "Elderberry", 7),
    ];

    private sealed class Host(Func<Component> build) : Component
    {
        protected override Component? Render() => build();
    }

    // Every ordering seam at once: SortKey (in memory), SortBy (in the store), SortField (reported out).
    private static BsColumn<Row>[] Columns() =>
    [
        new BsColumn<Row>
        {
            Title = "Name", Value = r => r.Name, Sortable = true,
            SortKey = r => r.Name, SortBy = r => r.Name, SortField = "name",
        },
        new BsColumn<Row>
        {
            Title = "Qty", Value = r => r.Qty, Sortable = true,
            SortKey = r => r.Qty, SortBy = r => r.Qty, SortField = "qty",
        },
    ];

    private static string[] BodyCells(string html, int column)
    {
        var body = Regex.Match(html, "<tbody>(.*?)</tbody>", RegexOptions.Singleline).Groups[1].Value;
        return Regex.Matches(body, "<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline)
            .Select(r => Regex.Matches(r.Groups[1].Value, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline))
            .Where(cells => cells.Count > column)
            .Select(cells => cells[column].Groups[1].Value)
            .ToArray();
    }

    // === IQueryable × controlled sort ======================================================================
    // The real server-side shape: the URL owns the sort, the database applies it.

    [Fact]
    public void Queryable_WithControlledSort_OrdersInTheStoreByTheControlledField()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All.AsQueryable(), Columns: Columns(), PageSize: 2,
            Sort: "qty", SortDescending: true, OnSortChange: _ => { })));

        // Sort names the column via SortField; the store then orders by that column's SortBy.
        Assert.Equal(["Date", "Elderberry"], BodyCells(grid.Html, 0)); // 9, 7
        Assert.Contains("aria-sort=\"descending\"", grid.Html);
    }

    [Fact]
    public async Task Queryable_WithControlledSort_ReportsTheClick_AndDoesNotReorderItself()
    {
        var asked = new List<DataGridSort>();
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All.AsQueryable(), Columns: Columns(), PageSize: 5,
            Sort: null, OnSortChange: s => asked.Add(s))));

        var html = await grid.InvokeAsync(grid.HandlerIds("click")[0]);

        Assert.Equal(new DataGridSort("name", false), Assert.Single(asked));
        // The caller owns the sort and hasn't changed it, so the query's own order stands.
        Assert.Equal(["Banana", "Apple", "Cherry", "Date", "Elderberry"], BodyCells(html, 0));
    }

    [Fact]
    public void Queryable_WithControlledPage_RendersTheCallersPageFromTheStore()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All.AsQueryable(), Columns: Columns(), PageSize: 2,
            Page: 2, OnPageChange: _ => { })));

        Assert.Equal(["Elderberry"], BodyCells(grid.Html, 0));
        Assert.Contains("5-5 / 5", grid.Html);
    }

    // === IQueryable × the other features ===================================================================

    [Fact]
    public void Queryable_FootersSeeOnlyTheCurrentPage_NotTheWholeSet()
    {
        // The documented semantic difference: with a list the grid holds every row, so footers total all of
        // them; with a query it only ever materialises one page, and inventing a whole-set total from a slice
        // would be a lie.
        BsColumn<Row>[] columns =
        [
            new BsColumn<Row> { Title = "Name", Value = r => r.Name },
            new BsColumn<Row> { Title = "Qty", Value = r => r.Qty, Footer = rows => rows.Sum(r => r.Qty) },
        ];

        var queryable = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All.AsQueryable(), Columns: columns, PageSize: 2)));
        Assert.Contains("<tfoot><tr><td></td><td>8</td></tr></tfoot>", queryable.Html); // 3 + 5, this page

        var list = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All, Columns: columns, PageSize: 2)));
        Assert.Contains("<tfoot><tr><td></td><td>25</td></tr></tfoot>", list.Html); // every row
    }

    [Fact]
    public async Task Queryable_SupportsMasterDetail()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Id: "g", Data: All.AsQueryable(), Columns: Columns(), PageSize: 2,
            RowKey: r => r.Id, ExpandedContent: r => Div()[$"detail-{r.Name}"])));

        var html = await grid.InvokeAsync(grid.HandlerIds("click")[2]); // first row's expander

        Assert.Contains("<div>detail-Banana</div>", html);
        Assert.Contains("aria-expanded=\"true\"", html);
    }

    [Fact]
    public void Queryable_WithNoPaging_RendersEveryRow()
    {
        // PageSize 0: no pager, and the whole query materialises. Legitimate for a small table.
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All.AsQueryable(), Columns: Columns())));

        Assert.Equal(5, BodyCells(grid.Html, 0).Length);
        Assert.DoesNotContain("pagination", grid.Html);
    }

    [Fact]
    public void Queryable_IgnoresTotalCount_BecauseItCountsTheStoreItself()
    {
        // Both set is contradictory input. The query knows the real count, so it wins — rather than trusting a
        // number that disagrees with the rows on screen.
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All.AsQueryable(), TotalCount: 999, Columns: Columns(), PageSize: 2)));

        Assert.Contains("1-2 / 5", grid.Html);
        Assert.DoesNotContain("999", grid.Html);
    }

    [Fact]
    public async Task Queryable_CacheIsKeyedOnTheQuery_SoASwappedQueryReloads()
    {
        // A filter change swaps the query instance, and the cache must not serve the old page for the new
        // query. The swap is driven by real component state: a host reading a captured local would never be
        // marked dirty, so the render cache would (rightly) reuse its subtree and prove nothing.
        var host = RaskTest.Render(new QueryFilterHost());
        Assert.Equal(5, BodyCells(host.Html, 0).Length);

        var html = await host.InvokeAsync(host.HandlerIds("click")[0]); // flip the filter

        Assert.Equal(["Date", "Elderberry"], BodyCells(html, 0)); // 9, 7
    }

    private sealed class QueryFilterHost : Component
    {
        private bool _filtered;

        protected override Component? Render() =>
        [
            Button.Type("button").OnClick(() => _filtered = !_filtered)["filter"],
            BsDataGrid<Row>(
                Data: (_filtered ? All.Where(r => r.Qty > 5) : All).AsQueryable(),
                Columns: Columns(), PageSize: 5)
        ];
    }

    // === TotalCount edge cases =============================================================================

    [Fact]
    public void TotalCount_SmallerThanThePageSize_RendersNoPager()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All.Take(2).ToList(), TotalCount: 2, Columns: Columns(), PageSize: 10)));

        Assert.DoesNotContain("pagination", grid.Html);
        Assert.Equal(2, BodyCells(grid.Html, 0).Length);
    }

    [Fact]
    public void TotalCount_WithNoPageSize_RendersTheSliceAndNoPager()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All.Take(2).ToList(), TotalCount: 5, Columns: Columns())));

        Assert.Equal(2, BodyCells(grid.Html, 0).Length);
        Assert.DoesNotContain("pagination", grid.Html);
    }

    [Fact]
    public void ControlledPage_BeyondTheLastPage_ClampsRatherThanRenderingNothing()
    {
        // A stale ?page=99 in the URL must not produce an empty grid with no way back.
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All, Columns: Columns(), PageSize: 2, Page: 99, OnPageChange: _ => { })));

        Assert.NotEmpty(BodyCells(grid.Html, 0));
        Assert.Equal(["Elderberry"], BodyCells(grid.Html, 0)); // the last page
    }

    [Fact]
    public void ControlledPage_Negative_ClampsToTheFirstPage()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All, Columns: Columns(), PageSize: 2, Page: -5, OnPageChange: _ => { })));

        Assert.Equal(["Banana", "Apple"], BodyCells(grid.Html, 0));
    }

    // === Degenerate inputs =================================================================================

    [Fact]
    public void NoData_AndNoEmpty_RendersHeadersAndAnEmptyBody()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(Columns: Columns())));

        Assert.Contains("<thead>", grid.Html);
        Assert.Contains("<tbody></tbody>", grid.Html);
    }

    [Fact]
    public void NoColumns_RendersAnEmptyHeaderRow_AndDoesNotThrow()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(Data: All)));

        Assert.Contains("<thead><tr></tr></thead>", grid.Html);
    }

    [Fact]
    public void ColumnWithNeitherValueNorTemplate_RendersAnEmptyCell()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All.Take(1).ToList(), Columns: [new BsColumn<Row> { Title = "X" }])));

        Assert.Contains("<tbody><tr data-rask-key=\"0\"><td></td></tr></tbody>", grid.Html);
    }

    [Fact]
    public void NullCellValue_RendersEmpty_RatherThanThrowing()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All.Take(1).ToList(),
            Columns: [new BsColumn<Row> { Title = "X", Value = _ => null }])));

        Assert.Contains("<td></td>", grid.Html);
    }

    [Fact]
    public void ExpandedContentReturningNull_RendersNoDetailRow()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Id: "g", Data: All.Take(1).ToList(), Columns: Columns(),
            RowKey: r => r.Id, ExpandedContent: _ => null)));

        // The expander still renders (the grid can't know the callback will decline until it asks).
        Assert.Contains("aria-expanded=\"false\"", grid.Html);
        Assert.DoesNotContain("colspan", grid.Html);
    }

    [Fact]
    public async Task ExpansionWithoutRowKey_FollowsThePosition_NotTheRow()
    {
        // RowKey is documented as required for master-detail. Without it rows key by index, so a sort moves
        // the open row. This pins the documented consequence rather than pretending it works.
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Id: "g", Data: All, Columns: Columns(), ExpandedContent: r => Div()[$"detail-{r.Name}"])));

        await grid.InvokeAsync(grid.HandlerIds("click")[2]);            // open row 0 (Banana)
        var html = await grid.InvokeAsync(grid.HandlerIds("click")[0]); // sort by Name -> Apple is row 0

        Assert.Contains("<div>detail-Apple</div>", html);   // the position stayed open...
        Assert.DoesNotContain("<div>detail-Banana</div>", html); // ...not the row
    }

    [Fact]
    public void TwoGridsOnOnePage_DoNotShareDetailRowIds()
    {
        // The ids must be unique across grids, or aria-controls on one resolves into the other.
        var grid = RaskTest.Render(new Host(() =>
            Div()[
                BsDataGrid<Row>(Data: All.Take(1).ToList(), Columns: Columns(),
                    RowKey: r => r.Id, ExpandedContent: _ => Div()["a"]),
                BsDataGrid<Row>(Data: All.Take(1).ToList(), Columns: Columns(),
                    RowKey: r => r.Id, ExpandedContent: _ => Div()["b"])
            ]));

        var ids = Regex.Matches(grid.Html, "aria-controls=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void SortableColumnWithNoOrderingAtAll_LeavesTheOrderAlone()
    {
        // Sortable with no SortKey/Value to compare: the header still works, the order simply doesn't change.
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All, Columns: [new BsColumn<Row> { Title = "X", Sortable = true }])));

        Assert.Contains("aria-sort=\"none\"", grid.Html);
    }

    [Fact]
    public void PageSizeLargerThanTheData_RendersEveryRowAndNoPager()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All, Columns: Columns(), PageSize: 99)));

        Assert.Equal(5, BodyCells(grid.Html, 0).Length);
        Assert.DoesNotContain("pagination", grid.Html);
    }

    [Fact]
    public void AllDensityFlagsOff_RendersABareTable()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All.Take(1).ToList(), Columns: Columns(),
            Striped: false, Hover: false, Small: false, Responsive: false)));

        Assert.Contains("<table class=\"table\">", grid.Html);
        Assert.DoesNotContain("table-responsive", grid.Html);
    }

    [Fact]
    public void FooterWithMasterDetail_GetsALeadingBlankCell_SoColumnsLineUp()
    {
        BsColumn<Row>[] columns =
            [new BsColumn<Row> { Title = "Qty", Value = r => r.Qty, Footer = rows => rows.Sum(r => r.Qty) }];

        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All, Columns: columns, RowKey: r => r.Id, ExpandedContent: _ => Div()["d"])));

        // One spacer for the expander column, then the total.
        Assert.Contains("<tfoot><tr><td></td><td>25</td></tr></tfoot>", grid.Html);
    }

    [Fact]
    public void ActivePage_IsMarkedForAssistiveTech()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All, Columns: Columns(), PageSize: 2)));

        Assert.Contains("aria-current=\"page\"", grid.Html);
    }
}
