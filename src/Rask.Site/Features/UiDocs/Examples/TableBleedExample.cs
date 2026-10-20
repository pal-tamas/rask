namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TableBleedExample : Component
{
    private static readonly (string Customer, string Status, string Amount)[] Customers =
    [
        ("Lindsey Aminoff", "Paid", "$49.00"),
        ("Hanna Lubin", "Paid", "$312.00"),
        ("Kianna Bushevi", "Refunded", "$132.00"),
    ];

    // The box states how far the table may run through its padding; a card states its own.
    protected override Component? Render() =>
        Div.Class("max-w-md rounded-lg border border-zinc-200 p-4 [--ui-bleed:1rem] dark:border-zinc-700")[
            Ui.Heading["Recent customers"],
            Ui.Table.Bleed().ContainerClass("mt-4")[
                Ui.TableColumns[
                    Ui.TableColumn["Customer"],
                    Ui.TableColumn["Status"],
                    Ui.TableColumn.End["Amount"]
                ],
                Ui.TableRows[
                    Customers.Select(customer => Ui.TableRow.Key(customer.Customer)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[customer.Customer],
                        Ui.TableCell.Class("py-0")[Ui.Badge.Sm[customer.Status]],
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong).End[customer.Amount]
                    ])
                ]
            ]
        ];
}
