namespace Rask.Site.Features.UiDocs.Examples;

// Every heading says what its column holds, so the columns hold still and the table never shrinks below them: a phone
// scrolls it inside its box. A long name is cut short, with the whole of it in its tooltip.
public sealed partial class TableColumnWidthsExample : Component
{
    private static readonly Delivery[] Deliveries =
    [
        new("Aminoff Freight & Haulage, Nordic Fuel Division (formerly Aminoff Tankers)", "NO 914 778 271", new(2026, 3, 2), 12_480.5m, true),
        new("Bothman & Sons", "GB 123 4567 89", new(2026, 3, 9), 980m, true),
        new("Geidt Logistics", "PT 501 964 843", new(2026, 3, 11), 3_215.75m, false),
    ];

    private readonly List<Delivery> _deliveries = [.. Deliveries];

    protected override Component? Render() =>
        Ui.Table.Id("ui-deliveries")[
            Ui.TableColumns[
                Ui.TableColumn.Width(Ui.ColumnWidth.Rest)["Supplier"],
                Ui.TableColumn.Width(Ui.ColumnWidth.Short)["VAT number"],
                Ui.TableColumn.Width(Ui.ColumnWidth.Day)["Delivered"],
                Ui.TableColumn.Width(Ui.ColumnWidth.Number)["Amount"],
                Ui.TableColumn.Width(Ui.ColumnWidth.Flag)["Paid"],
                Ui.TableColumn.Width(Ui.ColumnWidth.Actions)
            ],
            Ui.TableRows[
                _deliveries.Select(delivery => Ui.TableRow.Key(delivery.Vat)[
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Cut(delivery.Supplier),
                    Ui.TableCell.Cut(delivery.Vat),
                    Ui.TableCell[Format.Day(delivery.Day)],
                    Ui.TableCell.End[Format.Number(delivery.Amount)],
                    Ui.TableCell[delivery.Paid ? "Yes" : "No"],
                    Ui.RowActions.Edit(Routes.UiFormButtonsPage()).Remove(() => { _deliveries.Remove(delivery); }, "Delete delivery", "Delete this delivery?")
                ])
            ]
        ];

    private sealed record Delivery(string Supplier, string Vat, DateOnly Day, decimal Amount, bool Paid);
}
