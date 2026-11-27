namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ItemNarrowExample : Component
{
    private bool _reports = true;

    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-6")[
            Ui.Item[
                Ui.ItemMedia[Ui.Avatar.Circle().Src("/img/avatars/amber.svg")],
                Ui.ItemHeading["Mara Lind"],
                Ui.ItemSubheading["mara@example.com"],
                Ui.ItemActions[Ui.Button.Sm.IconTrailing(Ui.IconName.ArrowUpRight)["Edit profile"]]
            ],
            Ui.ItemGroup[
                Ui.Item[
                    Ui.ItemHeading["Rask CLI"],
                    Ui.ItemSubheading["Not found on this computer."],
                    Ui.ItemActions[Ui.Button.Sm.Subtle["Browse…"], Ui.Button.Sm["Install"]]
                ],
                Ui.Item[
                    Ui.ItemHeading["Send error reports"],
                    Ui.ItemSubheading["When something goes wrong, we get a report so it can be fixed."],
                    Ui.ItemActions[Ui.Switch.Value(_reports).OnChange(on => { _reports = on; }).Attributes(("aria-label", "Send error reports"))]
                ]
            ]
        ];
}
