namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimelineExample : Component
{
    // Inside a heading a name is the heading's own ink, and what was done is inline text beside it.
    protected override Component? Render() =>
        Ui.Timeline[
            Ui.TimelineItem.Key("review")[
                Ui.TimelineIndicator[Ui.Icon.Name(Ui.IconName.Eye).Micro],
                Ui.TimelineContent[
                    Ui.Heading["curtisss", Ui.Text.Inline()[" requested a review from "], "james_rob", Ui.Text.Inline()[" · 4 days ago"]]
                ]
            ],
            Ui.TimelineItem.Key("tag")[
                Ui.TimelineIndicator[Ui.Icon.Name(Ui.IconName.Tag).Micro],
                Ui.TimelineContent[Ui.Heading["curtisss", Ui.Text.Inline()[" added tag "], Ui.Badge.Sm["feature"]]]
            ],
            Ui.TimelineItem.Key("approved")[
                Ui.TimelineIndicator.Color(Ui.Color.Green)[Ui.Icon.Name(Ui.IconName.Check).Micro],
                Ui.TimelineContent[Ui.Heading["james_rob", Ui.Text.Inline()[" approved these changes · 3 days ago"]]]
            ]
        ];
}
