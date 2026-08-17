using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Testing;

namespace Rask.Bootstrap.Tests;

// The column chooser (show/hide) and reordering. Like the group panel, the point is keyboard parity: every
// drag gesture has a real <button> or checkbox doing the same thing, and the whole feature funnels through the
// one VisibleColumns list so hide/reorder compose with grouping, sort and footers for free.
public partial class BsDataGridColumnsTests : global::Rask.Core.RaskMarkup
{
    private sealed record Row(string Name, string Region, int Amount);

    // Names chosen so a sort by Account and a sort by Amount disagree on the first row (Ant vs Yak), which is
    // what makes the sort-after-reorder test actually discriminate.
    private static readonly List<Row> Rows =
    [
        new("Zebra", "EMEA", 12),
        new("Yak", "AMER", 4),
        new("Ant", "EMEA", 31),
    ];

    private static BsColumn<Row>[] Columns() =>
    [
        new BsColumn<Row> { Title = "Account", Value = r => r.Name, Field = r => r.Name, Sortable = true },
        new BsColumn<Row>
        {
            Title = "Region", Value = r => r.Region, Field = r => r.Region, Sortable = true, Groupable = true,
        },
        new BsColumn<Row>
        {
            Title = "Amount", Value = r => r.Amount, Field = r => r.Amount, Sortable = true,
            Footer = rows => rows.Sum(x => x.Amount),
        },
    ];

    // `new`: the <thead> tag entry arrives with the markup host and this helper hides it (CS0108). The
    // attribute form does not avoid that — an attributed type with a free base slot is given `: RaskMarkup`
    // in its generated partial, so the entry is inherited either way.
    private static string Thead(string html) =>
        Regex.Match(html, "<thead>.*?</thead>", RegexOptions.Singleline).Value;

    private static string FirstBodyRow(string html)
    {
        var body = Regex.Match(html, "<tbody>(.*)</tbody>", RegexOptions.Singleline).Groups[1].Value;
        var row = Regex.Match(body, "<tr.*?</tr>", RegexOptions.Singleline).Value;
        // Drop the opening <tr ...> tag: its Key rides there and would collide with a cell value under IndexOf.
        return row[(row.IndexOf('>') + 1)..];
    }

    // <th> cells only — "<th" alone also matches the "<thead>" wrapper.
    private static int HeaderCount(string thead) => Regex.Matches(thead, "<th[ >]").Count;

