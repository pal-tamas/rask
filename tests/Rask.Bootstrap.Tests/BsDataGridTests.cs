using System.Text.RegularExpressions;

namespace Rask.Bootstrap.Tests;

// Static-render assertions for BsDataGrid<T>. ToHtml() renders the initial state (first page, unsorted),
// which is enough to check the header/cell structure and the sortable-header control. The sort/page/expand
// state transitions are driven by click handlers and are covered in BsDataGridInteractionTests.
public class BsDataGridTests
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
        new BsColumn<Row> { Title = "Qty", Value = r => r.Qty, Class = "text-end" },
    ];

    [Fact]
    public void RendersHeadersAndAllRows_WhenNotPaged()
    {
        var html = BsDataGrid<Row>(Data: Rows, Columns: Columns()).ToHtml();

        Assert.Contains("<table class=\"table table-striped table-hover\">", html);
        Assert.Contains("Name", html);
        Assert.Contains("Qty", html);
        // All three rows render (no paging).
        Assert.Contains("Banana", html);
        Assert.Contains("Apple", html);
        Assert.Contains("Cherry", html);
    }

    [Fact]
    public void SortableHeader_RendersAClickableButton_PlainHeaderDoesNot()
    {
        var html = BsDataGrid<Row>(Data: Rows, Columns: Columns()).ToHtml();

        // The sortable "Name" column header is a button; the non-sortable "Qty" is plain text.
        Assert.Contains("<button class=\"btn btn-sm btn-link text-decoration-none p-0 fw-semibold\"", html);
        Assert.Contains("<th class=\"text-end\" scope=\"col\">Qty</th>", html);
    }

    [Fact]
    public void ColumnClass_IsAppliedToCells()
    {
        var html = BsDataGrid<Row>(Data: Rows, Columns: Columns()).ToHtml();
        Assert.Contains("<td class=\"text-end\">5</td>", html);
    }

    [Fact]
    public void EmptyData_RendersTheEmptyPlaceholder()
    {
        var html = BsDataGrid<Row>(Data: [], Columns: Columns(), Empty: BsAlert(Color: BsColor.Info)["No rows"]).ToHtml();

        Assert.Contains("No rows", html);
        Assert.DoesNotContain("<table", html);
    }

    [Fact]
    public void Paging_RendersOnlyTheFirstPage_AndAPager()
    {
        // 3 rows, page size 2 → first page shows 2 rows and a pager with a range summary.
        var html = BsDataGrid<Row>(Data: Rows, Columns: Columns(), PageSize: 2).ToHtml();

        Assert.Contains("class=\"pagination pagination-sm mb-0\"", html);
        Assert.Contains("1-2 / 3", html);
    }

    [Fact]
    public void Footer_RendersTfootWithColumnTotals_OverAllRows()
    {
        BsColumn<Row>[] columns =
        [
            new BsColumn<Row> { Title = "Name", Value = r => r.Name },
            new BsColumn<Row> { Title = "Qty", Class = "text-end", Value = r => r.Qty,
                Footer = rows => rows.Sum(r => r.Qty) },
        ];

        var html = BsDataGrid<Row>(Data: Rows, Columns: columns).ToHtml();

        Assert.Contains("<tfoot>", html);
        Assert.Contains("<td class=\"text-end\">9</td>", html); // 3 + 5 + 1
    }

    [Fact]
    public void NoFooter_IsRendered_WhenNoColumnDefinesOne()
    {
        var html = BsDataGrid<Row>(Data: Rows, Columns: Columns()).ToHtml();

        Assert.DoesNotContain("<tfoot>", html);
    }

    [Fact]
    public void ExpandedContent_RendersAnExpanderPerRow_AndHidesDetailUntilExpanded()
    {
        var html = BsDataGrid<Row>(Data: Rows, Columns: Columns(),
            RowKey: r => r.Name,
            ExpandedContent: r => BsAlert(Color: BsColor.Info)[$"detail-{r.Name}"]).ToHtml();

        // A collapsed chevron toggle renders per row; the detail content stays hidden until expanded.
        Assert.Contains("bi-chevron-right", html);
        Assert.DoesNotContain("detail-Banana", html);
    }

    [Fact]
    public void SortHeader_IsAButton_NotASubmit()
    {
        // <button> defaults to type=submit, so without an explicit type a grid inside a <form> submits the
        // form on every sort click.
        var html = BsDataGrid<Row>(Data: Rows, Columns: Columns()).ToHtml();

        Assert.Contains("text-decoration-none p-0 fw-semibold\" type=\"button\"", html);
    }

    [Fact]
    public void SortableHeaders_CarryAriaSort_AndPlainHeadersDoNot()
    {
        var html = BsDataGrid<Row>(Data: Rows, Columns: Columns()).ToHtml();

        // Unsorted but sortable: aria-sort="none". Attribute order is an invariant — aria-* precedes the
        // tag-specific scope.
        Assert.Contains("<th aria-sort=\"none\" scope=\"col\">", html);
        Assert.DoesNotContain("<th class=\"text-end\" aria-sort", html);
    }

    [Fact]
    public void HeadersAreScopedToTheirColumn()
    {
        var html = BsDataGrid<Row>(Data: Rows, Columns: Columns()).ToHtml();

        Assert.Equal(2, Regex.Matches(html, "scope=\"col\"").Count);
    }

    [Fact]
    public void Expander_HasAnAccessibleName_AndNoDanglingAriaControls()
    {
        var html = BsDataGrid<Row>(Data: Rows, Columns: Columns(), RowKey: r => r.Name,
            ExpandedContent: r => BsAlert(Color: BsColor.Info)[$"detail-{r.Name}"]).ToHtml();

        // The toggle is icon-only, so it needs a name. aria-controls is absent while collapsed: the row it
        // would point at is not in the document yet.
        Assert.Contains("aria-expanded=\"false\" aria-label=\"Toggle details\"", html);
        Assert.DoesNotContain("aria-controls", html);
    }

    [Fact]
    public void IdAndClass_ReachTheTable()
    {
        // BsDataGrid derives from BsBlock, so it has the Id/Class passthrough every other Bs component has.
        var html = BsDataGrid<Row>(Data: Rows, Columns: Columns(), Id: "grid", Class: "shadow").ToHtml();

        Assert.Contains("<table id=\"grid\" class=\"table table-striped table-hover shadow\">", html);
    }

    [Fact]
    public void DensityFlags_MapToBootstrapClasses()
    {
        Assert.Contains("<table class=\"table\">",
            BsDataGrid<Row>(Data: Rows, Columns: Columns(), Striped: false, Hover: false,
                Responsive: false).ToHtml());

        Assert.Contains("<table class=\"table table-striped table-hover table-sm\">",
            BsDataGrid<Row>(Data: Rows, Columns: Columns(), Small: true, Responsive: false).ToHtml());
    }

    [Fact]
    public void Responsive_WrapsTheTable_AndLeavesThePagerOutside()
    {
        var html = BsDataGrid<Row>(Data: Rows, Columns: Columns(), PageSize: 2).ToHtml();

        Assert.StartsWith("<div class=\"table-responsive\">", html);
        // The pager must not be trapped inside the scroll container.
        Assert.Contains("</table></div><div class=\"d-flex", html);

        Assert.DoesNotContain("table-responsive",
            BsDataGrid<Row>(Data: Rows, Columns: Columns(), Responsive: false).ToHtml());
    }

    [Fact]
    public void CellValues_AreHtmlEncoded()
    {
        // Cell text goes through the encoding Text path, never Raw — a value is data, not markup.
        var html = BsDataGrid<Row>(
            Data: [new Row("<script>alert(1)</script>", 1)],
            Columns: [new BsColumn<Row> { Title = "Name", Value = r => r.Name }]).ToHtml();

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}
