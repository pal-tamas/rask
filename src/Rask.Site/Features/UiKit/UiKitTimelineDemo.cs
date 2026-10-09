namespace Rask.Site.Features.UiKit;

/// <summary>
///     Flux's timeline, example for example.
/// </summary>
/// <remarks>
///     Nothing here holds state: a timeline is a list. What an item says is the kit's own heading, text,
///     badge and callout, as on Flux's page — the timeline draws the indicators and the line between them,
///     and places the content beside each. The avatars and the reply box are plain markup: Flux's avatar and
///     composer are not in the kit yet.
/// </remarks>
public sealed partial class UiKitTimelineDemo : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("grid gap-8 lg:grid-cols-2")[Directions().Concat(Indicators()).Concat(Blocks()).Concat(Spacing())];

    private static Component[] Directions() =>
    [
        Example("ui-timeline", "Timeline", Ui.Timeline[
            Event(Ui.IconName.Eye, Who("curtisss"), Said(" requested a review from "), Who("james_rob"), Said(" · 4 days ago")),
            Event(Ui.IconName.Tag, Who("curtisss"), Said(" added tag "), Tag("feature")),
            Approved()
        ]),
        Example("ui-timeline-large", "Large", Ui.Timeline.Lg[
            Step("1", "Submit", "Complete the form and provide all necessary assets."),
            Step("2", "Review", "Verify your data and approve the configuration."),
            Step("3", "Publish", "Finalize your settings and push your changes live.")
        ]),
        Example("ui-timeline-horizontal", "Horizontal", Ui.Timeline.Horizontal()[
            Stage(null, Ui.IconName.CreditCard, "Order confirmed"),
            Stage(null, Ui.IconName.Truck, "On its way"),
            Stage(null, Ui.IconName.Home, "Delivered")
        ]),
        Example("ui-timeline-status", "Status", Ui.Timeline.Horizontal()[
            Stage(Ui.TimelineStatus.Complete, Ui.IconName.CreditCard, "Order confirmed"),
            Stage(Ui.TimelineStatus.Current, Ui.IconName.Truck, "On its way"),
            Stage(Ui.TimelineStatus.Incomplete, Ui.IconName.Home, "Delivered")
        ])
    ];

    private static Component[] Indicators() =>
    [
        Example("ui-timeline-color", "Indicator color", Ui.Timeline[
            Coloured(Ui.Color.Red, Ui.IconName.XMark, "Build failed"),
            Coloured(Ui.Color.Amber, Ui.IconName.ExclamationTriangle, "Warning issued"),
            Coloured(Ui.Color.Green, Ui.IconName.Check, "Deployed successfully")
        ]),
        Example("ui-timeline-bare", "Bare indicator", Ui.Timeline[
            Bare(Ui.IconName.DocumentText, "Draft created", " · 2 days ago"),
            Bare(Ui.IconName.PencilSquare, "Edits made", " · 1 day ago"),
            Bare(Ui.IconName.PaperAirplane, "Published", " · just now")
        ])
    ];

    private static Component[] Blocks() =>
    [
        Example("ui-timeline-block", "Block item", Ui.Timeline[
            Event(Ui.IconName.Eye, Who("curtisss"), Said(" requested a review from "), Who("james_rob")),
            Ui.TimelineItem.Key("block")[
                Ui.TimelineBlock[
                    Ui.Callout.Secondary.Actions([Ui.Button["View message ->"], Ui.Button.Ghost["Reply"]])[
                        Ui.CalloutHeading["james_rob", Ui.Text["replied to your message · 4 days ago"]]
                    ]
                ]
            ],
            Approved()
        ]),
        Example("ui-timeline-subgrid", "Block subgrid", Ui.Timeline[
            Event(Ui.IconName.Eye, Who("curtisss"), Said(" requested a review from "), Who("james_rob")),
            Ui.TimelineItem.Key("thread")[
                Ui.TimelineBlock.Class("overflow-hidden rounded-xl border border-zinc-500/20")[
                    Ui.TimelineSubgrid.Class("bg-zinc-500/5 p-3")[
                        Face("JR"),
                        Div.Class("space-y-1")[
                            Ui.Heading[Who("james_rob"), Ui.Text.Inline().Class("font-medium")[" commented · 4 days ago"]],
                            Ui.Text.Class("text-zinc-800 dark:text-white")["Contrast slider goes a bit wild at the top end..."]
                        ]
                    ],
                    Div.Class("border-t border-zinc-500/20"),
                    Ui.TimelineSubgrid.Class("p-3")[Face("CP"), Div.Class("text-sm opacity-60")["Leave a reply..."]]
                ]
            ],
            Approved()
        ])
    ];

    private static Component[] Spacing() =>
    [
        Example("ui-timeline-align", "Alignment", Ui.Timeline.Class("[--ui-timeline-item-gap:1.5rem]")[
            Aligned(Ui.TimelineAlign.Start, "1", "Start align", "Align indicator and content to the top/start"),
            Aligned(Ui.TimelineAlign.Baseline, "2", "Baseline align", "Align indicator and content to the baseline of the first line of text"),
            Aligned(Ui.TimelineAlign.Center, "3", "Center align", "Align indicator and content to the center"),
            Aligned(Ui.TimelineAlign.End, "4", "End align", "Align indicator and content to the bottom/end")
        ]),
        // The indicator's hidden first line takes the heading's size, so the two baselines meet.
        Example("ui-timeline-baseline", "Baseline adjustment", Ui.Timeline.Lg[
            Ui.TimelineItem.Key("1").Baseline[
                Ui.TimelineIndicator["1"],
                Ui.TimelineContent.Class("gap-1")[Ui.Heading.Xl["Without adjustment"], Ui.Text["The indicator may not align perfectly with large text"]]
            ],
            Ui.TimelineItem.Key("2").Baseline.Class("[&_[data-ui-timeline-baseline]]:text-2xl")[
                Ui.TimelineIndicator["2"],
                Ui.TimelineContent.Class("gap-1")[Ui.Heading.Xl["With baseline adjustment"], Ui.Text["The indicator aligns precisely with the heading baseline"]]
            ]
        ]),
        Example("ui-timeline-spacing", "Spacing", Ui.Timeline.Class("[--ui-timeline-item-gap:3rem] [--ui-timeline-content-gap:1rem]")[
            Step("1", "Create account", null),
            Step("2", "Provide details", null),
            Step("3", "Review & confirm", null)
        ])
    ];

    private static Component Example(string testid, string heading, Component timeline) =>
        Div.Key(testid)[
            H3.Class("mb-3 text-sm font-semibold")[heading],
            Div.Data("testid", testid)[timeline]
        ];

    private static Component Event(Ui.IconName icon, params Component[] said) =>
        Ui.TimelineItem.Key(icon.ToString())[
            Ui.TimelineIndicator[Ui.Icon.Name(icon).Micro],
            Ui.TimelineContent[Ui.Heading[said]]
        ];

    private static Component Approved() =>
        Ui.TimelineItem.Key("approved")[
            Ui.TimelineIndicator.Color(Ui.Color.Green)[Ui.Icon.Name(Ui.IconName.Check).Micro],
            Ui.TimelineContent[Ui.Heading[Who("james_rob"), Said(" approved these changes · 3 days ago")]]
        ];

    private static Component Step(string number, string title, string? detail) =>
        Ui.TimelineItem.Key(number)[
            Ui.TimelineIndicator[number],
            Ui.TimelineContent[
                Ui.Heading[title],
                detail is null ? null : Ui.Text[detail]
            ]
        ];

    private static Component Stage(Ui.TimelineStatus? status, Ui.IconName icon, string title) =>
        Ui.TimelineItem.Key(title).Status(status)[
            Ui.TimelineIndicator[Ui.Icon.Name(icon).Micro],
            Ui.TimelineContent[Ui.Heading[title]]
        ];

    private static Component Coloured(Ui.Color color, Ui.IconName icon, string title) =>
        Ui.TimelineItem.Key(title)[
            Ui.TimelineIndicator.Color(color)[Ui.Icon.Name(icon).Micro],
            Ui.TimelineContent[Ui.Heading[title]]
        ];

    private static Component Bare(Ui.IconName icon, string title, string when) =>
        Ui.TimelineItem.Key(title)[
            Ui.TimelineIndicator.Bare[Ui.Icon.Name(icon).Class("size-6 text-zinc-400")],
            Ui.TimelineContent[Ui.Heading[Who(title), Said(when)]]
        ];

    private static Component Aligned(Ui.TimelineAlign align, string number, string title, string detail) =>
        Ui.TimelineItem.Key(number).Align(align)[
            Ui.TimelineIndicator[number],
            Ui.TimelineContent.Class("gap-1")[Ui.Heading[title], Ui.Text[detail]]
        ];

    // Inside a heading: a name is the heading's own ink, and what was done is inline text beside it.
    private static Component Who(string name) => name;

    private static Component Said(string words) => Ui.Text.Inline()[words];

    private static Component Tag(string label) => Ui.Badge.Sm[label];

    private static Component Face(string initials) =>
        Span.Class("grid size-6 place-items-center rounded-full bg-zinc-500/20 text-[10px] font-medium")[initials];
}
