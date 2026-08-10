using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Testing;

namespace Rask.Bootstrap.Tests;

// The group panel. The point of these is keyboard parity: every drag gesture has a real <button> doing the
// same thing, so the whole feature works with no pointer at all. Drag is the accelerator, not the feature.
public partial class BsDataGridGroupPanelTests : global::Rask.Core.RaskMarkup
{
    private sealed record Row(string Name, string Region, string Rep, int Amount);

    private static readonly List<Row> Rows =
    [
        new("Northwind", "EMEA", "Ana", 12),
        new("Contoso", "AMER", "Bo", 4),
        new("Fabrikam", "EMEA", "Ana", 31),
        new("Tailspin", "APAC", "Cy", 2),
    ];

    private static BsColumn<Row>[] Columns() =>
    [
        new BsColumn<Row> { Title = "Account", Value = r => r.Name, Field = r => r.Name, Sortable = true },
        new BsColumn<Row> { Title = "Region", Value = r => r.Region, Field = r => r.Region, Groupable = true },
        new BsColumn<Row> { Title = "Rep", Value = r => r.Rep, Field = r => r.Rep, Groupable = true },
    ];

    private static string[] Clicks(string html) =>
        Regex.Matches(html, "data-rask-on-click=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();

    // The aria-label of every button, in document order — the keyboard user's view of the panel.
    private static string[] Labels(string html) =>
        Regex.Matches(html, "aria-label=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();

    [Fact]
    public void WithoutTheePanel_NoPanelAndNoGroupControls()
    {
        var html = BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name).ToHtml();

        Assert.DoesNotContain("bs-grid-grouppanel", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Group by", html, StringComparison.Ordinal);
        Assert.DoesNotContain("draggable", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePanel_PromptsWhenEmpty()
    {
        var html = BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name, GroupPanel: true).ToHtml();

        Assert.Contains("bs-grid-grouppanel", html, StringComparison.Ordinal);
        Assert.Contains("Drag a column here to group by it", html, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryGroupableHeader_OffersAGroupByButton_AndIsADragSource()
    {
        var html = BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name, GroupPanel: true).ToHtml();

        // The keyboard route in. Account is not Groupable, so it offers nothing and is not draggable.
        Assert.Contains("aria-label=\"Group by Region\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Group by Rep\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Group by Account", html, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(html, "<th[^>]*draggable=\"true\"").Count);
    }

    [Fact]
    public async Task TheHeaderButton_GroupsAndUngroups()
    {
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            GroupPanel: true));

        // Group by Region — no pointer involved.
        var byRegion = Clicks(grid.Html)[1]; // [0] is Account's sort button
        var html = await grid.InvokeAsync(byRegion);

        Assert.Contains("Grouped by", html, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(html, "table-group-divider").Count); // AMER, APAC, EMEA
        // Region's header (and with it the aria-pressed group-by toggle) folds away once it's grouped — its
        // value lives in the band header now, so the panel chip is what carries the grouped state and ungroup.
        Assert.DoesNotContain("aria-label=\"Group by Region\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Stop grouping by Region\"", html, StringComparison.Ordinal);

        // The chip ungroups. Read it by label, not index: once grouped, the panel's chip buttons
        // precede the headers in document order and every index shifts.
        html = await grid.InvokeAsync(ClickFor(html, "Stop grouping by Region"));
        Assert.DoesNotContain("table-group-divider", html, StringComparison.Ordinal);
        Assert.Contains("Drag a column here to group by it", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheChip_OffersUngroupAndReorder_AsRealButtons()
    {
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            GroupPanel: true, Grouped: ["region", "rep"], OnGroupedChange: _ => { }));

        // Every drag gesture has a button equivalent: this is the parity claim, asserted directly.
        var labels = Labels(grid.Html);
        Assert.Contains("Move Region out one level", labels);
        Assert.Contains("Move Region in one level", labels);
        Assert.Contains("Stop grouping by Region", labels);
        Assert.Contains("Move Rep out one level", labels);
        Assert.Contains("Stop grouping by Rep", labels);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task ReorderingAChip_RenestsTheGrouping()
    {
        var reported = new List<IReadOnlyList<string>>();
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            GroupPanel: true, Grouped: ["region", "rep"], OnGroupedChange: reported.Add));

        // "Move Rep out one level" — nesting order IS the report's meaning, so it must be keyboard-reachable.
        var html = grid.Html;
        var moveRepOut = ClickFor(html, "Move Rep out one level");
        await grid.InvokeAsync(moveRepOut);

        Assert.Equal(["rep", "region"], reported[^1]);
    }

    [Fact]
    public async Task TheChipsEdges_CannotMovePastTheEnds()
    {
        var reported = new List<IReadOnlyList<string>>();
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            GroupPanel: true, Grouped: ["region", "rep"], OnGroupedChange: reported.Add));

