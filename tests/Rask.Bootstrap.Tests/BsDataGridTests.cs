namespace Rask.Bootstrap.Tests;

// Static-render assertions for BsDataGrid<T>. ToHtml() renders the initial state (first page, unsorted),
// which is enough to check the header/cell structure and the sortable-header control. Sort/page state
// changes are driven by click handlers (covered live by the consuming apps).
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
        Assert.Contains("<th class=\"text-end\">Qty</th>", html);
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
}
