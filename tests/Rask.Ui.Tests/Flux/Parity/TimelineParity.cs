using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/timeline</c>, example for example.
/// </summary>
/// <remarks>
///     What an item SAYS on Flux's page is other components, and the heading, the text, the small badge, the
///     callout and its buttons are the real ones, with the page's own words. The avatars, the reaction button
///     and the composer of the comment thread are not rebuilt yet: each is a stand-in of the size Flux's
///     measures, marked <c>data-parity-skip</c> — held to its place and its size and not looked into.
/// </remarks>
public sealed partial class TimelineParity : FluxParity
{
    // What an app's own Tailwind build emits for the classes these examples hand to Class: the kit's sheet
    // holds only what the kit writes. The comment thread's (`bg-zinc-50 dark:bg-zinc-800 border …`, `p-3 …`,
    // `border-t …`, `space-y-1`) are stated under names of this page's own: unlayered, a `.border` or a
    // `.bg-white` here would repaint the callout's buttons. Flux's docs default a border to gray-200.
    private const string AppUtilities =
        "<style>.size-6{width:1.5rem;height:1.5rem}.text-zinc-400{color:oklch(70.5% .015 286.067)}.gap-1{gap:.25rem}"
        + ".parity-thread{border:1px solid oklch(92.8% .006 264.531);border-radius:.75rem;overflow:hidden;background-color:oklch(98.5% 0 0)}"
        + ".parity-thread:where(.dark,.dark *){background-color:oklch(27.4% .006 286.033);border-color:oklch(37% .013 285.805)}"
        + ".parity-said{padding:.75rem;background-color:oklch(98.5% 0 0)}"
        + ".parity-said:where(.dark,.dark *){background-color:oklch(27.4% .006 286.033)}"
        + ".parity-rule{border-top:1px solid oklch(92% .004 286.32)}"
        + ".parity-rule:where(.dark,.dark *){border-color:oklch(37% .013 285.805)}"
        + ".parity-reply{padding:.75rem;background-color:#fff}"
        + ".parity-reply:where(.dark,.dark *){background-color:oklch(21% .006 285.885)}"
        + ".parity-below{margin-bottom:.25rem}"
        + ".\\[--ui-timeline-item-gap\\:1\\.5rem\\]{--ui-timeline-item-gap:1.5rem}"
        + ".\\[--ui-timeline-item-gap\\:3rem\\]{--ui-timeline-item-gap:3rem}"
        + ".\\[--ui-timeline-content-gap\\:1rem\\]{--ui-timeline-content-gap:1rem}"
        + ".\\[\\&_\\[data-ui-timeline-baseline\\]\\]\\:text-2xl [data-ui-timeline-baseline]{font-size:1.5rem;line-height:2rem}"
        + ".parity-comment{margin-bottom:.25rem;color:oklch(27.4% .006 286.033)}.parity-comment:where(.dark,.dark *){color:#fff}"
        + "</style>";

