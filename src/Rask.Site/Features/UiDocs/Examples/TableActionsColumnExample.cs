namespace Rask.Site.Features.UiDocs.Examples;

// Two icon buttons at the row's end — a pencil and a bin — in a column made for exactly two: Ui.TableColumn.Actions.
public sealed partial class TableActionsColumnExample : Component
{
    private static readonly Town[] Towns =
    [
        new("Debrecen", "HU-HB", 4),
        new("Szeged", "HU-CS", 2),
        new("Pécs", "HU-BA", 1),
    ];

    private readonly List<Town> _towns = [.. Towns];

    protected override Component? Render() =>
        Ui.Table.Id("ui-towns")[
            Ui.TableColumns[
                Ui.TableColumn.Rest["Town"],
                Ui.TableColumn.Short["County"],
                Ui.TableColumn.Number["Depots"],
                Ui.TableColumn.Actions
            ],
            Ui.TableRows[
                _towns.Select(town => Ui.TableRow.Key(town.Name)[
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Cut(town.Name),
                    Ui.TableCell[town.County],
                    Ui.TableCell.End[town.Depots],
                    Ui.TableCell.End.Class("py-0")[
                        Div.Class("flex justify-end gap-1")[
                            Ui.Button.Ghost.Sm.Icon(Ui.IconName.Pencil).Tooltip("Edit").Href(Routes.UiFormButtonsPage()),
                            Ui.Button.Ghost.Sm.Icon(Ui.IconName.Trash).Tooltip("Delete").OnClick(() => { _towns.Remove(town); })
                        ]
                    ]
                ])
            ]
        ];

    private sealed record Town(string Name, string County, int Depots);
}
