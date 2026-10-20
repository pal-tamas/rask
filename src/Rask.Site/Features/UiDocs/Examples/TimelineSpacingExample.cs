namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimelineSpacingExample : Component
{
    protected override Component? Render() =>
        Ui.Timeline.Class("[--ui-timeline-item-gap:3rem] [--ui-timeline-content-gap:1rem]")[
            Ui.TimelineItem.Key("1")[Ui.TimelineIndicator["1"], Ui.TimelineContent[Ui.Heading["Create account"]]],
            Ui.TimelineItem.Key("2")[Ui.TimelineIndicator["2"], Ui.TimelineContent[Ui.Heading["Provide details"]]],
            Ui.TimelineItem.Key("3")[Ui.TimelineIndicator["3"], Ui.TimelineContent[Ui.Heading["Review & confirm"]]]
        ];
}
