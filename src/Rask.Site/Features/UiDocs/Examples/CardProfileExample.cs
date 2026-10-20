namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CardProfileExample : Component
{
    protected override Component? Render() =>
        Ui.Card.Inset.Soft.Lg.Class("max-w-md")[
            Ui.CardHeader[
                Ui.CardHeading["Profile"],
                Ui.CardSubheading["This is how others will see you"],
                Ui.CardActions[Ui.Button["Edit"]]
            ],
            Ui.CardBody[
                Ui.Text.Variant(Ui.TextVariant.Strong)["Olivia Martin"],
                Ui.Text.Class("mt-4")["A few words about yourself"]
            ],
            Ui.CardFooter[
                Ui.Text["Last saved 2 minutes ago"],
                Ui.CardActions[Ui.Button["Cancel"], Ui.Button["Save"]]
            ]
        ];
}
