namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalloutInlineActionsExample : Component
{
    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.Callout.Icon(Ui.IconName.Cube).Secondary.Inline().Actions([Ui.Button["Track order ->"], Ui.Button.Ghost["Reschedule"]])[
                Ui.CalloutHeading["Your package is delayed"]
            ],
            Ui.Callout.Icon(Ui.IconName.ExclamationTriangle).Secondary.Inline().Actions(Ui.Button["Update billing"])[
                Ui.CalloutHeading["Payment issue detected"],
                Ui.CalloutText["Your last payment attempt failed. Update your billing details to prevent service interruption."]
            ]
        ];
}
