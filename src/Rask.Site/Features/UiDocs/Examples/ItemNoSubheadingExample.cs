namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ItemNoSubheadingExample : Component
{
    private bool _renew = true;

    protected override Component? Render() =>
        Ui.ItemGroup.Class("max-w-xl")[
            Ui.Item[Ui.ItemHeading["Subscribed since"], Ui.ItemActions[Ui.Text["October 4, 2026"]]],
            Ui.Item[Ui.ItemHeading["Seats"], Ui.ItemActions[Ui.Text["3 of 5 used"]]],
            Ui.Item[
                Ui.ItemHeading["Renew automatically"],
                Ui.ItemActions[Ui.Switch.Value(_renew).OnChange(on => { _renew = on; }).Attributes(("aria-label", "Renew automatically"))]
            ]
        ];
}
