namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalloutHeadingIconExample : Component
{
    protected override Component? Render() =>
        Ui.Callout[
            Ui.CalloutHeading.Icon(Ui.IconName.Newspaper)["Policy update"],
            Ui.CalloutText["We've updated our Terms of Service and Privacy Policy. Please review them to stay informed."]
        ];
}
