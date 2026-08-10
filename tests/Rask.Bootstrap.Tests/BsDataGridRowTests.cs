using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Testing;

namespace Rask.Bootstrap.Tests;

// Row-level presentation and interaction for BsDataGrid<T>: RowClass, OnRowClick (and the RowClickable rule
// that keeps interactive template cells alive), StickyHeader and MaxHeight.
public partial class BsDataGridRowTests : global::Rask.Core.RaskMarkup
{
    private sealed record Row(string Name, int Qty);

    private static readonly List<Row> Rows = [new("Banana", 3), new("Apple", 5)];

    private static BsColumn<Row>[] Columns() =>
    [
        new BsColumn<Row> { Title = "Name", Value = r => r.Name },
        new BsColumn<Row> { Title = "Qty", Value = r => r.Qty },
    ];

    // Handler ids are reissued every render, so read them fresh in document order and index the one under
    // test — the same approach BsDataGridInteractionTests uses.
    private static string[] ClickHandlers(string html) =>
        Regex.Matches(html, "data-rask-on-click=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();

    [Fact]
    public void RowClass_StylesRowsConditionally()
    {
        var html = BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            RowClass: r => r.Qty < 5 ? "table-warning" : null).ToHtml();

        Assert.Contains("<tr class=\"table-warning\" data-rask-key=\"Banana\">", html, StringComparison.Ordinal);
        // Apple (Qty 5) returns null, which must render no class attribute at all rather than an empty one.
        Assert.Contains("<tr data-rask-key=\"Apple\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RowClass_DoesNotReachTheMasterDetailRow()
    {
        // RowClass styles the DATA rows. The full-width detail row is structural, not a data row, so it must
        // not inherit the class — a table-warning band bleeding across the detail's colspan would be wrong.
        var grid = RaskTest.Render(BsDataGrid(Id: "g", Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            RowClass: _ => "table-warning",
            ExpandedContent: r => Div[$"detail-{r.Name}"]));

        // Non-sortable columns, no row click: the only click handlers are the per-row expanders; [0] = Banana.
        var html = await grid.InvokeAsync(ClickHandlers(grid.Html)[0]);

        // The data row carries the class; the detail row it opened does not.
        Assert.Contains("<tr class=\"table-warning\" data-rask-key=\"Banana\">", html, StringComparison.Ordinal);
        Assert.Contains("<tr id=\"g-detail-0\" data-rask-key=\"Banana:detail\">", html, StringComparison.Ordinal);
        // Both data rows are styled; the open detail row is not — so the class count equals the data-row count.
        Assert.Equal(2, Regex.Matches(html, "table-warning").Count);
    }

    [Fact]
    public void WithoutARowClick_NoCellCarriesAHandler()
    {
        // The feature is opt-in all the way down: a grid that doesn't use it pays nothing and its markup is
        // unchanged. This is the baseline the RowClickable rule below is measured against. Handlers are only
        // emitted by a live render, so this (and every handler assertion here) goes through RaskTest.
        var html = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name)).Html;

        Assert.DoesNotContain("data-rask-on-click", html, StringComparison.Ordinal);
        Assert.DoesNotContain("bs-grid-click", html, StringComparison.Ordinal);
    }

    [Fact]
    public void OnRowClick_LandsOnTheCells_NotTheRow()
    {
        // The handler must never sit on the <tr>: the client cancels the default action of every click it
        // dispatches, so a row-level handler would sit above the expander and the selection checkbox and
        // silently kill both.
        var html = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            OnRowClick: _ => { })).Html;

        Assert.DoesNotMatch("<tr[^>]*data-rask-on-click", html);
        Assert.Contains("<td class=\"bs-grid-click\" data-rask-on-click=", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnRowClick_FiresWithTheClickedRow()
    {
        Row? clicked = null;
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            OnRowClick: r => clicked = r));

        // Two rows x two clickable cells, in document order: [0]=Banana/Name, [1]=Banana/Qty,
        // [2]=Apple/Name. Clicking any cell of a row reports that row, so [2] must report Apple.
        await grid.InvokeAsync(ClickHandlers(grid.Html)[2]);

        Assert.Equal(Rows[1], clicked);
    }

    [Fact]
    public void TemplateColumns_AreNotRowClickable_ByDefault()
    {
        // The load-bearing one. A Template cell is where an author puts a link or a button, and the client's
        // unconditional preventDefault would leave it inert with no error. So it opts out unless asked.
        var columns = new[]
        {
            new BsColumn<Row> { Title = "Name", Value = r => r.Name },
            new BsColumn<Row> { Title = "Open", Template = r => A.Href("/x")[r.Name] },
        };

        var html = RaskTest.Render(BsDataGrid(Data: Rows, Columns: columns, RowKey: r => r.Name,
            OnRowClick: _ => { })).Html;

        // The Value cell is clickable; the Template cell holding the link is not, so the link still navigates
        // — no handler anywhere above the <a>, which is the whole point.
        Assert.Contains("<td class=\"bs-grid-click\" data-rask-on-click=", html, StringComparison.Ordinal);
        Assert.Contains("<td><a href=\"/x\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RowClickable_OverridesTheDefault_BothWays()
    {
        var columns = new[]
        {
            // A Value column carved out of the row click (e.g. it holds the row's own action).
            new BsColumn<Row> { Title = "Name", Value = r => r.Name, RowClickable = false },
            // A non-interactive template (a badge) opted back in.
            new BsColumn<Row> { Title = "Qty", Template = r => BsBadge[r.Qty.ToString()], RowClickable = true },
        };

        var html = RaskTest.Render(BsDataGrid(Data: Rows, Columns: columns, RowKey: r => r.Name,
            OnRowClick: _ => { })).Html;

        // The opted-out Value column renders a plain cell, and the opted-in badge template a clickable one.
        Assert.Contains("<td>Banana</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td class=\"bs-grid-click\" data-rask-on-click=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void OnRowClick_DoesNotReachTheExpanderCell()
    {
        // The expander's own button must keep working: it is a leading cell and never gets the row handler,
        // so nothing sits above the button to cancel its click.
        var html = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            OnRowClick: _ => { }, ExpandedContent: r => Div[r.Name])).Html;

        Assert.DoesNotMatch("<td[^>]*data-rask-on-click[^>]*><button", html);
        Assert.Contains("<td><button", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnRowClickAsync_IsAwaited()
    {
        var clicked = 0;
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            OnRowClickAsync: _ =>
            {
                clicked++;
                return Task.CompletedTask;
            }));

        await grid.InvokeAsync(ClickHandlers(grid.Html)[0]);

        Assert.Equal(1, clicked);
    }

    [Fact]
    public void StickyHeaderAndMaxHeight_ForwardToTheTable()
    {
        var html = BsDataGrid(Data: Rows, Columns: Columns(), StickyHeader: true, MaxHeight: "300px").ToHtml();

        Assert.Contains("style=\"max-height:300px\"", html, StringComparison.Ordinal);
        Assert.Contains("bs-table-sticky", html, StringComparison.Ordinal);
    }

    [Fact]
    public void MaxHeight_BoundsTheTableOnly_LeavingThePagerOutside()
    {
        // The pager must stay reachable rather than scroll away inside the box with the rows.
        var html = BsDataGrid(Data: Rows, Columns: Columns(), PageSize: 1, MaxHeight: "300px").ToHtml();

        var wrapperEnd = html.IndexOf("</table></div>", StringComparison.Ordinal);
        var pager = html.IndexOf("pagination", StringComparison.Ordinal);

        Assert.True(wrapperEnd > 0 && pager > wrapperEnd, "the pager must render after the scroll container");
    }
}
