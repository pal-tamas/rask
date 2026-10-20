namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimelineBlockExample : Component
{
    protected override Component? Render() =>
        Ui.Timeline[
            Ui.TimelineItem.Key("review")[
                Ui.TimelineIndicator[Ui.Icon.Name(Ui.IconName.Eye).Micro],
                Ui.TimelineContent[Ui.Heading["curtisss", Ui.Text.Inline()[" requested a review from "], "james_rob"]]
            ],
            Ui.TimelineItem.Key("reply")[
                Ui.TimelineBlock[
                    Ui.Callout.Secondary.Actions([Ui.Button["View message ->"], Ui.Button.Ghost["Reply"]])[
                        Ui.CalloutHeading["james_rob", Ui.Text["replied to your message · 4 days ago"]]
                    ]
                ]
            ],
            Ui.TimelineItem.Key("approved")[
                Ui.TimelineIndicator.Color(Ui.Color.Green)[Ui.Icon.Name(Ui.IconName.Check).Micro],
                Ui.TimelineContent[Ui.Heading["james_rob", Ui.Text.Inline()[" approved these changes · 3 days ago"]]]
            ]
        ];
}
