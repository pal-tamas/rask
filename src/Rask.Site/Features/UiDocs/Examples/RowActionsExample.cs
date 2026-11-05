namespace Rask.Site.Features.UiDocs.Examples;

// The pencil opens the row's form; the bin asks in the layout's Ui.Confirm. A supplier with orders cannot be deleted.
public sealed partial class RowActionsExample : Component
{
    private static readonly Supplier[] Everyone =
    [
        new("Aminoff Freight", "Oslo", Orders: 3), new("Bothman & Sons", "Leeds", Orders: 0), new("Geidt Logistics", "Porto", Orders: 0),
    ];

    private List<Supplier> _suppliers = [.. Everyone];

    protected override Component? Render() =>
        Div.Class("grid gap-3")[
            Div[Ui.Button.Key("restore").Sm.OnClick(() => { _suppliers = [.. Everyone]; })["Restore"]],
            Ui.Table.Id("ura-suppliers").ContainerClass("min-w-0")[
                Ui.TableColumns[
                    Ui.TableColumn["Supplier"],
                    Ui.TableColumn.Width(Ui.ColumnWidth.Reference)["City"],
                    Ui.TableColumn.Width(Ui.ColumnWidth.Actions)
                ],
                Ui.TableRows[
                    _suppliers.Select(supplier => Ui.TableRow.Key(supplier.Name)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Cut(supplier.Name),
                        Ui.TableCell.Cut(supplier.City),
                        Ui.RowActions.Edit(Routes.UiFormButtonsPage()).Remove(Removal(supplier), "Delete supplier", $"Delete {supplier.Name}?")
                    ])
                ]
            ]
        ];

    // A supplier with orders cannot be deleted: no delete, no bin.
    private Action? Removal(Supplier supplier) => supplier.Orders == 0 ? () => _suppliers.Remove(supplier) : null;

    private sealed record Supplier(string Name, string City, int Orders);
}