        // The outermost level's "out" and the innermost's "in" are disabled rather than no-ops that look live.
        Assert.Matches("aria-label=\"Move Region out one level\"[^>]*disabled", grid.Html);
        Assert.Matches("aria-label=\"Move Rep in one level\"[^>]*disabled", grid.Html);
        // ...and the reachable ones are not.
        Assert.DoesNotMatch("aria-label=\"Move Region in one level\"[^>]*disabled", grid.Html);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task RemovingAChip_Ungroups()
    {
        var reported = new List<IReadOnlyList<string>>();
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            GroupPanel: true, Grouped: ["region", "rep"], OnGroupedChange: reported.Add));

        await grid.InvokeAsync(ClickFor(grid.Html, "Stop grouping by Region"));

        Assert.Equal(["rep"], reported[^1]);
    }

    [Fact]
    public void TheChipsAreDraggable_AndThePanelIsADropTarget()
    {
        // Drag is the accelerator over the buttons above. The panel needs an ondragover handler or the browser
        // rejects the drop outright — the client turns that into the preventDefault that marks a valid target.
        var html = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            GroupPanel: true, Grouped: ["region"], OnGroupedChange: _ => { })).Html;

        Assert.Contains("<div class=\"bs-grid-grouppanel", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-on-dragover", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-on-drop", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-on-dragstart", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"bs-grid-chip", html, StringComparison.Ordinal);
    }

    // The drag LOGIC lives here rather than in the browser walk on purpose. Rask uses native HTML5 drag-and-
    // drop, and Playwright's DragTo synthesises mouse move/down/up, which the browser does not turn into
    // dragstart/drop — so an E2E "drag" would test a hand-built DataTransfer rather than the feature. The
    // handlers are ordinary registered handlers, so dispatching them directly exercises the real path; the
    // browser walk asserts the wiring is present and proves the keyboard route end-to-end.
    private static string Handler(string html, string kind, int index = 0) =>
        Regex.Matches(html, $"data-rask-on-{kind}=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ElementAt(index);

    [Fact]
    public async Task DraggingAHeaderOntoThePanel_GroupsByIt()
    {
        var reported = new List<IReadOnlyList<string>>();
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            GroupPanel: true, Grouped: [], OnGroupedChange: reported.Add));

        // dragstart on the Rep header, then drop on the panel — the gesture the panel's hint text describes.
        var repDragStart = Handler(grid.Html, "dragstart", 1); // [0] is Region's header
        await grid.InvokeAsync(repDragStart);
        await grid.InvokeAsync(Handler(grid.Html, "drop"));    // [0] is the panel itself

        Assert.Equal(["rep"], reported[^1]);
    }

    [Fact]
    public async Task DroppingOnAChip_InsertsBeforeIt()
    {
        var reported = new List<IReadOnlyList<string>>();
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            GroupPanel: true, Grouped: ["region", "rep"], OnGroupedChange: reported.Add));

        // Drag the Rep chip onto the Region chip: dropping ON a chip inserts before it, so rep leads.
        // Chips render before the headers, so the chip dragstarts are [0] (region) and [1] (rep).
        await grid.InvokeAsync(Handler(grid.Html, "dragstart", 1));
        await grid.InvokeAsync(Handler(grid.Html, "drop", 1)); // [0] panel, [1] region chip

        Assert.Equal(["rep", "region"], reported[^1]);
    }

    [Fact]
    public async Task ADragThatStartedNowhere_DoesNothing()
    {
        // A drop with no dragstart before it (a stray drop from outside) must not rearrange anything.
        var reported = new List<IReadOnlyList<string>>();
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            GroupPanel: true, Grouped: ["region"], OnGroupedChange: reported.Add));

        await grid.InvokeAsync(Handler(grid.Html, "drop"));

        Assert.Empty(reported);
    }

    [Fact]
    public async Task DraggingAChipOut_Ungroups()
    {
        // The accelerator over the chip's × button: drag it out of the panel and release on nothing. dragend
        // fires with no drop before it, so _dragField is still set — the "drag out to ungroup" gesture.
        var reported = new List<IReadOnlyList<string>>();
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            GroupPanel: true, Grouped: ["region"], OnGroupedChange: reported.Add));

        await grid.InvokeAsync(Handler(grid.Html, "dragstart")); // [0] the region chip
        await grid.InvokeAsync(Handler(grid.Html, "dragend"));   // released on nothing

        Assert.Equal([], reported[^1]);
    }

    [Fact]
    public async Task ADropThenDragEnd_DoesNotAlsoUngroup()
    {
        // A completed drop consumes the drag, so the dragend that follows it must be a no-op — otherwise every
        // successful reorder would immediately ungroup the level it just moved.
        var reported = new List<IReadOnlyList<string>>();
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            GroupPanel: true, Grouped: ["region", "rep"], OnGroupedChange: reported.Add));

        await grid.InvokeAsync(Handler(grid.Html, "dragstart", 1)); // rep chip
        await grid.InvokeAsync(Handler(grid.Html, "drop", 1));      // onto the region chip → [rep, region]
        Assert.Equal(["rep", "region"], reported[^1]);

        var count = reported.Count;
        await grid.InvokeAsync(Handler(grid.Html, "dragend"));      // the drag is already spent — no-op
        Assert.Equal(count, reported.Count);
    }

    [Fact]
    public void ThePanelSurvivesTheEmptyState()
    {
        // Grouping down to nothing must not strand the user: the panel is how they got here and how they leave.
        var html = BsDataGrid<Row>(Data: [], Columns: Columns(), RowKey: r => r.Name, GroupPanel: true,
            Grouped: ["region"], OnGroupedChange: _ => { }, Empty: Div.Id("none")["No rows."]).ToHtml();

        Assert.Contains("bs-grid-grouppanel", html, StringComparison.Ordinal);
        Assert.Contains("No rows.", html, StringComparison.Ordinal);
    }

    // The handler id of the button carrying this aria-label. Ids are reissued every render, so it is read from
    // the current markup rather than captured.
    private static string ClickFor(string html, string label)
    {
        var m = Regex.Match(html,
            "data-rask-on-click=\"([^\"]+)\"[^>]*aria-label=\"" + Regex.Escape(label) + "\"");
        if (!m.Success)
        {
            m = Regex.Match(html,
                "aria-label=\"" + Regex.Escape(label) + "\"[^>]*data-rask-on-click=\"([^\"]+)\"");
        }

        Assert.True(m.Success, $"no button labelled '{label}' in:\n{html}");
        return m.Groups[1].Value;
    }
}
