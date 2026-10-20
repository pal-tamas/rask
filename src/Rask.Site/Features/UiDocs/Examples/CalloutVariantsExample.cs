namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalloutVariantsExample : Component
{
    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.Callout.Secondary.Icon(Ui.IconName.InformationCircle).Heading("Your account has been successfully created."),
            Ui.Callout.Success.Icon(Ui.IconName.CheckCircle).Heading("Your account is verified and ready to use."),
            Ui.Callout.Warning.Icon(Ui.IconName.ExclamationCircle).Heading("Please verify your account to unlock all features."),
            Ui.Callout.Danger.Icon(Ui.IconName.XCircle).Heading("Something went wrong. Try again or contact support.")
        ];
}
