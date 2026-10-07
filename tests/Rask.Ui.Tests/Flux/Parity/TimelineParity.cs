using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/timeline</c>, example for example.
/// </summary>
/// <remarks>
///     What an item SAYS on Flux's page is other components — headings, text, a badge, a callout, avatars, a
///     composer — none of which is on this branch. Each is a stand-in of the size Flux's measures, marked
///     <c>data-parity-skip</c>: held to its place and its size, which is what the timeline's layout decides,
///     and not looked into. Everything the timeline draws — the grid, the lines, the indicators and the
///     icons in them — is compared in full.
/// </remarks>
public sealed partial class TimelineParity : FluxParity
{
    // What an app's own Tailwind build emits for the classes these examples hand to Class: the kit's sheet
    // holds only what the kit writes. Flux's docs default a border to gray-200, which `border` says here.
    private const string AppUtilities =
        "<style>.size-6{width:1.5rem;height:1.5rem}.text-zinc-400{color:oklch(70.5% .015 286.067)}.gap-1{gap:.25rem}"
        + ".p-3{padding:.75rem}.rounded-xl{border-radius:.75rem}.overflow-hidden{overflow:hidden}"
        + ".border{border:1px solid oklch(92.8% .006 264.531)}.border-t{border-top:1px solid}"
        + ".border-zinc-200{border-color:oklch(92% .004 286.32)}"
        + ".bg-zinc-50{background-color:oklch(98.5% 0 0)}.bg-white{background-color:#fff}"
        + ".dark\\:bg-zinc-800:where(.dark,.dark *){background-color:oklch(27.4% .006 286.033)}"
        + ".dark\\:bg-zinc-900:where(.dark,.dark *){background-color:oklch(21% .006 285.885)}"
        + ".dark\\:border-zinc-700:where(.dark,.dark *){border-color:oklch(37% .013 285.805)}"
        + ".\\[--ui-timeline-item-gap\\:1\\.5rem\\]{--ui-timeline-item-gap:1.5rem}"
        + ".\\[--ui-timeline-item-gap\\:3rem\\]{--ui-timeline-item-gap:3rem}"
        + ".\\[--ui-timeline-content-gap\\:1rem\\]{--ui-timeline-content-gap:1rem}"
        + ".\\[\\&_\\[data-ui-timeline-baseline\\]\\]\\:text-2xl [data-ui-timeline-baseline]{font-size:1.5rem;line-height:2rem}"
        + "</style>";

