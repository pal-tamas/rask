namespace Rask.Site.Features.UiDocs.Examples;

// A pump's row folds out a row of its own beneath it: one cell across all three columns, ColSpan(3).
public sealed partial class TableFoldOutExample : Component
{
    private static readonly (string Name, string Fuel, string Pistols)[] Pumps =
    [
        ("Pump 1", "Diesel", "Pistol 1 · 2,340 l this month, Pistol 2 · 1,108 l this month"),
        ("Pump 2", "AdBlue", "Pistol 3 · 412 l this month"),
    ];

    private readonly HashSet<string> _open = [with(StringComparer.Ordinal), "Pump 1"];

    protected override Component? Render() =>
        Ui.Table.Id("utf-pumps").ContainerClass("min-w-0")[
            Ui.TableColumns[Ui.TableColumn.Class("w-12")[Span.Class("sr-only")["Details"]], Ui.TableColumn["Pump"], Ui.TableColumn["Fuel"]],
            Ui.TableRows[Pumps.SelectMany(Rows)]
        ];

    private IEnumerable<Component> Rows((string Name, string Fuel, string Pistols) pump)
    {
        var open = _open.Contains(pump.Name);
        yield return Ui.TableRow.Key(pump.Name)[
            Ui.TableCell.Class("py-0")[
                Ui.Button.Key("fold").Ghost.Sm.Square(true).Icon(Ui.IconName.ChevronDown).Class(open ? null : "-rotate-90")
                    .Tooltip(open ? "Hide pistols" : "Show pistols").AriaExpanded(open)
                    .OnClick(() => { if (!_open.Remove(pump.Name)) { _open.Add(pump.Name); } })
            ],
            Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[pump.Name],
            Ui.TableCell[pump.Fuel]
        ];

        if (open)
        {
            yield return Ui.TableRow.Key(pump.Name + "-pistols")[
                Ui.TableCell.ColSpan(3).Class("bg-zinc-50 dark:bg-zinc-800/50")[Div.Class("ps-15 whitespace-normal")[pump.Pistols]]
            ];
        }
    }
}
