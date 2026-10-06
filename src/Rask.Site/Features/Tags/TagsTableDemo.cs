namespace Rask.Site.Features;

public sealed partial class TagsTableDemo : Component
{
    protected override Component? Render() => Ui.Table[
        Ui.TableColumns[Ui.TableColumn["#"], Ui.TableColumn["Tag"]],
        Ui.TableRows[
            Ui.TableRow[Ui.TableCell["1"], Ui.TableCell[Code["Div"]]],
            Ui.TableRow[Ui.TableCell["2"], Ui.TableCell[Code["Span"]]]
        ]
    ];
}
