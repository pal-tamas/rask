namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalloutActionsExample : Component
{
    protected override Component? Render() =>
        Ui.Callout.Icon(Ui.IconName.Clock).Actions([Ui.Button["Renew now"], Ui.Button.Ghost["View plans"]])[
            Ui.CalloutHeading["Subscription expiring soon"],
            Ui.CalloutText["Your current plan will expire in 3 days. Renew now to avoid service interruption and continue accessing premium features."]
        ];
}
