using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Testing;

#pragma warning disable RASK014 // test-defined Component subclasses are constructed directly

namespace Rask.Bootstrap.Tests;

// Grouping under the two SERVER-SIDE data modes, the paths BsDataGridGroupTests (in-memory lists) never
// reaches: an IQueryable, where the STORE has to order by the group columns first so bands stay whole within
// a page, and a TotalCount slice, where the CALLER orders and the grid bands whatever arrives. These are real
// code paths — Queried() leads the ORDER BY with the group columns (BsDataGrid.cs), and BodyRows bands the
// page regardless of where it came from — so they get their own functional coverage rather than riding on the
// in-memory tests.
public partial class BsDataGridGroupServerTests : global::Rask.Core.RaskMarkup
{
    private sealed record Row(string Name, string Category, string Supplier, int Qty);

    // Deliberately NOT ordered by category: the grid (in-memory) or the store (IQueryable) has to impose that.
    // Interleaved on purpose — banding this order as-is would repeat the bands, which is exactly what the
    // ordering guarantee prevents.
    private static readonly List<Row> Interleaved =
    [
        new("Apple", "Fruit", "Acme", 5),
        new("Carrot", "Veg", "Acme", 9),
        new("Banana", "Fruit", "Bolt", 3),
        new("Leek", "Veg", "Bolt", 2),
        new("Cherry", "Fruit", "Acme", 7),
    ];

    private static BsColumn<Row>[] Columns() =>
    [
        new BsColumn<Row>
        {
            Title = "Name", Value = r => r.Name, Field = r => r.Name, Sortable = true, SortBy = r => r.Name,
        },
        new BsColumn<Row>
        {
            Title = "Category", Value = r => r.Category, Field = r => r.Category, Groupable = true,
        },
        new BsColumn<Row>
        {
            Title = "Supplier", Value = r => r.Supplier, Field = r => r.Supplier, Groupable = true,
        },
        new BsColumn<Row>
        {
            Title = "Qty", Value = r => r.Qty, Field = r => r.Qty, Footer = rs => rs.Sum(x => x.Qty),
        },
    ];

    private sealed class Host(Func<Component> build) : Component
    {
        protected override Component? Render() => build();
    }

    // Counts store executions through the query PROVIDER (Count()/Skip()/Take()), so the cache can be asserted
    // rather than assumed — the same shape BsDataGridQueryTests uses.
    private sealed class CountingSource
    {
        public int Executions { get; private set; }

        public IEnumerable<Row> Rows(IEnumerable<Row> rows)
        {
            Executions++;
            foreach (var row in rows)
            {
                yield return row;
            }
        }
    }

    // The band header text, markup stripped: tags become spaces, runs collapse, so assertions read as the user
    // sees them (copied from BsDataGridGroupTests — the canonical shape).
    private static string[] BandTitles(string html) =>
        Regex.Matches(html, "<tr class=\"table-group-divider\".*?<td[^>]*>(.*?)</td>", RegexOptions.Singleline)
            .Select(m => Regex.Replace(Regex.Replace(m.Groups[1].Value, "<[^>]+>", " "), @"\s+", " ").Trim())
            .ToArray();

    // The first data cell of every DATA row — excludes band-header (table-group-divider) and subtotal
    // (table-light) rows so the assertions see the rows, in order.
    private static string[] FirstCells(string html)
    {
        var body = Regex.Match(html, "<tbody>(.*?)</tbody>", RegexOptions.Singleline).Groups[1].Value;
        return Regex.Matches(body, "<tr(?![^>]*table-group-divider)(?![^>]*table-light)[^>]*>(.*?)</tr>",
                RegexOptions.Singleline)
            .Select(r => Regex.Match(r.Groups[1].Value, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline)
                .Groups[1].Value)
            .ToArray();
    }

    // The header titles, markup stripped.
    private static string[] HeaderTitles(string html)
    {
        var head = Regex.Match(html, "<thead>(.*?)</thead>", RegexOptions.Singleline).Groups[1].Value;
        return Regex.Matches(head, "<th[^>]*>(.*?)</th>", RegexOptions.Singleline)
            .Select(m => Regex.Replace(Regex.Replace(m.Groups[1].Value, "<[^>]+>", " "), @"\s+", " ").Trim())
            .ToArray();
    }

    // --- IQueryable: the store orders --------------------------------------------------------------------
    //
    // These go through RaskTest.Render (a real render engine), not ToHtml(): an IQueryable Data throws under
    // the WASM engine that a static render would pick, and BsDataGridQueryTests renders the same way.

