namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TableExample : Component
{
    private static readonly (string Customer, string Date, string Status, Ui.Color? Hue, string Amount)[] Orders =
    [
        ("Lindsey Aminoff", "Jul 29", "Paid", Ui.Color.Green, "$49.00"),
        ("Hanna Lubin", "Jul 28", "Paid", Ui.Color.Green, "$312.00"),
        ("Kianna Bushevi", "Jul 30", "Refunded", null, "$132.00"),
        ("Gustavo Geidt", "Jul 27", "Paid", Ui.Color.Green, "$31.00"),
    ];

    protected override Component? Render() =>
        Ui.Table[
            Ui.TableColumns[
                Ui.TableColumn["Customer"],
                Ui.TableColumn["Date"],
                Ui.TableColumn["Status"],
                Ui.TableColumn.End["Amount"]
            ],
            Ui.TableRows[
                Orders.Select(order => Ui.TableRow.Key(order.Customer)[
                    Ui.TableCell[order.Customer],
                    Ui.TableCell[order.Date],
                    // A cell holding a badge drops its own vertical padding, so the row keeps its height.
                    Ui.TableCell.Class("py-0")[Ui.Badge.Sm.Color(order.Hue)[order.Status]],
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong).End[order.Amount]
                ])
            ]
        ];
}
