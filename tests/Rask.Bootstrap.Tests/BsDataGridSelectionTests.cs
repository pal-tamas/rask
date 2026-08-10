using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Testing;

namespace Rask.Bootstrap.Tests;

// Row selection: the checkbox column, select-all-this-page, and the controlled/uncontrolled split. Several of
// these guard failures that are invisible in a screenshot — a vacuously-checked select-all over an empty page,
// a <tfoot> off by one, a selection that follows positions instead of rows.
public partial class BsDataGridSelectionTests : global::Rask.Core.RaskMarkup
{
    private sealed record Row(string Name, int Qty);

    private static readonly List<Row> Rows = [new("Banana", 3), new("Apple", 5), new("Cherry", 1)];

    private static BsColumn<Row>[] Columns() =>
    [
        new BsColumn<Row> { Title = "Name", Value = r => r.Name, Sortable = true },
        new BsColumn<Row> { Title = "Qty", Value = r => r.Qty },
    ];

    private static string[] ChangeHandlers(string html) =>
        Regex.Matches(html, "data-rask-on-change=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();

    // The client reports a checkbox's real `checked` state as "true"/"false" (rask.js), not a "toggle" signal —
    // the server side is meant to be self-correcting. These mirror that exactly, so the tests exercise the same
    // contract the browser does.
    private static string Checked(bool on) => $"{{\"value\":\"{(on ? "true" : "false")}\"}}";

    [Fact]
    public void WithoutSelection_NoCheckboxColumn()
    {
        var html = BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name).ToHtml();

        Assert.DoesNotContain("bs-grid-check", html, StringComparison.Ordinal);
        Assert.DoesNotContain("form-check-input", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Selectable_AddsALeadingCheckboxColumn()
    {
        var html = BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name, Selectable: true).ToHtml();

        // The checkbox column leads, before the first data header.
        Assert.Contains("<thead><tr><th class=\"bs-grid-check\" scope=\"col\">", html, StringComparison.Ordinal);
        Assert.Equal(4, Regex.Matches(html, "form-check-input").Count); // 1 select-all + 3 rows
    }

    [Fact]
    public void EachOfTheFourEntryPoints_EnablesSelection()
    {
        // SelectedKeys/OnSelectionChange imply it, so a controlled grid doesn't have to say Selectable too.
        Assert.Contains("bs-grid-check",
            BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name, Selectable: true).ToHtml(),
            StringComparison.Ordinal);
        Assert.Contains("bs-grid-check",
            BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name, SelectedKeys: []).ToHtml(),
            StringComparison.Ordinal);
        Assert.Contains("bs-grid-check",
            BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name, OnSelectionChange: _ => { }).ToHtml(),
            StringComparison.Ordinal);
        Assert.Contains("bs-grid-check",
            BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
                OnSelectionChangeAsync: _ => Task.CompletedTask).ToHtml(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheRowCheckbox_IsNamedAfterItsRow()
    {
        // Twenty identical "Select row" labels read as one control repeated. The name comes from the first
        // Value column.
        var html = BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name, Selectable: true).ToHtml();

        Assert.Contains("aria-label=\"Select Banana\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Select Apple\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRowCheckbox_FallsBackWhenThereIsNoTextToBorrow()
    {
        BsColumn<Row>[] templatesOnly = [new BsColumn<Row> { Title = "N", Template = r => Span[r.Name] }];

        var html = BsDataGrid(Data: Rows, Columns: templatesOnly, RowKey: r => r.Name, Selectable: true).ToHtml();

        Assert.Contains("aria-label=\"Select row\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSelectAllCheckbox_SaysItOnlyCoversThisPage()
    {
        // It can only reach the rows the grid holds, and next to a pager "Select all" would be a lie.
        var html = BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name, Selectable: true, PageSize: 2)
            .ToHtml();

        Assert.Contains("aria-label=\"Select all rows on this page\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClickingARowCheckbox_SelectsAndDeselects()
    {
        var reported = new List<IReadOnlyList<object>>();
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            Selectable: true, OnSelectionChange: reported.Add));

        // [0] is select-all, [1..] are the rows.
        var html = await grid.InvokeAsync(ChangeHandlers(grid.Html)[1], Checked(true));
        Assert.Equal(["Banana"], reported[^1]);
        Assert.Contains("<tr class=\"table-active\" data-rask-key=\"Banana\">", html, StringComparison.Ordinal);

        html = await grid.InvokeAsync(ChangeHandlers(html)[1], Checked(false));
        Assert.Empty(reported[^1]);
        Assert.DoesNotContain("table-active", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectAll_CoversThePage_AndClearsWhenFull()
    {
        var reported = new List<IReadOnlyList<object>>();
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            Selectable: true, PageSize: 2, OnSelectionChange: reported.Add));

        // Page 1 holds Banana and Apple — and only those get selected, not Cherry on page 2.
        var html = await grid.InvokeAsync(ChangeHandlers(grid.Html)[0], Checked(true));
        Assert.Equal(["Banana", "Apple"], reported[^1].OrderByDescending(k => (string)k));
        Assert.Equal(2, Regex.Matches(html, "table-active").Count);

        // A full page toggles back off.
        await grid.InvokeAsync(ChangeHandlers(html)[0], Checked(false));
        Assert.Empty(reported[^1]);
    }

    [Fact]
    public async Task Selection_AccumulatesAcrossPages()
    {
        // The point of a bulk action: three on page 1 and two on page 2 is five rows, so paging must not
        // silently drop what is already picked.
        var reported = new List<IReadOnlyList<object>>();
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            Selectable: true, PageSize: 2, OnSelectionChange: reported.Add));

        await grid.InvokeAsync(ChangeHandlers(grid.Html)[1], Checked(true));  // Banana, page 1

        var pager = Regex.Matches(grid.Html, "data-rask-on-click=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToArray();
        var html = await grid.InvokeAsync(pager[^1]);                  // next page

        await grid.InvokeAsync(ChangeHandlers(html)[1], Checked(true));       // Cherry, page 2

        Assert.Equal(["Banana", "Cherry"], reported[^1].Select(k => (string)k).OrderBy(k => k));
    }

    [Fact]
    public void SelectAll_OnAnEmptyPage_IsNotCheckedAndIsDisabled()
    {
        // All() over an empty page is vacuously true, which would render the box checked over nothing.
        var html = BsDataGrid<Row>(Data: [], Columns: Columns(), RowKey: r => r.Name, Selectable: true).ToHtml();

        Assert.DoesNotContain("checked", html, StringComparison.Ordinal);
        Assert.Contains("disabled", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlledSelection_RendersWhatItIsGiven()
    {
        var html = BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            SelectedKeys: ["Apple"], OnSelectionChange: _ => { }).ToHtml();

        Assert.Contains("<tr class=\"table-active\" data-rask-key=\"Apple\">", html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(html, "table-active"));
    }

    [Fact]
    public async Task ControlledSelection_ReportsButDoesNotSelectItself()
    {
        // The caller owns it: the grid must not also mutate its own set, or the two would drift.
        var reported = new List<IReadOnlyList<object>>();
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            SelectedKeys: ["Apple"], OnSelectionChange: reported.Add));

        var html = await grid.InvokeAsync(ChangeHandlers(grid.Html)[1], Checked(true)); // Banana

        Assert.Equal(["Apple", "Banana"], reported[^1].Select(k => (string)k).OrderBy(k => k));
        // Still only Apple: the props did not change, so the render did not either.
        Assert.Single(Regex.Matches(html, "table-active"));
    }

    [Fact]
    public async Task ControlledSelection_ReportsTheWholeSet_NotADelta()
    {
        var reported = new List<IReadOnlyList<object>>();
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            SelectedKeys: ["Apple", "Cherry"], OnSelectionChange: reported.Add));

        await grid.InvokeAsync(ChangeHandlers(grid.Html)[1], Checked(true)); // + Banana

        Assert.Equal(["Apple", "Banana", "Cherry"], reported[^1].Select(k => (string)k).OrderBy(k => k));
    }

    [Fact]
    public async Task DeselectingAControlledRow_ReportsTheRemainder()
    {
        var reported = new List<IReadOnlyList<object>>();
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            SelectedKeys: ["Apple", "Banana"], OnSelectionChange: reported.Add));

        await grid.InvokeAsync(ChangeHandlers(grid.Html)[1], Checked(false)); // Banana, already selected -> off

        Assert.Equal(["Apple"], reported[^1]);
    }

    [Fact]
    public async Task OnSelectionChangeAsync_IsAwaited()
    {
        IReadOnlyList<object>? got = null;
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            Selectable: true, OnSelectionChangeAsync: keys =>
            {
                got = keys;
                return Task.CompletedTask;
            }));

        await grid.InvokeAsync(ChangeHandlers(grid.Html)[1], Checked(true));

        Assert.Equal(["Banana"], got);
    }

    [Fact]
    public async Task SelectionSurvivesASort_BecauseItIsKeyed()
    {
        // Keyed, not indexed: sorting moves the rows but not the selection.
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            Selectable: true));

        var html = await grid.InvokeAsync(ChangeHandlers(grid.Html)[1], Checked(true));
        Assert.Contains("<tr class=\"table-active\" data-rask-key=\"Banana\">", html, StringComparison.Ordinal);

        var sort = Regex.Matches(html, "data-rask-on-click=\"([^\"]+)\"").Select(m => m.Groups[1].Value).First();
        html = await grid.InvokeAsync(sort);

        // Banana is now in the middle, and still the selected one.
        Assert.Contains("<tr class=\"table-active\" data-rask-key=\"Banana\">", html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(html, "table-active"));
    }

    [Fact]
    public async Task WithMasterDetail_BothLeadingColumnsRender_AndTheDetailSpansThemAll()
    {
        // The colspan and the three leading-cell sites are where an extra leading column goes wrong.
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            Selectable: true, ExpandedContent: r => Div[r.Name]));

        // Expand the first row: [0]/[1] are the sortable headers, so the expanders start at [2].
        var expander = Regex.Matches(grid.Html, "data-rask-on-click=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToArray();
        var html = await grid.InvokeAsync(expander[2]);

        // 2 data columns + checkbox + expander = 4.
        Assert.Contains("colspan=\"4\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void WithFooters_TheFooterKeepsALeadingCellPerLeadingColumn()
    {
        // Miss this and every total sits one column to the left.
        BsColumn<Row>[] columns =
        [
            new BsColumn<Row> { Title = "Name", Value = r => r.Name },
            new BsColumn<Row> { Title = "Qty", Value = r => r.Qty, Footer = rows => rows.Sum(r => r.Qty) },
        ];

        var html = BsDataGrid(Data: Rows, Columns: columns, RowKey: r => r.Name, Selectable: true,
            ExpandedContent: r => Div[r.Name]).ToHtml();

        var tfoot = Regex.Match(html, "<tfoot>(.*?)</tfoot>", RegexOptions.Singleline).Groups[1].Value;

        // checkbox + expander + Name + Qty(9)
        Assert.Equal(4, Regex.Matches(tfoot, "<td").Count);
        Assert.EndsWith("<td>9</td></tr>", tfoot, StringComparison.Ordinal);
    }

    [Fact]
    public void WithTotalCount_SelectionWorksOnTheSlice()
    {
        // Server-side mode: the grid holds one page and can only ever name its keys.
        var html = BsDataGrid(Data: Rows.Take(2).ToList(), Columns: Columns(), RowKey: r => r.Name,
            TotalCount: 40, PageSize: 2, Page: 0, Selectable: true, SelectedKeys: ["Banana"]).ToHtml();

        Assert.Contains("<tr class=\"table-active\" data-rask-key=\"Banana\">", html, StringComparison.Ordinal);
        Assert.Contains("1-2 / 40", html, StringComparison.Ordinal);
    }

    [Fact]
    public void WhileLoading_TheCheckboxesAreDisabled()
    {
        // A real `disabled` here, not aria-disabled: unlike the sort/pager controls (which stay focusable so a
        // fetch doesn't throw away the user's keyboard position), a checkbox that cannot be changed should not
        // be reachable at all.
        var html = BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name, Selectable: true,
            Loading: true).ToHtml();

        // Count the checkboxes specifically — the sort button's aria-disabled also contains "disabled".
        Assert.Equal(4, Regex.Matches(html, "<input[^>]*form-check-input[^>]*\\sdisabled").Count);

        // ...and they are enabled again when it clears.
        var idle = BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name, Selectable: true,
            Loading: false).ToHtml();

        Assert.Empty(Regex.Matches(idle, "<input[^>]*form-check-input[^>]*\\sdisabled"));
    }
}