    [Fact]
    public void Query_Grouping_OrdersInTheStore_SoTheBandsAreWhole()
    {
        // THE load-bearing test for the server path. The source is interleaved (Fruit, Veg, Fruit, Veg, Fruit);
        // without leading the ORDER BY with the group column the store would return it interleaved and the same
        // band would repeat down the page. The grid leads with the group column, so each category appears once.
        var grid = RaskTest.Render(new Host(() => BsDataGrid.Data(Interleaved.AsQueryable())
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { })));

        Assert.Equal(["Category: Fruit (3)", "Category: Veg (2)"], BandTitles(grid.Html));
        Assert.Equal(["Apple", "Banana", "Cherry", "Carrot", "Leek"], FirstCells(grid.Html));
    }

    [Fact]
    public void Query_UserSort_AppliesWithinTheBands_NotAcrossThem()
    {
        // The group column leads the ORDER BY, then the user's SortBy is appended as a ThenBy — so "group by
        // category, sort by name descending" orders descending INSIDE each band, and the bands stay whole. A
        // sort that won over the grouping would scatter the bands across the page.
        var grid = RaskTest.Render(new Host(() => BsDataGrid.Data(Interleaved.AsQueryable())
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { })
            .Sort("name")
            .SortDescending(true)
            .OnSortChange(_ => { })));

        Assert.Equal(["Category: Fruit (3)", "Category: Veg (2)"], BandTitles(grid.Html));
        Assert.Equal(["Cherry", "Banana", "Apple", "Leek", "Carrot"], FirstCells(grid.Html));
    }

    [Fact]
    public void Query_NestedGrouping_LeadsTheOrderBy_InGroupOrder()
    {
        // Two group columns lead the ORDER BY in order (category, then supplier), so the rows arrive banded at
        // both levels: Fruit{Acme:Apple,Cherry; Bolt:Banana}, Veg{Acme:Carrot; Bolt:Leek}.
        var grid = RaskTest.Render(new Host(() => BsDataGrid.Data(Interleaved.AsQueryable())
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["category", "supplier"])
            .OnGroupedChange(_ => { })));

        Assert.Equal(["Apple", "Cherry", "Banana", "Carrot", "Leek"], FirstCells(grid.Html));
        Assert.Equal(
            [
                "Category: Fruit (3)", "Supplier: Acme (2)", "Supplier: Bolt (1)",
                "Category: Veg (2)", "Supplier: Acme (1)", "Supplier: Bolt (1)",
            ],
            BandTitles(grid.Html));
    }

    [Fact]
    public void Query_GroupedColumn_IsHiddenByDefault()
    {
        // Column hiding is orthogonal to where the data comes from: the grouped column folds away over an
        // IQueryable exactly as over a list, while the store still leads the ORDER BY with it.
        var grid = RaskTest.Render(new Host(() => BsDataGrid.Data(Interleaved.AsQueryable())
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { })));

        Assert.Equal(["Name", "Supplier", "Qty"], HeaderTitles(grid.Html));
        Assert.Contains("Category: Fruit", grid.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Query_Grouping_DoesNotDefeatTheQueryCache()
    {
        // The cache key folds in the grouping (string.Join(',', CurrentGrouped)), so a grouped grid still
        // caches: an unrelated re-render must not re-issue the two store round-trips.
        var source = new CountingSource();
        var query = source.Rows(Interleaved).AsQueryable();
        var grid = RaskTest.Render(new Host(() => BsDataGrid.Data(query)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { })));

        var afterFirst = source.Executions;
        Assert.True(afterFirst > 0, "the first render must run the query");

        grid.Render();
        grid.Render();

        Assert.Equal(afterFirst, source.Executions);
    }

    // --- TotalCount: the caller orders, the grid bands the slice ------------------------------------------
    //
    // Sliced() never reorders — it renders the page it was handed. BodyRows still bands it, so grouping works,
    // but whole bands depend on the caller ordering the slice. In-memory lists (ToHtml) are fine here.

    [Fact]
    public void TotalCount_Grouping_BandsTheCallerOrderedSlice()
    {
        // A pre-ordered slice + TotalCount + Grouped: the grid bands the slice it was handed. The caller
        // ordered by category, so the bands are whole even though the grid never reorders under TotalCount.
        var slice = new List<Row>
        {
            new("Apple", "Fruit", "Acme", 5),
            new("Banana", "Fruit", "Bolt", 3),
            new("Cherry", "Fruit", "Acme", 7),
            new("Carrot", "Veg", "Acme", 9),
            new("Leek", "Veg", "Bolt", 2),
        };

        var html = BsDataGrid.Data(slice)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .TotalCount(slice.Count)
            .PageSize(10)
            .Grouped(["category"])
            .OnGroupedChange(_ => { }).ToHtml();

        Assert.Equal(["Category: Fruit (3)", "Category: Veg (2)"], BandTitles(html));
        Assert.Equal(["Apple", "Banana", "Cherry", "Carrot", "Leek"], FirstCells(html));
    }

    [Fact]
    public void TotalCount_GroupedColumn_IsHiddenByDefault()
    {
        var slice = new List<Row>
        {
            new("Apple", "Fruit", "Acme", 5),
            new("Banana", "Fruit", "Bolt", 3),
            new("Carrot", "Veg", "Acme", 9),
        };

        var html = BsDataGrid.Data(slice)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .TotalCount(slice.Count)
            .PageSize(10)
            .Grouped(["category"])
            .OnGroupedChange(_ => { }).ToHtml();

        Assert.Equal(["Name", "Supplier", "Qty"], HeaderTitles(html));
        Assert.Contains("Category: Fruit", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TotalCount_UnorderedSlice_RepeatsBands_TheCallerMustOrder()
    {
        // The contract, made visible: the grid does NOT reorder a TotalCount slice. Hand it an interleaved
        // slice and the bands repeat — never silently, never thrown. This is why the docs say order the query.
        var html = BsDataGrid.Data(Interleaved)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .TotalCount(Interleaved.Count)
            .PageSize(10)
            .Grouped(["category"])
            .OnGroupedChange(_ => { }).ToHtml();

        // Interleaved Fruit/Veg/Fruit/Veg/Fruit → five bands, not two.
        Assert.Equal(
            ["Category: Fruit (1)", "Category: Veg (1)", "Category: Fruit (1)", "Category: Veg (1)",
             "Category: Fruit (1)"],
            BandTitles(html));
    }

    [Fact]
    public void TotalCount_GroupSubtotals_ArePageScoped()
    {
        // A subtotal reuses the column Footer over the BAND's rows on THIS page — never an invented whole-set
        // figure. The grid only holds the slice under TotalCount, so the subtotal can only ever total the slice.
        var slice = new List<Row>
        {
            new("Apple", "Fruit", "Acme", 5),
            new("Banana", "Fruit", "Bolt", 3),
            new("Cherry", "Fruit", "Acme", 7),
            new("Carrot", "Veg", "Acme", 9),
            new("Leek", "Veg", "Bolt", 2),
        };

        var html = BsDataGrid.Data(slice)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .TotalCount(999)
            .PageSize(10)
            .Grouped(["category"])
            .OnGroupedChange(_ => { })
            .GroupSubtotals(true).ToHtml();

        var subtotals = Regex.Matches(html, "<tr class=\"table-light\"[^>]*>(.*?)</tr>", RegexOptions.Singleline)
            .Select(m => Regex.Replace(m.Groups[1].Value, "<[^>]+>", "|")).ToArray();

        Assert.Equal(2, subtotals.Length);
        Assert.Contains("15", subtotals[0], StringComparison.Ordinal); // Fruit on this page: 5+3+7
        Assert.Contains("11", subtotals[1], StringComparison.Ordinal); // Veg on this page: 9+2
    }

    [Fact]
    public async Task TotalCount_GroupCollapsible_HidesTheBandRows()
    {
        var slice = new List<Row>
        {
            new("Apple", "Fruit", "Acme", 5),
            new("Banana", "Fruit", "Bolt", 3),
            new("Cherry", "Fruit", "Acme", 7),
            new("Carrot", "Veg", "Acme", 9),
            new("Leek", "Veg", "Bolt", 2),
        };

        var grid = RaskTest.Render(BsDataGrid.Data(slice)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .TotalCount(slice.Count)
            .PageSize(10)
            .Grouped(["category"])
            .OnGroupedChange(_ => { })
            .GroupCollapsible(true));

        Assert.Equal(5, FirstCells(grid.Html).Length);

        // [0] is the sortable Name header's own button; the band toggles follow it in document order.
        var toggles = Regex.Matches(grid.Html, "data-rask-on-click=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToArray();
        var html = await grid.InvokeAsync(toggles[1]); // collapse Fruit

        Assert.Equal(["Carrot", "Leek"], FirstCells(html));
        Assert.Equal(["Category: Fruit (3)", "Category: Veg (2)"], BandTitles(html)); // both headers stay
    }
}
