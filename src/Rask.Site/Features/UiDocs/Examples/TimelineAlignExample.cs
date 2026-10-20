namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimelineAlignExample : Component
{
    private static readonly (Ui.TimelineAlign Align, string Number, string Title, string Detail)[] Items =
    [
        (Ui.TimelineAlign.Start, "1", "Start align", "Align indicator and content to the top/start"),
        (Ui.TimelineAlign.Baseline, "2", "Baseline align", "Align indicator and content to the baseline of the first line of text"),
        (Ui.TimelineAlign.Center, "3", "Center align", "Align indicator and content to the center"),
        (Ui.TimelineAlign.End, "4", "End align", "Align indicator and content to the bottom/end"),
    ];

    protected override Component? Render() =>
        Ui.Timeline.Class("[--ui-timeline-item-gap:1.5rem]")[
            Items.Select(item => Ui.TimelineItem.Key(item.Number).Align(item.Align)[
                Ui.TimelineIndicator[item.Number],
                Ui.TimelineContent.Class("gap-1")[Ui.Heading[item.Title], Ui.Text[item.Detail]]
            ])
        ];
}
