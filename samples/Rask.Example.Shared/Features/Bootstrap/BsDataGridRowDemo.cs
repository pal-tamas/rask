namespace Rask.Example.Shared.Features;

// Row-level interaction on a BsDataGrid<T>: OnRowClick opens a row, and RowClass tints it from its own data.
//
// Note where the row click does and does not reach. The grid attaches it to the cells of the columns whose
// RowClickable resolves true — by default the Value columns, never the Template ones. That is what keeps the
// "Open" button below working: a click handler above it would cancel the button's own click.
//
// A clickable row is a pointer shortcut, never the only way in. The Open button is the real, keyboard-reachable
// control, and the row click just duplicates it.
public sealed partial class BsDataGridRowDemo : Component
{
    private sealed record Invoice(string Number, string Customer, decimal Amount, int DaysOverdue);

    private static readonly List<Invoice> Invoices =
    [
        new("INV-1041", "Northwind Ltd", 1240.00m, 0),
        new("INV-1042", "Contoso", 480.50m, 12),
        new("INV-1043", "Fabrikam", 3100.00m, 0),
        new("INV-1044", "Adventure Works", 920.00m, 45),
        new("INV-1045", "Tailspin Toys", 275.00m, 0),
    ];

    private string? _opened;

    protected override Component? Render() =>
        Div.Id("grid-row-demo")[
            _opened is not null
                ? BsAlert.Id("grid-row-opened").Color(BsColor.Info).Class(Margin.Bottom(3))[$"Opened {_opened}"]
                : null,
            BsDataGrid
                .Data(Invoices)
                .Columns([
                    new BsColumn<Invoice> { Title = "Invoice", Value = i => i.Number, Sortable = true },
                    new BsColumn<Invoice> { Title = "Customer", Value = i => i.Customer, Sortable = true },
                    new BsColumn<Invoice>
                    {
                        Title = "Amount", Class = Txt.End(), Sortable = true, SortKey = i => i.Amount,
                        Value = i => i.Amount.ToString("C"),
                    },
                    // A Template column, so it is not row-clickable by default and the button keeps its click.
                    new BsColumn<Invoice>
                    {
                        Title = "", Class = Txt.End(),
                        Template = i => BsButton
                            .Id($"open-{i.Number}")
                            .Color(BsColor.Primary)
                            .Outline(true)
                            .Size(BsSize.Sm)
                            .OnClick(() => _opened = i.Number)["Open"],
                    },
                ])
                .Id("bs-grid-row")
                .RowKey(i => i.Number)
                .OnRowClick(i => _opened = i.Number)
                .RowClass(i => i.DaysOverdue switch
                {
                    0 => null,
                    < 30 => "table-warning",
                    _ => "table-danger",
                })];
}
