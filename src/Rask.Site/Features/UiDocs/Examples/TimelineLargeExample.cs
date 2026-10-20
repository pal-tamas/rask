namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimelineLargeExample : Component
{
    private static readonly (string Number, string Title, string Detail)[] Steps =
    [
        ("1", "Submit", "Complete the form and provide all necessary assets."),
        ("2", "Review", "Verify your data and approve the configuration."),
        ("3", "Publish", "Finalize your settings and push your changes live."),
    ];

    protected override Component? Render() =>
        Ui.Timeline.Lg[
            Steps.Select(step => Ui.TimelineItem.Key(step.Number)[
                Ui.TimelineIndicator[step.Number],
                Ui.TimelineContent[Ui.Heading[step.Title], Ui.Text[step.Detail]]
            ])
        ];
}
