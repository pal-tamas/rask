using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TableStickyExample : Component
{
    // Sticky parts need a surface of their own, or the rows show through them.
    private const string Surface = "bg-white dark:bg-zinc-900";

    private static readonly (int Id, string Customer, string Date, string Status, string Amount)[] Orders =
    [
        (428, "Lindsey Aminoff", "Jul 29", "Paid", "$49.00"), (427, "Hanna Lubin", "Jul 28", "Paid", "$312.00"),
        (426, "Kianna Bushevi", "Jul 30", "Refunded", "$132.00"), (424, "Gustavo Geidt", "Jul 27", "Paid", "$31.00"),
        (423, "Nolan George", "Jul 26", "Paid", "$313.00"), (421, "Desirae George", "Jul 31", "Paid", "$32.00"),
        (420, "Jackson Bothman", "Jul 30", "Refunded", "$94.00"), (419, "Hanna Lipshutz", "Jul 29", "Paid", "$22.00"),
        (418, "Alfredo Levin", "Jul 25", "Failed", "$12.00"), (417, "Zain Lubin", "Jul 28", "Paid", "$91.00"),
    ];

    protected override Component? Render() =>
        Ui.Table.ContainerClass("max-h-64 max-w-md")[
            Ui.TableColumns.Sticky().Class(Surface)[
                Ui.TableColumn.Sticky().Class(Surface)["ID"],
                Ui.TableColumn["Customer"],
                Ui.TableColumn["Email"],
                Ui.TableColumn["Date"],
                Ui.TableColumn["Status"],
                Ui.TableColumn.End["Amount"]
            ],
            Ui.TableRows[
                Orders.Select(order => Ui.TableRow.Key(order.Id)[
                    Ui.TableCell.Sticky().Class(Surface)[order.Id.ToString(CultureInfo.InvariantCulture)],
                    Ui.TableCell[order.Customer],
                    Ui.TableCell[order.Customer.Split(' ')[0].ToLowerInvariant() + "@example.com"],
                    Ui.TableCell[order.Date],
                    Ui.TableCell.Class("py-0")[Ui.Badge.Sm[order.Status]],
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong).End[order.Amount]
                ])
            ]
        ];
}
