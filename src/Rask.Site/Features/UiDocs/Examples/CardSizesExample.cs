namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CardSizesExample : Component
{
    protected override Component? Render() =>
        Div.Class("grid items-start gap-6 sm:grid-cols-2")[
            Ui.Card.Xs.Separated[
                Ui.CardHeader[Ui.CardHeading["xs"], Ui.CardActions[Ui.Button["Edit"]]],
                Ui.CardBody[Ui.Text["Compact, for dense lists and sidebars."]]
            ],
            Ui.Card.Sm.Separated[
                Ui.CardHeader[Ui.CardHeading["sm"], Ui.CardActions[Ui.Button["Edit"]]],
                Ui.CardBody[Ui.Text["Tight, for small widgets and stats."]]
            ],
            Ui.Card.Md.Separated[
                Ui.CardHeader[Ui.CardHeading["md"], Ui.CardActions[Ui.Button["Edit"]]],
                Ui.CardBody[Ui.Text["The default, for most content."]]
            ],
            Ui.Card.Lg.Separated[
                Ui.CardHeader[Ui.CardHeading["lg"], Ui.CardActions[Ui.Button["Edit"]]],
                Ui.CardBody[Ui.Text["Roomy, for forms and settings."]]
            ]
        ];
}
