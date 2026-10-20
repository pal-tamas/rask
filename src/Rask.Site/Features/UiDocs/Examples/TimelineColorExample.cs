namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimelineColorExample : Component
{
    protected override Component? Render() =>
        Ui.Timeline[
            Ui.TimelineItem.Key("failed")[
                Ui.TimelineIndicator.Color(Ui.Color.Red)[Ui.Icon.Name(Ui.IconName.XMark).Micro],
                Ui.TimelineContent[Ui.Heading["Build failed"]]
            ],
            Ui.TimelineItem.Key("warning")[
                Ui.TimelineIndicator.Color(Ui.Color.Amber)[Ui.Icon.Name(Ui.IconName.ExclamationTriangle).Micro],
                Ui.TimelineContent[Ui.Heading["Warning issued"]]
            ],
            Ui.TimelineItem.Key("deployed")[
                Ui.TimelineIndicator.Color(Ui.Color.Green)[Ui.Icon.Name(Ui.IconName.Check).Micro],
                Ui.TimelineContent[Ui.Heading["Deployed successfully"]]
            ]
        ];
}
