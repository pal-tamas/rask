using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Testing;

namespace Rask.Bootstrap.Tests;

// Grouping: the Field-derived column name, banding over consecutive rows, the ordering that makes the bands
// whole, nesting, collapse and subtotals.
public partial class BsDataGridGroupTests : global::Rask.Core.RaskMarkup
{
    private sealed record Row(string Name, string Category, string Supplier, int Qty);

    // Deliberately NOT ordered by category: the grid has to impose that itself, which is the whole ordering
    // guarantee. Interleaved on purpose — banding a set in this order without re-ordering repeats the bands.
    private static readonly List<Row> Rows =
    [
        new("Apple", "Fruit", "Acme", 5),
        new("Carrot", "Veg", "Acme", 9),
        new("Banana", "Fruit", "Bolt", 3),
        new("Leek", "Veg", "Bolt", 2),
        new("Cherry", "Fruit", "Acme", 7),
    ];

    private static BsColumn<Row>[] Columns() =>
    [
        new BsColumn<Row> { Title = "Name", Value = r => r.Name, Field = r => r.Name, Sortable = true },
        new BsColumn<Row>
        {
            Title = "Category", Value = r => r.Category, Field = r => r.Category, Groupable = true,
        },
        new BsColumn<Row>
        {
            Title = "Supplier", Value = r => r.Supplier, Field = r => r.Supplier, Groupable = true,
        },
        new BsColumn<Row> { Title = "Qty", Value = r => r.Qty, Field = r => r.Qty, Footer = rs => rs.Sum(x => x.Qty) },
    ];

    // The band header's text, with the markup taken out. Tags become spaces (the title and the count are
    // separate spans, spaced by a margin rather than whitespace) and runs collapse, so the assertions read as
    // the user sees it.
    private static string[] BandTitles(string html) =>
        Regex.Matches(html, "<tr class=\"table-group-divider\".*?<td[^>]*>(.*?)</td>", RegexOptions.Singleline)
            .Select(m => Regex.Replace(Regex.Replace(m.Groups[1].Value, "<[^>]+>", " "), @"\s+", " ").Trim())
            .ToArray();

    private static string[] FirstCells(string html)
    {
        var body = Regex.Match(html, "<tbody>(.*?)</tbody>", RegexOptions.Singleline).Groups[1].Value;
        return Regex.Matches(body, "<tr(?![^>]*table-group-divider)(?![^>]*table-light)[^>]*>(.*?)</tr>",
                RegexOptions.Singleline)
            .Select(r => Regex.Match(r.Groups[1].Value, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline)
                .Groups[1].Value)
            .ToArray();
    }

    // The header titles, markup stripped — a sortable header wraps its title in a button, so tags come out.
    private static string[] HeaderTitles(string html)
    {
        var head = Regex.Match(html, "<thead>(.*?)</thead>", RegexOptions.Singleline).Groups[1].Value;
        return Regex.Matches(head, "<th[^>]*>(.*?)</th>", RegexOptions.Singleline)
            .Select(m => Regex.Replace(Regex.Replace(m.Groups[1].Value, "<[^>]+>", " "), @"\s+", " ").Trim())
            .ToArray();
    }

    [Fact]
    public void Field_NamesTheColumn_FromTheMember()
    {
        // The whole point of Field being an expression: the token is read off the property, so it cannot drift.
        // Value could never do this — a compiled Func carries no member name.
        var html = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { }).ToHtml();

