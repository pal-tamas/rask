namespace Rask.Site.Features.UiDocs.Examples;

// Search for what no supplier is called: the table's one row says so, across both columns, as Flux's demo draws it.
public sealed partial class EmptyExample : Component
{
    private static readonly (string Name, string City)[] Suppliers = [("Aminoff Freight", "Oslo"), ("Bothman & Sons", "Leeds"), ("Geidt Logistics", "Porto")];

    private string _search = "";

    protected override Component? Render()
    {
        var found = Suppliers.Where(s => s.Name.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        IEnumerable<Component> rows = found.Count == 0
            ? [Ui.TableRow.Key("none")[Ui.TableCell.ColSpan(2)[Ui.Empty["No supplier matches “" + _search.Trim() + "”."]]]]
            : found.Select(s => Ui.TableRow.Key(s.Name)[Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[s.Name], Ui.TableCell[s.City]]);

        return Div.Class("grid gap-4")[
            Ui.Input.Value(_search).Placeholder("Search suppliers").Icon(Ui.IconName.MagnifyingGlass).Clearable().Class("max-w-xs")
                .OnInput(text => { _search = text; }).OnChange(text => { _search = text; }),
            Ui.Table.Id("ue-suppliers").ContainerClass("min-w-0")[
                Ui.TableColumns[Ui.TableColumn["Supplier"], Ui.TableColumn["City"]],
                Ui.TableRows[rows]
            ]
        ];
    }
}
