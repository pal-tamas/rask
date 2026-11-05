namespace Rask.Site.Features.UiDocs.Examples;

// A list with nothing in it yet says so in place of its table. Out of a cell the page gives the ink: here the cell's.
public sealed partial class EmptyListExample : Component
{
    private bool _archived;

    protected override Component? Render() =>
        Div.Class("grid gap-4")[
            Div[Ui.Button.Key("archive").Sm.OnClick(() => { _archived = !_archived; })[_archived ? "Bring them back" : "Archive every driver"]],
            _archived
                ? Ui.Empty.Id("ue-drivers-empty").Class("text-sm text-zinc-500 dark:text-zinc-300")["No drivers yet."]
                : Ui.Table.Id("ue-drivers").ContainerClass("min-w-0")[
                    Ui.TableColumns[Ui.TableColumn["Driver"], Ui.TableColumn["Chip"]],
                    Ui.TableRows[
                        Ui.TableRow.Key("lindsey")[Ui.TableCell.Variant(Ui.TableCellVariant.Strong)["Lindsey Aminoff"], Ui.TableCell["C-0142"]],
                        Ui.TableRow.Key("jaxson")[Ui.TableCell.Variant(Ui.TableCellVariant.Strong)["Jaxson Geidt"], Ui.TableCell["C-0177"]]
                    ]
                ]
        ];
}
