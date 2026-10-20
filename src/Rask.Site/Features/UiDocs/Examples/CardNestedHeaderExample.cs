namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CardNestedHeaderExample : Component
{
    protected override Component? Render() =>
        Ui.Card.Inset.Soft.Class("max-w-md")[
            Ui.CardHeader[Ui.CardHeading["Billing"], Ui.CardSubheading["Manage your plan and payments"]],
            Ui.CardBody[
                Ui.CardHeader[
                    Ui.CardHeading["Pro plan"],
                    Ui.CardSubheading["$24 per month, renews Oct 12"],
                    Ui.CardActions[Ui.Button["Change plan"]]
                ],
                Ui.Text["8 of 10 seats used"]
            ],
            Ui.CardFooter[Ui.Text["Invoices are emailed to billing@acme.com"]]
        ];
}