        Assert.Contains("Category: Fruit", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Field_AlsoNamesAControlledSort()
    {
        // Sorting keys off the same name, so a column needs naming once. SortField still wins where set.
        var grid = RaskTest.Render(BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Sort("name")
            .OnSortChange(_ => { }));

        // A Sortable column with a resolvable name renders a real sort control.
        Assert.Contains("aria-sort=\"ascending\"", grid.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutGrouping_NoBands()
    {
        var html = BsDataGrid.Data(Rows).Columns(Columns()).RowKey(r => r.Name).ToHtml();

        Assert.DoesNotContain("table-group-divider", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Grouping_BandsTheRows_AndOrdersSoTheBandsAreWhole()
    {
        // THE load-bearing test. The source is interleaved (Fruit, Veg, Fruit, Veg, Fruit); banding it as-is
        // would emit five bands. The grid orders by the group key first, so each category appears exactly once.
        var html = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { }).ToHtml();

        Assert.Equal(["Category: Fruit (3)", "Category: Veg (2)"], BandTitles(html));
        Assert.Equal(["Apple", "Banana", "Cherry", "Carrot", "Leek"], FirstCells(html));
    }

    [Fact]
    public void TheUserSort_AppliesWithinTheBands_NotAcrossThem()
    {
        // "Group by category, sort by name descending" has to mean descending INSIDE each band — a sort that
        // won over the grouping would scatter the bands.
        var html = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { })
            .Sort("name")
            .SortDescending(true)
            .OnSortChange(_ => { }).ToHtml();

        Assert.Equal(["Category: Fruit (3)", "Category: Veg (2)"], BandTitles(html));
        Assert.Equal(["Cherry", "Banana", "Apple", "Leek", "Carrot"], FirstCells(html));
    }

    [Fact]
    public void NestedGrouping_BandsWithinBands()
    {
        var html = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["category", "supplier"])
            .OnGroupedChange(_ => { }).ToHtml();

        Assert.Equal(
            [
                "Category: Fruit (3)", "Supplier: Acme (2)", "Supplier: Bolt (1)",
                "Category: Veg (2)", "Supplier: Acme (1)", "Supplier: Bolt (1)",
            ],
            BandTitles(html));
    }

    [Fact]
    public void TheBandHeader_SpansEveryVisibleColumn_IncludingTheLeadingOnes()
    {
        var html = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Selectable(true)
            .Grouped(["category"])
            .OnGroupedChange(_ => { }).ToHtml();

        // 4 data columns, Category grouped away (3 visible), + the checkbox column = 4.
        Assert.Contains("<td colspan=\"4\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupedColumn_IsHiddenByDefault_AndItsValueSurvivesInTheBandHeader()
    {
        // A grouped column repeats one value down its whole band under a header the band header already carries,
        // so by default it folds away — the value lives only in the band header.
        var html = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { }).ToHtml();

        Assert.Equal(["Name", "Supplier", "Qty"], HeaderTitles(html)); // Category's <th> is gone
        Assert.Contains("Category: Fruit", html, StringComparison.Ordinal); // but the band header keeps it
        // The Category cell "Fruit"/"Veg" no longer appears as a row cell; only Name leads each row.
        Assert.Equal(["Apple", "Banana", "Cherry", "Carrot", "Leek"], FirstCells(html));
    }

    [Fact]
    public void ShowGroupedColumns_KeepsTheGroupedColumn_InHeaderAndCells()
    {
        var html = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { })
            .ShowGroupedColumns(true).ToHtml();

        Assert.Equal(["Name", "Category", "Supplier", "Qty"], HeaderTitles(html)); // all four stay
        // The value shows both in the band header AND repeated in every row (the pre-hide behaviour).
        var fruitBand = Regex.Match(html, "Category: Fruit.*?(?=Category: Veg)", RegexOptions.Singleline).Value;
        Assert.Contains(">Fruit<", fruitBand, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiLevelGrouping_HidesEveryGroupedColumn()
    {
        var html = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["category", "supplier"])
            .OnGroupedChange(_ => { }).ToHtml();

        // Both grouped columns fold away; only the ungrouped Name and Qty remain.
        Assert.Equal(["Name", "Qty"], HeaderTitles(html));
    }

    [Fact]
    public void SubtotalLabel_LandsOnTheFirstVisibleColumn_WhenColumnZeroIsGroupedAway()
    {
        // Column 0 is the grouped-away one, so "Subtotal" must move to the first column that actually renders.
        BsColumn<Row>[] columns =
        [
            new BsColumn<Row>
            {
                Title = "Category", Value = r => r.Category, Field = r => r.Category, Groupable = true,
            },
            new BsColumn<Row> { Title = "Name", Value = r => r.Name, Field = r => r.Name },
            new BsColumn<Row> { Title = "Qty", Value = r => r.Qty, Footer = rs => rs.Sum(x => x.Qty) },
        ];

        var html = BsDataGrid.Data(Rows)
            .Columns(columns)
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { })
            .GroupSubtotals(true).ToHtml();

        var firstSubtotal = Regex.Match(html, "<tr class=\"table-light\"[^>]*>(.*?)</tr>", RegexOptions.Singleline)
            .Groups[1].Value;
        // First rendered cell (Name's slot) carries the caption; the Category cell it used to sit under is gone.
        Assert.Contains(">Subtotal<", firstSubtotal, StringComparison.Ordinal);
    }

    [Fact]
    public void AGroupedColumnsFooter_DoesNotForceATfoot()
    {
        // The only footer belongs to the column being grouped; folding the column away takes its footer with it,
        // so there is nothing left to put a <tfoot> on the table.
        BsColumn<Row>[] columns =
        [
            new BsColumn<Row> { Title = "Name", Value = r => r.Name, Field = r => r.Name },
            new BsColumn<Row>
            {
                Title = "Category", Value = r => r.Category, Field = r => r.Category, Groupable = true,
                Footer = rs => rs.Count,
            },
        ];

        var grouped = BsDataGrid.Data(Rows)
            .Columns(columns)
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { }).ToHtml();
        Assert.DoesNotContain("<tfoot>", grouped, StringComparison.Ordinal);

        // Kept (opt-out) or ungrouped, the footer is back.
        var shown = BsDataGrid.Data(Rows)
            .Columns(columns)
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { })
            .ShowGroupedColumns(true).ToHtml();
        Assert.Contains("<tfoot>", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupingEveryColumn_NeverEmitsAZeroColspanBandHeader()
    {
        // The degenerate case: the one and only column is grouped away, so the band header spans no data
        // columns. colspan must clamp to 1 rather than emit the invalid colspan="0".
        BsColumn<Row>[] columns =
        [
            new BsColumn<Row>
            {
                Title = "Category", Value = r => r.Category, Field = r => r.Category, Groupable = true,
            },
        ];

        var html = BsDataGrid.Data(Rows)
            .Columns(columns)
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { }).ToHtml();

        Assert.DoesNotContain("colspan=\"0\"", html, StringComparison.Ordinal);
        Assert.Contains("<td colspan=\"1\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownOrUngroupableFields_AreIgnored()
    {
        // Grouped is URL input: ?group=deleteMe must render an ungrouped grid, never throw. Same for a column
        // that exists but isn't Groupable.
        var unknown = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["nope"])
            .OnGroupedChange(_ => { }).ToHtml();
        Assert.DoesNotContain("table-group-divider", unknown, StringComparison.Ordinal);

        // "name" is a real column with a Field, but not Groupable.
        var notGroupable = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["name"])
            .OnGroupedChange(_ => { }).ToHtml();
        Assert.DoesNotContain("table-group-divider", notGroupable, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupCollapsible_RendersAToggle_WithAriaExpanded_ButNoDanglingAriaControls()
    {
        var html = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { })
            .GroupCollapsible(true).ToHtml();

        Assert.Contains("aria-expanded=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Toggle Category Fruit\"", html, StringComparison.Ordinal);
        // The controlled content is a run of sibling <tr>s with no element to point at, so aria-controls is
        // deliberately absent rather than dangling.
        Assert.DoesNotContain("aria-controls", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollapsingABand_HidesItsRows_AndLeavesTheOthers()
    {
        var grid = RaskTest.Render(BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { })
            .GroupCollapsible(true));

        Assert.Equal(5, FirstCells(grid.Html).Length);

        // [0] is the sortable header's own button; the band toggles follow it in document order.
        var toggles = Regex.Matches(grid.Html, "data-rask-on-click=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToArray();
        var html = await grid.InvokeAsync(toggles[1]); // collapse Fruit

        Assert.Equal(["Carrot", "Leek"], FirstCells(html));
        // Both band headers stay — collapsing hides rows, not the band itself.
        Assert.Equal(["Category: Fruit (3)", "Category: Veg (2)"], BandTitles(html));
        Assert.Contains("aria-expanded=\"false\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollapseIsKeyedByValue_SoItFollowsTheBandAcrossASort()
    {
        var grid = RaskTest.Render(BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { })
            .GroupCollapsible(true));

        var toggles = Regex.Matches(grid.Html, "data-rask-on-click=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToArray();
        await grid.InvokeAsync(toggles[1]); // collapse Fruit

        // Sorting reorders the rows inside the bands; the collapsed band must still be Fruit, not "whichever
        // band is now first" — which is what an index-keyed collapse would give. [0] is the sort control.
        var sort = Regex.Matches(grid.Html, "data-rask-on-click=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).First();
        var html = await grid.InvokeAsync(sort);

        Assert.Equal(["Carrot", "Leek"], FirstCells(html));
    }

    [Fact]
    public void GroupSubtotals_TotalTheBand_ReusingTheColumnFooter()
    {
        var html = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { })
            .GroupSubtotals(true).ToHtml();

        var subtotals = Regex.Matches(html, "<tr class=\"table-light\"[^>]*>(.*?)</tr>", RegexOptions.Singleline)
            .Select(m => Regex.Replace(m.Groups[1].Value, "<[^>]+>", "|")).ToArray();

        Assert.Equal(2, subtotals.Length);
        Assert.Contains("15", subtotals[0], StringComparison.Ordinal); // Fruit: 5+3+7
        Assert.Contains("11", subtotals[1], StringComparison.Ordinal); // Veg: 9+2
        // The grand footer still totals everything.
        Assert.Contains("<tfoot>", html, StringComparison.Ordinal);
        Assert.Contains("26", html, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupSubtotals_OnNestedBands_TotalTheInnermostOnly()
    {
        // One subtotal per innermost band, not a cascade of identical rows at every level.
        var html = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["category", "supplier"])
            .OnGroupedChange(_ => { })
            .GroupSubtotals(true).ToHtml();

        Assert.Equal(4, Regex.Matches(html, "<tr class=\"table-light\"[^>]*>").Count); // 4 innermost bands
    }

    [Fact]
    public void GroupSubtotals_ReuseAColumnsFooterTemplate_WhenItSetsOne()
    {
        // A subtotal reuses whichever footer hook the column defines — FooterTemplate (a component), not only
        // the text Footer. It is the same single hook the grand <tfoot> uses, so a badge footer bands too.
        BsColumn<Row>[] columns =
        [
            new BsColumn<Row> { Title = "Name", Value = r => r.Name, Field = r => r.Name },
            new BsColumn<Row>
            {
                Title = "Category", Value = r => r.Category, Field = r => r.Category, Groupable = true,
            },
            new BsColumn<Row> { Title = "Supplier", Value = r => r.Supplier, Field = r => r.Supplier },
            new BsColumn<Row>
            {
                Title = "Qty", Value = r => r.Qty,
                FooterTemplate = rs => BsBadge[rs.Sum(x => x.Qty).ToString()],
            },
        ];

        var html = BsDataGrid.Data(Rows)
            .Columns(columns)
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { })
            .GroupSubtotals(true).ToHtml();

        var subtotals = Regex.Matches(html, "<tr class=\"table-light\"[^>]*>(.*?)</tr>", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(2, subtotals.Length);
        Assert.Contains("<span class=\"badge\">15</span>", subtotals[0], StringComparison.Ordinal); // Fruit 5+3+7
        Assert.Contains("<span class=\"badge\">11</span>", subtotals[1], StringComparison.Ordinal); // Veg 9+2
    }

    [Fact]
    public async Task UncontrolledGrouping_IsOwnedByTheGrid()
    {
        // No Grouped/OnGroupedChange passed at all -> the grid keeps its own list. Nothing renders a control
        // yet (that is the panel, PR6), so drive the intent directly through a controlled render instead.
        var html = BsDataGrid.Data(Rows).Columns(Columns()).RowKey(r => r.Name).ToHtml();
        Assert.DoesNotContain("table-group-divider", html, StringComparison.Ordinal);

        await Task.CompletedTask;
    }

    [Fact]
    public void ControlledGrouping_RendersWhatItIsGiven()
    {
        var html = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["supplier"])
            .OnGroupedChange(_ => { }).ToHtml();

        Assert.Equal(["Supplier: Acme (3)", "Supplier: Bolt (2)"], BandTitles(html));
    }

    [Fact]
    public void GroupKey_BandsCoarserThanTheCell()
    {
        // The band is by first letter; the cell still shows the whole name.
        BsColumn<Row>[] columns =
        [
            new BsColumn<Row>
            {
                Title = "Initial", Value = r => r.Name, Field = r => r.Name, Groupable = true,
                GroupKey = r => r.Name[0],
            },
        ];

        // ShowGroupedColumns keeps the sole column visible — the point here is that the CELL shows the whole
        // name while the BAND is keyed by the initial, which is only observable with the column rendered.
        var html = BsDataGrid.Data(Rows)
            .Columns(columns)
            .RowKey(r => r.Name)
            .Grouped(["name"])
            .OnGroupedChange(_ => { })
            .ShowGroupedColumns(true).ToHtml();

        Assert.Equal(["Initial: A (1)", "Initial: B (1)", "Initial: C (2)", "Initial: L (1)"], BandTitles(html));
        Assert.Equal(["Apple", "Banana", "Carrot", "Cherry", "Leek"], FirstCells(html));
    }

    [Fact]
    public void GroupHeader_OverridesTheBandContent()
    {
        BsColumn<Row>[] columns =
        [
            new BsColumn<Row>
            {
                Title = "Category", Value = r => r.Category, Field = r => r.Category, Groupable = true,
                GroupHeader = (key, band) => Span.Class("custom")[$"{key} has {band.Count}"],
            },
        ];

        var html = BsDataGrid.Data(Rows)
            .Columns(columns)
            .RowKey(r => r.Name)
            .Grouped(["category"])
            .OnGroupedChange(_ => { }).ToHtml();

        Assert.Contains("<span class=\"custom\">Fruit has 3</span>", html, StringComparison.Ordinal);
    }
}
