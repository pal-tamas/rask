namespace Rask.Example.Shared.Features;

// A BsDataGrid<T> over an in-memory list: typed BsColumn<T> columns bind straight to the row type (no
// view-model), Sortable turns a header into a sort toggle, PageSize adds a pager, Footer totals a column
// over every row (not just the visible page), and Template renders a custom cell. Sorting and paging are
// component state, so they work with no JavaScript.
public sealed partial class BsDataGridDemo : Component
{
    private sealed record Product(string Name, string Category, int Stock, decimal Price);

    private static readonly List<Product> Products =
    [
        new("Espresso Machine", "Kitchen", 12, 449.00m),
        new("Burr Grinder", "Kitchen", 34, 129.50m),
        new("Pour-over Kettle", "Kitchen", 0, 79.90m),
        new("Desk Lamp", "Office", 51, 39.00m),
        new("Standing Desk", "Office", 7, 599.00m),
        new("Ergonomic Chair", "Office", 3, 349.00m),
        new("Noise-cancelling Headphones", "Audio", 22, 279.00m),
        new("Bookshelf Speakers", "Audio", 9, 199.00m),
        new("Turntable", "Audio", 0, 329.00m),
        new("Mechanical Keyboard", "Desk", 18, 149.00m),
        new("Trackball Mouse", "Desk", 41, 69.00m),
        new("Monitor Arm", "Desk", 15, 119.00m),
    ];

    // The wrapper exists so the whole component — table AND pager — has one addressable root: BsDataGrid's Id
    // lands on the <table>, and the pager renders as its sibling.
    protected override Component? Render() =>
        Div.Id("grid-demo")[
        BsDataGrid(
            Id: "bs-grid",
            Data: Products,
            PageSize: 5,
            RowKey: p => p.Name,
            Columns:
            [
                new BsColumn<Product> { Title = "Product", Value = p => p.Name, Sortable = true },
                new BsColumn<Product> { Title = "Category", Value = p => p.Category, Sortable = true },
                // Template renders a component instead of text; SortKey keeps the numeric order while the
                // cell shows a badge.
                new BsColumn<Product>
                {
                    Title = "Stock", Class = Txt.End(), Sortable = true, SortKey = p => p.Stock,
                    Template = StockBadge,
                },
                // Footer runs over the whole result set, so the total does not change as you page.
                new BsColumn<Product>
                {
                    Title = "Price", Class = Txt.End(), Sortable = true, SortKey = p => p.Price,
                    Value = p => p.Price.ToString("C"),
                    Footer = rows => rows.Sum(p => p.Price).ToString("C"),
                },
            ])];

    private static Component StockBadge(Product product) =>
        product.Stock == 0
            ? BsBadge.Color(BsColor.Danger)["Out of stock"]
            : BsBadge.Color(product.Stock < 10 ? BsColor.Warning : BsColor.Success)[product.Stock.ToString()];
}
