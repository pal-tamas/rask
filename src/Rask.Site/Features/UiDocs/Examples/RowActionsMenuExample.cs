using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

// Everything a row can do behind one ⋯: look at it, edit it, what the page adds, and the delete last, asking first.
// Its column keeps to the table's right edge while the others scroll under it. A supplier with orders cannot be deleted.
public sealed partial class RowActionsMenuExample : Component
{
    private static readonly Supplier[] Everyone =
    [
        new("Aminoff Freight", "Oslo", "NO 912 345 678", Orders: 3), new("Bothman & Sons", "Leeds", "GB 123 4567 89", Orders: 0),
        new("Geidt Logistics", "Porto", "PT 501 234 567", Orders: 0),
    ];

    private List<Supplier> _suppliers = [.. Everyone];
    private string? _chosen;

    protected override Component? Render() =>
        Div.Class("grid gap-3")[
            Div.Class("flex items-center gap-3")[
                Ui.Button.Key("restore").Sm.OnClick(() => { _suppliers = [.. Everyone]; _chosen = null; })["Restore"],
                Ui.Text.Id("urm-chosen")[_chosen is null ? "No supplier chosen." : $"Chosen: {_chosen}."]
            ],
            Ui.Table.Id("urm-suppliers").ContainerClass("min-w-0")[
                Ui.TableColumns[
                    Ui.TableColumn["Supplier"],
                    Ui.TableColumn.Reference["City"],
                    Ui.TableColumn.Short["VAT number"],
                    Ui.TableColumn.Number["Orders"],
                    Ui.TableColumn.RowMenu
                ],
                Ui.TableRows[
                    _suppliers.Select(supplier => Ui.TableRow.Key(supplier.Name)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Cut(supplier.Name),
                        Ui.TableCell.Cut(supplier.City),
                        Ui.TableCell.Cut(supplier.Vat),
                        Ui.TableCell.End[supplier.Orders.ToString(CultureInfo.InvariantCulture)],
                        Ui.RowActions.Key(supplier.Name).Menu()
                            .View(Routes.UiTablePage())
                            .Edit(Routes.UiFormButtonsPage())
                            .Remove(Removal(supplier), "Delete supplier", $"Delete {supplier.Name}?")[
                                Ui.MenuItem.Icon(Ui.IconName.Check).OnClick(() => { _chosen = supplier.Name; })["Choose"]
                            ]
                    ])
                ]
            ]
        ];

    // A supplier with orders cannot be deleted: no delete in its menu.
    private Action? Removal(Supplier supplier) => supplier.Orders == 0 ? () => _suppliers.Remove(supplier) : null;

    private sealed record Supplier(string Name, string City, string Vat, int Orders);
}