    public override string Page => "timeline";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Preview(
            Raw.Value(AppUtilities),
            Ui.Timeline[
                Requested(),
                Tagged(),
                Approved()
            ]));

        yield return ("large", Preview(Ui.Timeline.Lg[
            Step("1", "Submit", "Complete the form and provide all necessary assets."),
            Step("2", "Review", "Verify your data and approve the final draft for accuracy."),
            Step("3", "Publish", "Finalize your settings and push your content live.")
        ]));

        yield return ("horizontal", Preview(Ui.Timeline.Horizontal()[
            Ui.TimelineItem[Mark(Ui.IconName.CreditCard), Ui.TimelineContent[Ui.Heading["Order confirmed"]]],
            Ui.TimelineItem[Mark(Ui.IconName.Truck), Ui.TimelineContent[Ui.Heading["On its way"]]],
            Ui.TimelineItem[Mark(Ui.IconName.Home), Ui.TimelineContent[Ui.Heading["Delivered"]]]
        ]));

        yield return ("status", Preview(Ui.Timeline.Horizontal()[
            Ui.TimelineItem.Complete[Mark(Ui.IconName.CreditCard), Ui.TimelineContent[Ui.Heading["Order confirmed"]]],
            Ui.TimelineItem.Current[Mark(Ui.IconName.Truck), Ui.TimelineContent[Ui.Heading["On its way"]]],
            Ui.TimelineItem.Incomplete[Mark(Ui.IconName.Home), Ui.TimelineContent[Ui.Heading["Delivered"]]]
        ]));

        yield return ("indicator-color", Preview(Ui.Timeline[
            Coloured(Ui.Color.Red, Ui.IconName.XMark, "Build failed"),
            Coloured(Ui.Color.Amber, Ui.IconName.ExclamationTriangle, "Warning issued"),
            Coloured(Ui.Color.Green, Ui.IconName.Check, "Deployed successfully")
        ]));

        yield return ("bare-indicator", Preview(Ui.Timeline[
            Bare(Ui.IconName.DocumentText, "Draft created ", "· 2 days ago"),
            Bare(Ui.IconName.PencilSquare, "Edits made ", "· 1 day ago"),
            Bare(Ui.IconName.PaperAirplane, "Published ", "· just now")
        ]));

        yield return ("block-item", Preview(Ui.Timeline[
            Requested(),
            Ui.TimelineItem[
                Ui.TimelineBlock[
                    Ui.Callout.Secondary.Actions([Ui.Button["View message ->"], Ui.Button.Ghost["Reply"]])[
                        Ui.CalloutHeading["james_rob", Ui.Text["replied to your message · 4 days ago"]]
                    ]
                ]
            ],
            Tagged(),
            Approved()
        ]));

        // A comment and a reply box: each row an avatar (24px, a stand-in) and what is said — the comment's
        // heading and text over the reaction button (a stand-in), and the composer (a stand-in of its height).
        // The card's look and the rule between the rows are the example's own classes.
        yield return ("block-subgrid", Preview(Ui.Timeline[
            Requested(),
            Ui.TimelineItem[
                Ui.TimelineBlock.Class("parity-thread")[
                    Ui.TimelineSubgrid.Class("parity-said")[Words("width:24px;height:24px"), Comment()],
                    Div.Class("parity-rule"),
                    Ui.TimelineSubgrid.Class("parity-reply")[Words("width:24px;height:24px"), Words("height:102px")]
                ]
            ],
            Tagged(),
            Approved()
        ]));

        // `gap-1` on each content and the wider item gap are the docs page's own classes for this example.
        yield return ("alignment", Preview(Ui.Timeline.Class("[--ui-timeline-item-gap:1.5rem]")[
            Ui.TimelineItem.Start[Ui.TimelineIndicator["1"], Said(Ui.Heading["Start align"], Ui.Text["Align indicator and content to the top/start"])],
            Ui.TimelineItem.Baseline[Ui.TimelineIndicator["2"], Said(Ui.Heading["Baseline align"], Ui.Text["Align indicator and content to the baseline of the first line of text"])],
            Ui.TimelineItem.Center[Ui.TimelineIndicator["3"], Said(Ui.Heading["Center align"], Ui.Text["Align indicator and content to the center"])],
            Ui.TimelineItem.End[Ui.TimelineIndicator["4"], Said(Ui.Heading["End align"], Ui.Text["Align indicator and content to the bottom/end"])]
        ]));

        // Baseline adjustment: a heading of size xl (32px) and a line of text under it.
        yield return ("alignment", Preview(Ui.Timeline.Lg[
            Ui.TimelineItem.Baseline[Ui.TimelineIndicator["1"], Said(Ui.Heading.Xl["Without adjustment"], Ui.Text["The indicator may not align perfectly with large text"])],
            Ui.TimelineItem.Baseline.Class("[&_[data-ui-timeline-baseline]]:text-2xl")[Ui.TimelineIndicator["2"], Said(Ui.Heading.Xl["With baseline adjustment"], Ui.Text["The indicator aligns precisely with the heading baseline"])]
        ]));

        yield return ("spacing", Preview(Ui.Timeline.Class("[--ui-timeline-item-gap:3rem] [--ui-timeline-content-gap:1rem]")[
            Ui.TimelineItem[Ui.TimelineIndicator["1"], Said(Ui.Heading["Create account"])],
            Ui.TimelineItem[Ui.TimelineIndicator["2"], Said(Ui.Heading["Provide details"])],
            Ui.TimelineItem[Ui.TimelineIndicator["3"], Said(Ui.Heading["Review & confirm"])]
        ]));
    }

    // A preview on Flux's page: the timeline in a 480px box, centred.
    private static Component Preview(params Component[] content) =>
        Div.Style("display:flex;justify-content:center")[Div.Style("min-width:480px")[content]];

    // Stand-in for what is not rebuilt yet (flux:avatar, flux:composer, the reaction button): a box of the size
    // measured on Flux's page.
    private static HTMLDivElement Words(string size) => Div.Data("parity-skip", "").Style(size);

    private static Component Mark(Ui.IconName icon) => Ui.TimelineIndicator[Ui.Icon.Name(icon).Micro];

    private static Component Requested() =>
        Ui.TimelineItem[
            Mark(Ui.IconName.Eye),
            Ui.TimelineContent[
                Ui.Heading["curtisss ", Ui.Text.Inline()["requested a review from"], " james_rob ", Ui.Text.Inline()["· 4 days ago"]]
            ]
        ];

    private static Component Tagged() =>
        Ui.TimelineItem[
            Mark(Ui.IconName.Tag),
            Ui.TimelineContent[Ui.Heading["curtisss ", Ui.Text.Inline()["added tag"], " ", Ui.Badge.Sm["feature"]]]
        ];

    private static Component Approved() =>
        Ui.TimelineItem[
            Ui.TimelineIndicator.Color(Ui.Color.Green)[Ui.Icon.Name(Ui.IconName.Check).Micro],
            Ui.TimelineContent[Ui.Heading["james_rob ", Ui.Text.Inline()["approved these changes · 3 days ago"]]]
        ];

    private static Component Step(string number, string title, string detail) =>
        Ui.TimelineItem[Ui.TimelineIndicator[number], Ui.TimelineContent[Ui.Heading[title], Ui.Text[detail]]];

    private static Component Coloured(Ui.Color color, Ui.IconName icon, string title) =>
        Ui.TimelineItem[Ui.TimelineIndicator.Color(color)[Ui.Icon.Name(icon).Micro], Ui.TimelineContent[Ui.Heading[title]]];

    private static Component Bare(Ui.IconName icon, string title, string when) =>
        Ui.TimelineItem[
            Ui.TimelineIndicator.Bare[Ui.Icon.Name(icon).Class("size-6 text-zinc-400")],
            Ui.TimelineContent[Ui.Heading[title, Ui.Text.Inline()[when]]]
        ];

    // `space-y-1` around the comment and `mt-2` on the reaction button are the example's own classes.
    private static Component Comment() =>
        Div[
            Ui.Heading.Class("parity-below")["james_rob ", Ui.Text.Inline().Class("font-medium")["commented · 4 days ago"]],
            Ui.Text.Class("parity-comment")["Contrast slider goes a bit wild at the top end..."],
            Words("height:24px;margin-top:8px")
        ];

    private static Component Said(params Component[] lines) => Ui.TimelineContent.Class("gap-1")[lines];
}
