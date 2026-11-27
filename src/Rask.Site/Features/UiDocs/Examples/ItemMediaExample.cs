namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ItemMediaExample : Component
{
    protected override Component? Render() =>
        Ui.ItemGroup.Class("max-w-xl")[
            Ui.Item[
                Ui.ItemMedia[Ui.Avatar.Sm.Circle().Src("/img/avatars/amber.svg")],
                Ui.ItemHeading["Mara Lind"],
                Ui.ItemSubheading["mara@example.com"]
            ],
            Ui.Item[
                Ui.ItemMedia[Ui.Avatar.Sm.Icon(Ui.IconName.CommandLine)],
                Ui.ItemHeading["Rask CLI"],
                Ui.ItemSubheading["Version 1.4.2"]
            ],
            Ui.Item[
                Ui.ItemMedia[Ui.Avatar.Sm.Initials("N")],
                Ui.ItemHeading["Northwind"],
                Ui.ItemSubheading["8 boards"]
            ]
        ];
}
