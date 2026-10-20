namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimelineSubgridExample : Component
{
    // The avatars are plain initials in a circle; each subgrid puts its first child in the indicators' column.
    protected override Component? Render() =>
        Ui.Timeline[
            Ui.TimelineItem.Key("review")[
                Ui.TimelineIndicator[Ui.Icon.Name(Ui.IconName.Eye).Micro],
                Ui.TimelineContent[Ui.Heading["curtisss", Ui.Text.Inline()[" requested a review from "], "james_rob"]]
            ],
            Ui.TimelineItem.Key("thread")[
                Ui.TimelineBlock.Class("overflow-hidden rounded-xl border border-zinc-500/20")[
                    Ui.TimelineSubgrid.Class("bg-zinc-500/5 p-3")[
                        Ui.Avatar.Xs.Circle().Initials("JR"),
                        Div.Class("space-y-1")[
                            Ui.Heading["james_rob", Ui.Text.Inline().Class("font-medium")[" commented · 4 days ago"]],
                            Ui.Text.Class("text-zinc-800 dark:text-white")["Contrast slider goes a bit wild at the top end..."]
                        ]
                    ],
                    Div.Class("border-t border-zinc-500/20"),
                    Ui.TimelineSubgrid.Class("p-3")[Ui.Avatar.Xs.Circle().Initials("CP"), Ui.Text["Leave a reply..."]]
                ]
            ],
            Ui.TimelineItem.Key("approved")[
                Ui.TimelineIndicator.Color(Ui.Color.Green)[Ui.Icon.Name(Ui.IconName.Check).Micro],
                Ui.TimelineContent[Ui.Heading["james_rob", Ui.Text.Inline()[" approved these changes · 3 days ago"]]]
            ]
        ];
}
