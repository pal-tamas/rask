namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ItemBillingExample : Component
{
    protected override Component? Render() =>
        Ui.ItemGroup.Class("max-w-xl")[
            Ui.Item[
                Ui.ItemHeading["Pro plan"],
                Ui.ItemSubheading["$20 a month for each person"],
                Ui.ItemActions[Ui.Badge.Sm.Green["Active"]]
            ],
            Ui.Item[
                Ui.ItemHeading["Next payment"],
                Ui.ItemSubheading["December 4, 2026"],
                Ui.ItemActions[Ui.Heading["$60.00"]]
            ],
            Ui.Item[
                Ui.ItemHeading["Payment details and invoices"],
                Ui.ItemSubheading["Managed by Stripe."],
                Ui.ItemActions[Ui.Button.Sm.IconTrailing(Ui.IconName.ArrowUpRight)["Manage billing"]]
            ]
        ];
}
