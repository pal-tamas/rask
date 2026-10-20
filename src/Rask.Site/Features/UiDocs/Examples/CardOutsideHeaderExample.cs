namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CardOutsideHeaderExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-md")[
            Ui.CardHeader[Ui.CardHeading.Lg["Security"], Ui.CardActions[Ui.Button["Activity log"]]],
            Ui.Card[
                Ui.CardHeading["Password"],
                Ui.Text.Class("mt-1")["Last changed 3 months ago"]
            ]
        ];
}
