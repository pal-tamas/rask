namespace Rask.Site.Features.UiDocs.Examples;

// A row that can do a great deal: eighteen actions in named groups, taller than a phone. The menu is held to the room
// it has and scrolls inside, so every action is reachable — by a finger, a wheel or the arrow keys.
public sealed partial class RowActionsLongMenuExample : Component
{
    private static readonly string[] Pumps =
    [
        "North yard", "South yard", "Depot 1", "Depot 2", "Harbour", "Airfield", "Quarry", "Farm east", "Farm west", "Washbay",
    ];

    private static readonly string[] Reads =
    [
        "Status", "Clock", "New fuelings", "Missing fuelings", "All fuelings", "Settings", "Tank level", "Last record",
        "Capabilities", "Stored fuelings",
    ];

    private static readonly string[] Writes = ["Set the clock", "Send the drivers", "Restart", "Rewind the counters"];

    private List<string> _pumps = [.. Pumps];
    private string? _asked;

    protected override Component? Render() =>
        Div.Class("grid gap-3")[
            Div.Class("flex items-center gap-3")[
                Ui.Button.Key("restore").Sm.OnClick(() => { _pumps = [.. Pumps]; _asked = null; })["Restore"],
                Ui.Text.Id("url-asked")[_asked ?? "Nothing asked yet."]
            ],
            Ui.Table.Id("url-pumps")[
                Ui.TableColumns[
                    Ui.TableColumn["Pump"],
                    Ui.TableColumn.RowMenu
                ],
                Ui.TableRows[
                    _pumps.Select(pump => Ui.TableRow.Key(pump)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Cut(pump),
                        Ui.RowActions.Key(pump)
                            .View(Routes.UiTablePage())
                            .Edit(Routes.UiFormButtonsPage())
                            .Remove(() => _pumps.Remove(pump), "Delete pump", $"Delete {pump}?")[
                                Ui.MenuGroup.Key("read").Heading("Read")[Reads.Select(what => Ui.MenuItem.Key(what).OnClick(() => { _asked = $"{pump}: {what}."; })[what])],
                                Ui.MenuGroup.Key("write").Heading("Write")[Writes.Select(what => Ui.MenuItem.Key(what).OnClick(() => { _asked = $"{pump}: {what}."; })[what])],
                                Ui.MenuGroup.Key("stopping")[Ui.MenuItem.Key("stop").Danger.Icon(Ui.IconName.StopCircle).OnClick(() => { _asked = $"{pump}: stopped."; })["Stop"]]
                            ]
                    ])
                ]
            ]
        ];
}
