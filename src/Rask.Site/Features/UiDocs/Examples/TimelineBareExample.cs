namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimelineBareExample : Component
{
    private static readonly (Ui.IconName Icon, string Title, string When)[] Events =
    [
        (Ui.IconName.DocumentText, "Draft created", " · 2 days ago"),
        (Ui.IconName.PencilSquare, "Edits made", " · 1 day ago"),
        (Ui.IconName.PaperAirplane, "Published", " · just now"),
    ];

    protected override Component? Render() =>
        Ui.Timeline[
            Events.Select(e => Ui.TimelineItem.Key(e.Title)[
                Ui.TimelineIndicator.Bare[Ui.Icon.Name(e.Icon).Class("size-6 text-zinc-400")],
                Ui.TimelineContent[Ui.Heading[e.Title, Ui.Text.Inline()[e.When]]]
            ])
        ];
}
