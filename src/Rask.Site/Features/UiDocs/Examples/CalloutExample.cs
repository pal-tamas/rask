namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalloutExample : Component
{
    protected override Component? Render() =>
        Ui.Callout.Icon(Ui.IconName.Clock)[
            Ui.CalloutHeading["Upcoming maintenance"],
            Ui.CalloutText[
                "Our servers will be undergoing scheduled maintenance this Sunday from 2 AM - 5 AM UTC. "
                + "Some services may be temporarily unavailable. ",
                Ui.CalloutLink.Href("#")["Learn more"]
            ]
        ];
}