    public override string Page => "timeline";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Preview(
            Raw.Value(AppUtilities),
            Ui.Timeline[
                Event(Ui.IconName.Eye, 20),
                Event(Ui.IconName.Tag, 24),
                Approved()
            ]));

        yield return ("large", Preview(Ui.Timeline.Lg[Step("1"), Step("2"), Step("3")]));

        yield return ("horizontal", Preview(Ui.Timeline.Horizontal()[
            Ui.TimelineItem[Mark(Ui.IconName.CreditCard), Ui.TimelineContent[Words("width:110.65625px;height:20px")]],
            Ui.TimelineItem[Mark(Ui.IconName.Truck), Ui.TimelineContent[Words("width:69.71875px;height:20px")]],
            Ui.TimelineItem[Mark(Ui.IconName.Home), Ui.TimelineContent[Words("width:63.421875px;height:20px")]]
        ]));

        yield return ("status", Preview(Ui.Timeline.Horizontal()[
            Ui.TimelineItem.Complete[Mark(Ui.IconName.CreditCard), Ui.TimelineContent[Words("width:110.65625px;height:20px")]],
            Ui.TimelineItem.Current[Mark(Ui.IconName.Truck), Ui.TimelineContent[Words("width:69.71875px;height:20px")]],
            Ui.TimelineItem.Incomplete[Mark(Ui.IconName.Home), Ui.TimelineContent[Words("width:63.421875px;height:20px")]]
        ]));

        yield return ("indicator-color", Preview(Ui.Timeline[
            Coloured(Ui.Color.Red, Ui.IconName.XMark),
            Coloured(Ui.Color.Amber, Ui.IconName.ExclamationTriangle),
            Coloured(Ui.Color.Green, Ui.IconName.Check)
        ]));

        yield return ("bare-indicator", Preview(Ui.Timeline[
            Bare(Ui.IconName.DocumentText),
            Bare(Ui.IconName.PencilSquare),
            Bare(Ui.IconName.PaperAirplane)
        ]));

        // The block holds a flux:callout: a stand-in of its 110px.
        yield return ("block-item", Preview(Ui.Timeline[
            Event(Ui.IconName.Eye, 20),
            Ui.TimelineItem[Ui.TimelineBlock[Words("height:110px")]],
            Event(Ui.IconName.Tag, 24),
            Approved()
        ]));

        // A comment and a reply box: each row an avatar (24px, a stand-in) and what is said (a stand-in of its
        // height). The card's look and the rule between the rows are the example's own classes.
        yield return ("block-subgrid", Preview(Ui.Timeline[
            Event(Ui.IconName.Eye, 20),
            Ui.TimelineItem[
                Ui.TimelineBlock.Class("bg-zinc-50 dark:bg-zinc-800 border dark:border-zinc-700 rounded-xl overflow-hidden")[
                    Ui.TimelineSubgrid.Class("p-3 bg-zinc-50 dark:bg-zinc-800")[Words("width:24px;height:24px"), Words("height:76px")],
                    Div.Class("border-t border-zinc-200 dark:border-zinc-700"),
                    Ui.TimelineSubgrid.Class("p-3 bg-white dark:bg-zinc-900")[Words("width:24px;height:24px"), Words("height:102px")]
                ]
            ],
            Event(Ui.IconName.Tag, 24),
            Approved()
        ]));

        // `gap-1` on each content and the wider item gap are the docs page's own classes for this example.
        yield return ("alignment", Preview(Ui.Timeline.Class("[--ui-timeline-item-gap:1.5rem]")[
            Ui.TimelineItem.Start[Ui.TimelineIndicator["1"], Said(Line("Start align", 14, 20), Line("Align indicator and content", 14, 20))],
            Ui.TimelineItem.Baseline[Ui.TimelineIndicator["2"], Said(Line("Baseline align", 14, 20), Line("Align indicator and content", 14, 20))],
            Ui.TimelineItem.Center[Ui.TimelineIndicator["3"], Said(Line("Center align", 14, 20), Line("Align indicator and content", 14, 20))],
            Ui.TimelineItem.End[Ui.TimelineIndicator["4"], Said(Line("End align", 14, 20), Line("Align indicator and content", 14, 20))]
        ]));

        // Baseline adjustment: a heading of size xl (32px) and a line of text under it.
        yield return ("alignment", Preview(Ui.Timeline.Lg[
            Ui.TimelineItem.Baseline[Ui.TimelineIndicator["1"], Said(Line("Without adjustment", 24, 32), Line("The indicator may not align", 14, 20))],
            Ui.TimelineItem.Baseline.Class("[&_[data-ui-timeline-baseline]]:text-2xl")[Ui.TimelineIndicator["2"], Said(Line("With baseline adjustment", 24, 32), Line("The indicator aligns precisely", 14, 20))]
        ]));

        yield return ("spacing", Preview(Ui.Timeline.Class("[--ui-timeline-item-gap:3rem] [--ui-timeline-content-gap:1rem]")[
            Ui.TimelineItem[Ui.TimelineIndicator["1"], Said(Words(Px(20)))],
            Ui.TimelineItem[Ui.TimelineIndicator["2"], Said(Words(Px(20)))],
            Ui.TimelineItem[Ui.TimelineIndicator["3"], Said(Words(Px(20)))]
        ]));
    }

    // A preview on Flux's page: the timeline in a 480px box, centred.
    private static Component Preview(params Component[] content) =>
        Div.Style("display:flex;justify-content:center")[Div.Style("min-width:480px")[content]];

    // Stand-in for what an item says (flux:heading, flux:text, flux:badge, flux:callout, flux:avatar,
    // flux:composer): a box of the size measured on Flux's page.
    private static HTMLDivElement Words(string size) => Div.Data("parity-skip", "").Style(size);

    private static Component Mark(Ui.IconName icon) => Ui.TimelineIndicator[Ui.Icon.Name(icon).Micro];

    private static Component Event(Ui.IconName icon, int height) =>
        Ui.TimelineItem[Mark(icon), Ui.TimelineContent[Words(Px(height))]];

    private static Component Approved() =>
        Ui.TimelineItem[
            Ui.TimelineIndicator.Color(Ui.Color.Green)[Ui.Icon.Name(Ui.IconName.Check).Micro],
            Ui.TimelineContent[Words(Px(20))]
        ];

    private static Component Step(string number) =>
        Ui.TimelineItem[Ui.TimelineIndicator[number], Ui.TimelineContent[Words(Px(20)), Words(Px(20))]];

    private static Component Coloured(Ui.Color color, Ui.IconName icon) =>
        Ui.TimelineItem[Ui.TimelineIndicator.Color(color)[Ui.Icon.Name(icon).Micro], Ui.TimelineContent[Words(Px(20))]];

    private static Component Bare(Ui.IconName icon) =>
        Ui.TimelineItem[
            Ui.TimelineIndicator.Bare[Ui.Icon.Name(icon).Class("size-6 text-zinc-400")],
            Ui.TimelineContent[Words(Px(20))]
        ];

    private static Component Said(params Component[] lines) => Ui.TimelineContent.Class("gap-1")[lines];

    // A stand-in that keeps a first line of text: baseline alignment meets the content's first baseline, and
    // an empty box has none. The font size and line height are the heading's or the text's on Flux's page.
    private static Component Line(string words, int fontSize, int lineHeight) =>
        Words(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"height:{lineHeight}px;font-size:{fontSize}px;line-height:{lineHeight}px"))[words];

    private static string Px(int height) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"height:{height}px");
}
