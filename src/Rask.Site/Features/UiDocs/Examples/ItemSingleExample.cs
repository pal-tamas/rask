namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ItemSingleExample : Component
{
    protected override Component? Render() =>
        Ui.Item.Class("max-w-xl")[
            Ui.ItemHeading["Sign out"],
            Ui.ItemSubheading["Your boards stay on this device."],
            Ui.ItemActions[Ui.Button.Sm["Sign out"]]
        ];
}
