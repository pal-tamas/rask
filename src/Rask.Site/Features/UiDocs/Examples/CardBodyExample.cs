namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CardBodyExample : Component
{
    protected override Component? Render() =>
        Div.Class("grid items-start gap-6 sm:grid-cols-2 lg:grid-cols-3")[
            Preferences(Ui.Card.Seamless, "Seamless"),
            Preferences(Ui.Card.Inset.Soft, "Inset"),
            Preferences(Ui.Card.Flush.Soft, "Flush"),
            Preferences(Ui.Card.Divided, "Divided"),
            Preferences(Ui.Card.Divided.Divider(Ui.CardDivider.Inset), "Divided, inset"),
            Preferences(Ui.Card.Separated, "Separated")
        ];

    private static Component Preferences(UiCard card, string body) =>
        card.Key(body)[
            Ui.CardHeader[Ui.CardHeading[body], Ui.CardSubheading["Choose what you hear about"]],
            Ui.CardBody[Ui.Text["Product updates"], Ui.Text.Class("mt-4")["Weekly digest"], Ui.Text.Class("mt-4")["Security alerts"]],
            Ui.CardFooter[Ui.Text["Changes apply at once"]]
        ];
}
