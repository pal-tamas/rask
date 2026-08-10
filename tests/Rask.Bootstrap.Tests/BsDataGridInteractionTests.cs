using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Testing;

#pragma warning disable RASK014 // test-defined Component subclasses are constructed directly

namespace Rask.Bootstrap.Tests;

// BsDataGrid<T>'s state transitions, driven through real click handlers. These were previously untested
// anywhere: the static suite renders only the initial frame, and the sort/page/expand paths are reachable
// only by clicking. RaskTest dispatches handlers in-process and re-renders, so none of this needs a browser.
public partial class BsDataGridInteractionTests : global::Rask.Core.RaskMarkup
{
    private sealed record Row(string Name, int Qty);

    private static readonly List<Row> Rows =
    [
        new("Banana", 3),
        new("Apple", 5),
        new("Cherry", 1),
    ];

    private static BsColumn<Row>[] Columns() =>
    [
        new BsColumn<Row> { Title = "Name", Value = r => r.Name, Sortable = true },
        new BsColumn<Row> { Title = "Qty", Value = r => r.Qty, Sortable = true },
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

    [Fact]
    public async Task ClickingASortableHeader_ReordersTheRows_AndFlipsAriaSort()
    {
        var grid = RaskTest.Render(BsDataGrid<Row>(Data: Rows, Columns: Columns()));
        Assert.Equal(["Banana", "Apple", "Cherry"], BodyCells(grid.Html, 0));

        var html = await grid.InvokeAsync(grid.HandlerIds("click")[0]);
        Assert.Equal(["Apple", "Banana", "Cherry"], BodyCells(html, 0));
        Assert.Contains("aria-sort=\"ascending\"", html);
        Assert.Contains("bi-caret-up-fill", html);

        // A second click on the same header flips to descending, exactly reversed.
        html = await grid.InvokeAsync(Markup.Attrs(html, "data-rask-on-click")[0]);
        Assert.Equal(["Cherry", "Banana", "Apple"], BodyCells(html, 0));
        Assert.Contains("aria-sort=\"descending\"", html);
        Assert.Contains("bi-caret-down-fill", html);
    }

    [Fact]
    public async Task SortingASecondColumn_MovesTheCaretAndTheSortState()
    {
        var grid = RaskTest.Render(BsDataGrid<Row>(Data: Rows, Columns: Columns()));

        await grid.InvokeAsync(grid.HandlerIds("click")[0]);            // by Name
        var html = await grid.InvokeAsync(grid.HandlerIds("click")[1]); // then by Qty

        Assert.Equal(["Cherry", "Banana", "Apple"], BodyCells(html, 0)); // 1, 3, 5
        // Only one column may claim a direction, and only it shows a caret.
        Assert.Single(Regex.Matches(html, "aria-sort=\"ascending\""));
        Assert.Single(Regex.Matches(html, "bi-caret-up-fill"));
    }

    [Fact]
    public async Task ClickingAPage_ShowsADisjointSlice_AndUpdatesTheRangeSummary()
    {
        var grid = RaskTest.Render(BsDataGrid<Row>(Data: Rows, Columns: Columns(), PageSize: 2));
        var first = BodyCells(grid.Html, 0);
        Assert.Contains("1-2 / 3", grid.Html);

        // Handlers: [0] Name, [1] Qty, [2] prev, [3] page 1, [4] page 2, [5] next.
        var html = await grid.InvokeAsync(grid.HandlerIds("click")[4]);

        var second = BodyCells(html, 0);
        Assert.Contains("3-3 / 3", html);
        // Disjoint, not merely different — this is what catches a wrong Skip offset.
        Assert.Empty(second.Intersect(first));
    }

    [Fact]
    public async Task NextAndPrev_WalkThePages()
    {
        var grid = RaskTest.Render(BsDataGrid<Row>(Data: Rows, Columns: Columns(), PageSize: 2));

        var html = await grid.InvokeAsync(grid.HandlerIds("click")[5]); // next
        Assert.Equal(["Cherry"], BodyCells(html, 0));

        html = await grid.InvokeAsync(Markup.Attrs(html, "data-rask-on-click")[2]); // prev
        Assert.Equal(["Banana", "Apple"], BodyCells(html, 0));
        Assert.Contains("1-2 / 3", html);
    }

    [Fact]
    public async Task PrevOnTheFirstPage_DoesNotUnderflow()
    {
        // Regression: BsPageItem's Disabled only adds a CSS class, so the prev button stays clickable. An
        // unguarded decrement went to page -1 and rendered "-1-0 / 3".
        var grid = RaskTest.Render(BsDataGrid<Row>(Data: Rows, Columns: Columns(), PageSize: 2));

        var html = await grid.InvokeAsync(grid.HandlerIds("click")[2]); // prev, already on page 1

        Assert.Contains("1-2 / 3", html);
        Assert.DoesNotContain("-1-0", html);
        Assert.Equal(["Banana", "Apple"], BodyCells(html, 0));
    }

    [Fact]
    public async Task NextOnTheLastPage_DoesNotOverflow()
    {
        var grid = RaskTest.Render(BsDataGrid<Row>(Data: Rows, Columns: Columns(), PageSize: 2));

        await grid.InvokeAsync(grid.HandlerIds("click")[5]);            // -> page 2 (last)
        var html = await grid.InvokeAsync(grid.HandlerIds("click")[5]); // next again

        Assert.Contains("3-3 / 3", html);
        Assert.Equal(["Cherry"], BodyCells(html, 0));
    }

    [Fact]
    public async Task Sorting_ResetsToTheFirstPage()
    {
        var grid = RaskTest.Render(BsDataGrid<Row>(Data: Rows, Columns: Columns(), PageSize: 2));

        var html = await grid.InvokeAsync(grid.HandlerIds("click")[4]); // page 2
        Assert.Contains("3-3 / 3", html);

        html = await grid.InvokeAsync(Markup.Attrs(html, "data-rask-on-click")[0]); // sort by Name

        Assert.Contains("1-2 / 3", html);
    }

    [Fact]
    public async Task PagingKeepsFooterTotals_SpanningEveryRow()
    {
        // The trap: totalling the visible page instead of the whole set.
        BsColumn<Row>[] columns =
        [
            new BsColumn<Row> { Title = "Name", Value = r => r.Name, Sortable = true },
            new BsColumn<Row> { Title = "Qty", Value = r => r.Qty, Footer = rows => rows.Sum(r => r.Qty) },
        ];
        var grid = RaskTest.Render(BsDataGrid<Row>(Data: Rows, Columns: columns, PageSize: 2));
        Assert.Contains("<tfoot><tr><td></td><td>9</td></tr></tfoot>", grid.Html);

        var html = await grid.InvokeAsync(grid.HandlerIds("click")[3]); // page 2

        Assert.Equal(["Cherry"], BodyCells(html, 0));
        Assert.Contains("<tfoot><tr><td></td><td>9</td></tr></tfoot>", html);
    }

    [Fact]
    public async Task ClickingTheExpander_OpensAndClosesTheDetailRow()
    {
        var grid = RaskTest.Render(BsDataGrid<Row>(Id: "g", Data: Rows, Columns: Columns(),
            RowKey: r => r.Name, ExpandedContent: r => Div[$"detail-{r.Name}"]));
        Assert.DoesNotContain("<div>detail-Banana</div>", grid.Html);

        var html = await grid.InvokeAsync(grid.HandlerIds("click")[2]); // first row's expander
        Assert.Contains("<div>detail-Banana</div>", html);
        // The detail id is derived from the row's position, not from RowKey: a RowKey like "Espresso Machine"
        // would put a space in the id, and aria-controls is a space-separated id list — one space silently
        // turns the reference into two tokens that resolve to nothing.
        Assert.Contains(
            "aria-expanded=\"true\" aria-controls=\"g-detail-0\" aria-label=\"Toggle details\"", html);
        Assert.Contains("<tr id=\"g-detail-0\"", html);
        Assert.Contains("bi-chevron-down", html);
        // The detail row spans the expander column plus both data columns.
        Assert.Contains("<td colspan=\"3\">", html);

        html = await grid.InvokeAsync(Markup.Attrs(html, "data-rask-on-click")[2]);
        Assert.DoesNotContain("<div>detail-Banana</div>", html);
        Assert.DoesNotContain("aria-expanded=\"true\"", html);
    }

    [Fact]
    public async Task DetailId_StaysAValidIdToken_WhenTheRowKeyContainsSpaces()
    {
        // Regression: interpolating the RowKey into the id produced "g-detail-Two Words". aria-controls is a
        // SPACE-SEPARATED id list, so that resolves to two tokens matching nothing — silently breaking the
        // association for exactly the screen-reader users it exists for. The documented pattern in the guide
        // (RowKey: p => p.Name) hits this immediately.
        List<Row> rows = [new("Two Words", 1)];
        var grid = RaskTest.Render(BsDataGrid<Row>(Id: "g", Data: rows, Columns: Columns(),
            RowKey: r => r.Name, ExpandedContent: _ => Div["d"]));

        var html = await grid.InvokeAsync(grid.HandlerIds("click")[2]);

        var controls = Regex.Match(html, "aria-controls=\"([^\"]+)\"").Groups[1].Value;
        Assert.DoesNotContain(" ", controls);
        // The id it names must actually exist in the document.
        Assert.Contains($"<tr id=\"{controls}\"", html);
    }

    [Fact]
    public async Task TwoRowsCanBeOpenAtOnce_AndClosingOneKeepsTheOther()
    {
        // Each detail row is a keyed insert, so opening a second must not disturb the first.
        var grid = RaskTest.Render(BsDataGrid<Row>(Id: "g", Data: Rows, Columns: Columns(),
            RowKey: r => r.Name, ExpandedContent: r => Div[$"detail-{r.Name}"]));

        await grid.InvokeAsync(grid.HandlerIds("click")[2]); // Banana
        // Banana's detail row shifts the handler list, so Apple's expander is now at [3].
        var html = await grid.InvokeAsync(grid.HandlerIds("click")[3]);
        Assert.Contains("<div>detail-Banana</div>", html);
        Assert.Contains("<div>detail-Apple</div>", html);

        html = await grid.InvokeAsync(Markup.Attrs(html, "data-rask-on-click")[2]); // close Banana
        Assert.DoesNotContain("<div>detail-Banana</div>", html);
        Assert.Contains("<div>detail-Apple</div>", html);
    }

    [Fact]
    public async Task ExpansionFollowsTheRow_AcrossASort()
    {
        // Keyed by RowKey rather than position, so re-ordering keeps the same row open.
        var grid = RaskTest.Render(BsDataGrid<Row>(Id: "g", Data: Rows, Columns: Columns(),
            RowKey: r => r.Name, ExpandedContent: r => Div[$"detail-{r.Name}"]));

        await grid.InvokeAsync(grid.HandlerIds("click")[2]);            // open Banana (first row)
        var html = await grid.InvokeAsync(grid.HandlerIds("click")[0]); // sort -> Apple, Banana, Cherry

        Assert.Equal(["Apple", "Banana", "Cherry"], BodyCells(html, 1));
        Assert.Contains("<div>detail-Banana</div>", html);
        Assert.DoesNotContain("<div>detail-Apple</div>", html);
    }

    [Fact]
    public async Task SortState_SurvivesAnEmptyRoundTrip()
    {
        // The Empty placeholder replaces the entire grid, so a filter that empties the list and then restores
        // it must not silently reset the user's sort. Sort/page/expansion live on BsDataGrid itself, and the
        // component keeps its identity across the round-trip, so they survive.
        var host = RaskTest.Render(new FilterHost());

        // Handlers: [0] the host's filter toggle, then the grid's sortable headers.
        var html = await host.InvokeAsync(host.HandlerIds("click")[1]); // sort by Name
        Assert.Equal(["Apple", "Banana", "Cherry"], BodyCells(html, 0));

        html = await host.InvokeAsync(Markup.Attrs(html, "data-rask-on-click")[0]); // filter everything out
        Assert.Contains("<div>none</div>", html);
        Assert.DoesNotContain("<table", html);

        html = await host.InvokeAsync(Markup.Attrs(html, "data-rask-on-click")[0]); // and back
        Assert.Equal(["Apple", "Banana", "Cherry"], BodyCells(html, 0));
    }

    // A parent that swaps the Data reference, the way a real filter would. Mutating one list in place would
    // leave the props reference-equal and the render cache would rightly skip the re-render.
    private sealed class FilterHost : Component
    {
        private bool _empty;

        protected override Component? Render() =>
        [
            Button.Type("button").OnClick(() => _empty = !_empty)["filter"],
            BsDataGrid<Row>(Data: _empty ? [] : Rows, Columns: Columns(), Empty: Div["none"])
        ];
    }
}
