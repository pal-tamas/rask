namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ItemActionsExample : Component
{
    private bool _reports = true;

    protected override Component? Render() =>
        Ui.ItemGroup.Class("max-w-xl")[
            Ui.Item[
                Ui.ItemHeading["Rask CLI"],
                Ui.ItemSubheading["Not found on this computer."],
                Ui.ItemActions[Ui.Button.Sm.Subtle["Browse…"], Ui.Button.Sm["Install"]]
            ],
            Ui.Item[
                Ui.ItemHeading["Send error reports"],
                Ui.ItemSubheading["When something goes wrong, we get a report so it can be fixed."],
                Ui.ItemActions[Ui.Switch.Value(_reports).OnChange(on => { _reports = on; }).Attributes(("aria-label", "Send error reports"))]
            ],
            Ui.Item[
                Ui.ItemHeading["Next payment"],
                Ui.ItemSubheading["December 4, 2026"],
                Ui.ItemActions[Ui.Heading["$60.00"]]
            ]
        ];
}
