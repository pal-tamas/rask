namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CardBleedExample : Component
{
    protected override Component? Render() =>
        Ui.Card.Inset.Class("max-w-sm")[
            Ui.CardBody[
                Ui.CardBleed[Div.Class("aspect-video w-full bg-linear-to-br from-emerald-700 to-emerald-200").Aria("hidden", "true")]
            ],
            Ui.CardFooter[Ui.CardHeading["Morning hike"], Ui.CardSubheading["Sept 12"]]
        ];
}
