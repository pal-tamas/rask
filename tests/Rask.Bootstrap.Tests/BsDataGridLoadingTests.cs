using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Testing;

namespace Rask.Bootstrap.Tests;

// BsDataGrid<T>'s busy state. Most of what matters here is invisible to a casual markup check — the nullable
// tri-state, where aria-busy sits relative to the spinner's live region, and the fact that the wrapper does
// not come and go — so each is asserted directly.
public class BsDataGridLoadingTests
{
    private sealed record Row(string Name, int Qty);

    private static readonly List<Row> Rows = [new("Banana", 3), new("Apple", 5), new("Cherry", 1)];

    private static BsColumn<Row>[] Columns() =>
    [
        new BsColumn<Row> { Title = "Name", Value = r => r.Name, Sortable = true },
        new BsColumn<Row> { Title = "Qty", Value = r => r.Qty, Sortable = true },
    ];

    private static string[] ClickHandlers(string html) =>
        Regex.Matches(html, "data-rask-on-click=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();

    [Fact]
    public void LoadingNull_RendersExactlyWhatItAlwaysDid()
    {
        // The reason Loading is nullable rather than a plain bool. Null means "not using the feature", so no
        // wrapper, no overlay, no aria-busy — the markup a pre-existing grid already depends on.
        var html = BsDataGrid(Data: Rows, Columns: Columns()).ToHtml();

        Assert.StartsWith("<div class=\"table-responsive\"><table class=\"table", html, StringComparison.Ordinal);
        Assert.DoesNotContain("position-relative", html, StringComparison.Ordinal);
        Assert.DoesNotContain("bs-grid-overlay", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-busy", html, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadingFalse_RendersTheWrapper_ButNoOverlay()
    {
        // In use but idle. The wrapper is present so that flipping to true adds only the overlay.
        var html = BsDataGrid(Data: Rows, Columns: Columns(), Loading: false).ToHtml();

        Assert.StartsWith("<div class=\"position-relative\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("bs-grid-overlay", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-busy", html, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadingTrue_DimsTheGrid_AndMarksTheTableBusy()
    {
        var html = BsDataGrid(Data: Rows, Columns: Columns(), Loading: true).ToHtml();

        Assert.StartsWith("<div class=\"position-relative\">", html, StringComparison.Ordinal);
        Assert.Contains("aria-busy=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("bs-grid-overlay", html, StringComparison.Ordinal);
        Assert.Contains("spinner-border", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWrapper_IsStableAcrossTheFlip()
    {
        // The differ matches sibling Elements by tag name alone, so a wrapper that appeared only while loading
        // would let the <div> at this slot be paired with — and morphed into — whatever <div> was there
        // before. Keeping it for both states is what preserves the table's identity, and with it focus and
        // scroll, across a refetch.
        var idle = BsDataGrid(Data: Rows, Columns: Columns(), Loading: false).ToHtml();
        var busy = BsDataGrid(Data: Rows, Columns: Columns(), Loading: true).ToHtml();

        Assert.StartsWith("<div class=\"position-relative\">", idle, StringComparison.Ordinal);
        Assert.StartsWith("<div class=\"position-relative\">", busy, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOverlay_IsAppendedLast_SoItIsATailInsert()
    {
        // Wedged between the table and the pager it would be a structural insert the differ has to reconcile
        // against two same-tag <div> siblings; at the tail it is a pure append. position:absolute means the
        // DOM order costs nothing visually.
        var html = BsDataGrid(Data: Rows, Columns: Columns(), PageSize: 2, Loading: true).ToHtml();

        var table = html.IndexOf("<table", StringComparison.Ordinal);
        var pager = html.IndexOf("pagination", StringComparison.Ordinal);
        var overlay = html.IndexOf("bs-grid-overlay", StringComparison.Ordinal);

        Assert.True(table < pager, "the table renders before the pager");
        Assert.True(pager < overlay, "the overlay must be appended after the pager, not between them");
    }

    [Fact]
    public void TheSpinner_IsNotInsideTheAriaBusySubtree()
    {
        // BsSpinner renders role="status", an aria-live region. Inside an aria-busy subtree its announcement
        // is deferred until busy clears — by which point the spinner is gone and the load was never announced.
        // So aria-busy goes on the <table> and the spinner is its sibling, not its child.
        var html = BsDataGrid(Data: Rows, Columns: Columns(), Loading: true).ToHtml();

        var tableEnd = html.IndexOf("</table>", StringComparison.Ordinal);
        var spinner = html.IndexOf("role=\"status\"", StringComparison.Ordinal);

        Assert.Contains("aria-busy=\"true\"", html, StringComparison.Ordinal);
        Assert.True(spinner > tableEnd, "the spinner's live region must sit outside the aria-busy table");
    }

    [Fact]
    public void WhileLoading_TheEmptyStateIsSuppressed()
    {
        // A fetch in flight is not "no results". Without this the first load flashes the placeholder before
        // the rows land.
        var busy = BsDataGrid<Row>(Data: [], Columns: Columns(), Loading: true,
            Empty: Div(Id: "nothing")["Nothing found."]).ToHtml();

        Assert.DoesNotContain("Nothing found.", busy, StringComparison.Ordinal);
        Assert.Contains("bs-grid-overlay", busy, StringComparison.Ordinal);

        // ...and it returns the moment the load finishes with nothing.
        var idle = BsDataGrid<Row>(Data: [], Columns: Columns(), Loading: false,
            Empty: Div(Id: "nothing")["Nothing found."]).ToHtml();

        Assert.Contains("Nothing found.", idle, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEmptyState_IsWrappedToo_SoTheWrapperNeverVanishes()
    {
        // "Loading finished, no results" is the most common transition this feature creates. If the Empty
        // branch returned unwrapped, the wrapper would disappear underneath the differ at exactly that moment.
        var html = BsDataGrid<Row>(Data: [], Columns: Columns(), Loading: false,
            Empty: Div(Id: "nothing")["Nothing found."]).ToHtml();

        Assert.StartsWith("<div class=\"position-relative\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void WhileLoading_TheControlsSayTheyAreDisabled()
    {
        // aria-disabled, not the disabled attribute: disabled would drop focus to <body> mid-fetch.
        var html = BsDataGrid(Data: Rows, Columns: Columns(), PageSize: 2, Loading: true).ToHtml();

        Assert.Contains(
            "<button class=\"btn btn-sm btn-link text-decoration-none p-0 fw-semibold\" aria-disabled=\"true\"",
            html, StringComparison.Ordinal);
        // Every pager item, not only the edges — the pager is where the user just clicked.
        Assert.Empty(Regex.Matches(html, "<li class=\"page-item\">"));
    }

    [Fact]
    public async Task WhileLoading_ASortClickIsIgnored()
    {
        // The overlay stops a mouse, but a keyboard user can still Tab to the header and press Enter. This is
        // what makes aria-disabled true, and what stops a second fetch racing the one in flight.
        //
        // The columns need SortField: under a controlled sort a column without one cannot be reported, so the
        // grid renders its header plain rather than offering a control that would do nothing — and there would
        // be no button here to click.
        var sorts = 0;
        BsColumn<Row>[] columns =
        [
            new BsColumn<Row> { Title = "Name", Value = r => r.Name, Sortable = true, SortField = "name" },
        ];

        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: columns, Loading: true,
            Sort: null, OnSortChange: _ => sorts++));

        await grid.InvokeAsync(ClickHandlers(grid.Html)[0]);

        Assert.Equal(0, sorts);
    }

    [Fact]
    public async Task WhileLoading_APageClickIsIgnored()
    {
        var pages = new List<int>();
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), PageSize: 2, Loading: true,
            Page: 0, OnPageChange: pages.Add));

        // The last handler is the pager's "next".
        var handlers = ClickHandlers(grid.Html);
        await grid.InvokeAsync(handlers[^1]);

        Assert.Empty(pages);
    }

    [Fact]
    public async Task WhenNotLoading_TheSameClicksStillWork()
    {
        // The guards must be keyed to Loading, not accidentally always-on.
        var pages = new List<int>();
        var grid = RaskTest.Render(BsDataGrid(Data: Rows, Columns: Columns(), PageSize: 2, Loading: false,
            Page: 0, OnPageChange: pages.Add));

        await grid.InvokeAsync(ClickHandlers(grid.Html)[^1]);

        Assert.Equal([1], pages);
    }
}
