namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimelineBaselineExample : Component
{
    // The indicator's hidden first line takes the heading's size, so the two baselines meet.
    protected override Component? Render() =>
        Ui.Timeline.Lg[
            Ui.TimelineItem.Key("without").Baseline[
                Ui.TimelineIndicator["1"],
                Ui.TimelineContent.Class("gap-1")[
                    Ui.Heading.Xl["Without adjustment"],
                    Ui.Text["The indicator may not align perfectly with large text"]
                ]
            ],
            Ui.TimelineItem.Key("with").Baseline.Class("[&_[data-ui-timeline-baseline]]:text-2xl")[
                Ui.TimelineIndicator["2"],
                Ui.TimelineContent.Class("gap-1")[
                    Ui.Heading.Xl["With baseline adjustment"],
                    Ui.Text["The indicator aligns precisely with the heading baseline"]
                ]
            ]
        ];
}