    private static string[] Clicks(string html) =>
        Regex.Matches(html, "data-rask-on-click=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();

    private static string[] Labels(string html) =>
        Regex.Matches(html, "aria-label=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();

    private static string Action(string html, string kind, int index = 0) =>
        Regex.Matches(html, $"data-rask-on-{kind}=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ElementAt(index);

    // The handler id of the button carrying this aria-label. Read from the current markup rather than
    // captured: an id only holds while its component keeps rendering that same handler.
    private static string ClickFor(string html, string label)
    {
        var m = Regex.Match(html,
            "data-rask-on-click=\"([^\"]+)\"[^>]*aria-label=\"" + Regex.Escape(label) + "\"");
        Assert.True(m.Success, $"no clickable labelled '{label}' in:\n{html}");
        return m.Groups[1].Value;
    }

    // The change handler id of the checkbox carrying this aria-label (the label precedes the handler on the box).
    private static string ChangeFor(string html, string label)
    {
        var m = Regex.Match(html,
            "aria-label=\"" + Regex.Escape(label) + "\"[^>]*data-rask-on-change=\"([^\"]+)\"");
        Assert.True(m.Success, $"no checkbox labelled '{label}' in:\n{html}");
        return m.Groups[1].Value;
    }

    // The change payload the client sends for a checkbox — its real checked state.
    private static string Checked(bool on) => $"{{\"value\":\"{(on ? "true" : "false")}\"}}";

    // --- unused = free -------------------------------------------------------------------------------------

    [Fact]
    public void WithoutTheChooser_NoMenuNoDraggable()
    {
        var html = BsDataGrid.Data(Rows).Columns(Columns()).RowKey(r => r.Name).ToHtml();

        Assert.DoesNotContain("bs-grid-columnchooser", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=\"Columns\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("draggable", html, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyControlledLists_WithNoChooser_RenderByteIdenticalMarkup()
    {
        // Opting into controlled visibility with nothing hidden, and no menu, must fold to the exact markup of
        // a plain grid — the VisibleColumns fast path returns the same column reference and no toolbar renders.
        // (Controlled ColumnOrder is deliberately excluded here: wiring order in enables header-drag reorder,
        // which is a real difference — see EveryHeader_IsADragSourceAndDropTarget.)
        var plain = BsDataGrid.Data(Rows).Columns(Columns()).RowKey(r => r.Name).ToHtml();
        var opted = BsDataGrid.Data(Rows).Columns(Columns()).RowKey(r => r.Name).HiddenColumns([]).ToHtml();

        Assert.Equal(plain, opted);
    }

    // --- the menu ------------------------------------------------------------------------------------------

    [Fact]
    public void TheChooser_RendersAToggleButtonAndAClosedMenu()
    {
        var html = BsDataGrid.Data(Rows).Columns(Columns()).RowKey(r => r.Name).ColumnChooser(true).ToHtml();

        Assert.Contains("bs-grid-columnchooser", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Columns\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("bs-grid-columnmenu", html, StringComparison.Ordinal); // closed by default
    }

    [Fact]
    public async Task OpeningTheMenu_ListsACheckboxPerNamedColumn()
    {
        var grid = RaskTest.Render(BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .ColumnChooser(true));

        var html = await grid.InvokeAsync(ClickFor(grid.Html, "Columns"));

        Assert.Contains("bs-grid-columnmenu", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Show Account\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Show Region\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Show Amount\"", html, StringComparison.Ordinal);
    }

    // --- hide ----------------------------------------------------------------------------------------------

    [Fact]
    public void HidingAColumn_FoldsHeaderCellsAndFooter()
    {
        var html = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .ColumnChooser(true)
            .HiddenColumns(["region"]).ToHtml();

        var thead = Thead(html);
        Assert.Equal(2, HeaderCount(thead));     // Account + Amount, Region folded out
        Assert.DoesNotContain("Region", thead, StringComparison.Ordinal); // menu is closed, so no leak
        Assert.DoesNotContain("EMEA", html, StringComparison.Ordinal);    // the region cells go with the header
        Assert.Contains("<tfoot>", html, StringComparison.Ordinal);       // Amount still totals
    }

    [Fact]
    public void HidingAColumn_ShrinksTheBandColspan()
    {
        // Grouped by region (folded away) — the band header spans the visible data columns. Hiding Amount too
        // takes the colspan from 2 (Account + Amount) down to 1 (Account alone).
        var kept = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["region"])
            .ColumnChooser(true).ToHtml();
        Assert.Contains("colspan=\"2\"", kept, StringComparison.Ordinal);

        var hidden = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .Grouped(["region"])
            .ColumnChooser(true)
            .HiddenColumns(["amount"]).ToHtml();
        Assert.Contains("colspan=\"1\"", hidden, StringComparison.Ordinal);
        Assert.DoesNotContain("colspan=\"2\"", hidden, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownHideToken_IsIgnored()
    {
        var html = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .ColumnChooser(true)
            .HiddenColumns(["deleteMe"]).ToHtml();

        Assert.Equal(3, HeaderCount(Thead(html))); // nothing hidden
    }

    [Fact]
    public void HidingEveryColumn_IsRefusedWholesale()
    {
        // A stale set that resolves to "hide everything" is dropped entirely rather than rendering a bodyless
        // table, the same tolerance a stale ?group=deleteMe gets.
        var html = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .ColumnChooser(true)
            .HiddenColumns(["name", "region", "amount"]).ToHtml();

        Assert.Equal(3, HeaderCount(Thead(html)));
    }

    [Fact]
    public async Task TheLastVisibleColumnsCheckbox_IsDisabled()
    {
        // With two of three already hidden, unchecking the last would empty the table — so its box is locked on.
        var grid = RaskTest.Render(BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .ColumnChooser(true)
            .HiddenColumns(["region", "amount"])
            .OnHiddenColumnsChange(_ => { }));

        var html = await grid.InvokeAsync(ClickFor(grid.Html, "Columns"));

        Assert.Matches("aria-label=\"Show Account\"[^>]*disabled", html);
        Assert.DoesNotMatch("aria-label=\"Show Region\"[^>]*disabled", html); // hidden ones can always re-show
    }

    [Fact]
    public async Task TheCheckbox_TogglesVisibility_Uncontrolled()
    {
        var grid = RaskTest.Render(BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .ColumnChooser(true));

        var open = await grid.InvokeAsync(ClickFor(grid.Html, "Columns"));
        Assert.Equal(3, HeaderCount(Thead(open)));

        // Uncheck Region — the grid owns the visibility and re-renders itself.
        var hidden = await grid.InvokeAsync(ChangeFor(open, "Show Region"), Checked(false));
        Assert.Equal(2, HeaderCount(Thead(hidden)));
        Assert.DoesNotContain("EMEA", hidden, StringComparison.Ordinal);
    }

    // --- reorder -------------------------------------------------------------------------------------------

    [Fact]
    public void Reordering_PermutesHeaderBodyAndFooter()
    {
        var html = BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .ColumnChooser(true)
            .ColumnOrder(["amount", "name", "region"]).ToHtml();

        var thead = Thead(html);
        Assert.True(thead.IndexOf("Amount", StringComparison.Ordinal)
            < thead.IndexOf("Account", StringComparison.Ordinal));
        Assert.True(thead.IndexOf("Account", StringComparison.Ordinal)
            < thead.IndexOf("Region", StringComparison.Ordinal));

        // First body row (Zebra/EMEA/12) now reads amount, name, region.
        var first = FirstBodyRow(html);
        Assert.True(first.IndexOf("12", StringComparison.Ordinal) < first.IndexOf("Zebra", StringComparison.Ordinal));
        Assert.True(first.IndexOf("Zebra", StringComparison.Ordinal) < first.IndexOf("EMEA", StringComparison.Ordinal));

        // Only Amount has a footer; reordered first, it leads the <tfoot>.
        Assert.Contains("<tfoot><tr><td>47</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SortStillResolves_AfterAReorder()
    {
        // The load-bearing test: the header renders in the reordered sequence, but sort tracks a column's
        // identity, not its slot. Amount is reordered to the front; clicking it must sort by Amount, not by
        // whatever column now sits at that position.
        var grid = RaskTest.Render(BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .ColumnChooser(true)
            .ColumnOrder(["amount", "name", "region"])
            .OnColumnOrderChange(_ => { }));

        // Clicks: [0] the Columns toggle, then the header sort buttons in render order — [1] Amount (first now).
        var sorted = await grid.InvokeAsync(Clicks(grid.Html)[1]);

        var first = FirstBodyRow(sorted);
        Assert.Contains("Yak", first, StringComparison.Ordinal);  // amount 4, the minimum
        Assert.DoesNotContain("Ant", first, StringComparison.Ordinal); // a name-sort would have led with Ant
    }

    [Fact]
    public async Task TheMoveButtons_ReorderAndAreDisabledAtTheEnds()
    {
        var reported = new List<IReadOnlyList<string>>();
        var grid = RaskTest.Render(BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .ColumnChooser(true)
            .ColumnOrder(["name", "region", "amount"])
            .OnColumnOrderChange(reported.Add));

        var html = await grid.InvokeAsync(ClickFor(grid.Html, "Columns"));

        // Parity: every reorder has a button. The ends are disabled rather than live-looking no-ops.
        var labels = Labels(html);
        Assert.Contains("Move Account earlier", labels);
        Assert.Contains("Move Amount later", labels);
        Assert.Matches("aria-label=\"Move Account earlier\"[^>]*disabled", html);
        Assert.Matches("aria-label=\"Move Amount later\"[^>]*disabled", html);
        Assert.DoesNotMatch("aria-label=\"Move Account later\"[^>]*disabled", html);

        // "Move Amount earlier" swaps Amount and Region.
        await grid.InvokeAsync(ClickFor(html, "Move Amount earlier"));
        Assert.Equal(["name", "amount", "region"], reported[^1]);
    }

    [Fact]
    public void EveryHeader_IsADragSourceAndDropTarget_WhenReorderEnabled()
    {
        // Live render, not ToHtml: handler ids (data-rask-on-*) are only registered in a live context.
        var html = RaskTest.Render(BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .ColumnChooser(true)).Html;

        // All three headers reorder by drag (accelerator over the menu buttons).
        Assert.Equal(3, Regex.Matches(html, "<th[^>]*draggable=\"true\"").Count);
        Assert.Contains("data-rask-on-dragstart", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-on-drop", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DroppingAHeaderOnAnother_Reorders()
    {
        // Drag LOGIC is unit-tested (Playwright can't synthesise native HTML5 drag). Drag Amount onto Account:
        // dropping ON a header inserts before it, so amount leads.
        var reported = new List<IReadOnlyList<string>>();
        var grid = RaskTest.Render(BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .ColumnChooser(true)
            .ColumnOrder(["name", "region", "amount"])
            .OnColumnOrderChange(reported.Add));

        // dragstart handlers are the headers in render order: [0] Account, [1] Region, [2] Amount.
        await grid.InvokeAsync(Action(grid.Html, "dragstart", 2)); // Amount
        await grid.InvokeAsync(Action(grid.Html, "drop", 0));      // onto Account

        Assert.Equal(["amount", "name", "region"], reported[^1]);
    }

    [Fact]
    public async Task ADropWithNoDragStart_DoesNothing()
    {
        var reported = new List<IReadOnlyList<string>>();
        var grid = RaskTest.Render(BsDataGrid.Data(Rows)
            .Columns(Columns())
            .RowKey(r => r.Name)
            .ColumnChooser(true)
            .ColumnOrder(["name", "region", "amount"])
            .OnColumnOrderChange(reported.Add));

        await grid.InvokeAsync(Action(grid.Html, "drop", 0));

        Assert.Empty(reported);
    }

    // --- fixtures (opt-outs) -------------------------------------------------------------------------------

    [Fact]
    public void ANonReorderableColumn_StaysAtItsDeclaredSlot()
    {
        BsColumn<Row>[] cols =
        [
            new() { Title = "Account", Value = r => r.Name, Field = r => r.Name, Reorderable = false },
            new() { Title = "Region", Value = r => r.Region, Field = r => r.Region },
            new() { Title = "Amount", Value = r => r.Amount, Field = r => r.Amount },
        ];

        // The order asks for region first, but Account is a fixture — it holds slot 0 and the movable columns
        // flow around it.
        var html = BsDataGrid.Data(Rows)
            .Columns(cols)
            .RowKey(r => r.Name)
            .ColumnChooser(true)
            .ColumnOrder(["region", "name", "amount"]).ToHtml();

        var thead = Thead(html);
        Assert.True(thead.IndexOf("Account", StringComparison.Ordinal)
            < thead.IndexOf("Region", StringComparison.Ordinal));
    }

    [Fact]
    public void ANonHideableColumn_HasNoWorkingHideToggleAndIgnoresTheToken()
    {
        BsColumn<Row>[] cols =
        [
            new() { Title = "Account", Value = r => r.Name, Field = r => r.Name },
            new() { Title = "Amount", Value = r => r.Amount, Field = r => r.Amount, Hideable = false },
        ];

        // A hide token can't drop a pinned column.
        var html = BsDataGrid.Data(Rows)
            .Columns(cols)
            .RowKey(r => r.Name)
            .ColumnChooser(true)
            .HiddenColumns(["amount"]).ToHtml();
        Assert.Contains("Amount", Thead(html), StringComparison.Ordinal);
    }
}
