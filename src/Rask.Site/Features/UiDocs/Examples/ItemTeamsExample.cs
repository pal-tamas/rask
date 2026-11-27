namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ItemTeamsExample : Component
{
    protected override Component? Render() =>
        Ui.ItemGroup.Class("max-w-xl")[
            Ui.Item[
                Ui.ItemMedia[Ui.Avatar.Sm.Initials("P")],
                Ui.ItemHeading["Paraframe"],
                Ui.ItemSubheading["24 boards"],
                Ui.ItemActions[Ui.Badge.Sm["Owner"]]
            ],
            Ui.Item[
                Ui.ItemMedia[Ui.Avatar.Sm.Initials("N")],
                Ui.ItemHeading["Northwind"],
                Ui.ItemSubheading["8 boards"],
                Ui.ItemActions[Ui.Badge.Sm["Member"]]
            ]
        ];
}
