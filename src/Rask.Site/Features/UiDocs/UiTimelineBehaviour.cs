namespace Rask.Site.Features.UiDocs;

/// <summary>How a timeline behaves: its indicators, blocks, baseline alignment and spacing.</summary>
internal sealed partial class UiTimelineBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Indicators(),
            Blocks(),
            Baseline(),
            Spacing()
        ];

    private static Component Indicators() =>
        UiDocsTopic.Key("indicators").Heading("Indicators and status").Paragraphs([])[
            UiDocsKeys.Column("Step").Rows([
                ("`Ui.Timeline.Horizontal()`", "Across the page: indicators in a row, the content under each."),
                ("`Ui.Timeline.Lg`", "48px indicators on a 2px line with wider gaps. `Ui.TimelineItem.Lg` enlarges one indicator."),
                ("`.Start` `.Baseline` `.Center` `.End`", "Where the content sits beside its indicator (`Ui.TimelineAlign`, "
                                                          + "centre by default): on the timeline for every item, on an item for itself."),
                ("`.Complete` `.Current` `.Incomplete`", "Progress (`Ui.TimelineStatus`): a filled indicator and a dark line "
                                                         + "on to the next item; a dark ring; a faint ring with the content dimmed. "
                                                         + "`Ui.TimelineIndicator.Status(…)` overrides the item's for the indicator alone."),
                ("`Ui.TimelineIndicator.Color(Ui.Color.Red)`", "A coloured circle, in any of the seventeen hues. A status is drawn instead of a colour."),
                ("`Ui.TimelineIndicator.Bare`", "No circle and no size: a larger icon standing on its own."),
            ])
        ];

    private static UiDocsTopic Blocks() =>
        UiDocsTopic.Key("blocks").Heading("A block instead of an indicator").Paragraphs([
            "`Ui.TimelineBlock` makes an item of a card or a callout, spanning the timeline's width with the line "
            + "running into it and out again. Inside one, `Ui.TimelineSubgrid` puts its first child back in the "
            + "indicators' column and its second in the content's: a comment thread whose avatars line up with the "
            + "events around it.",
        ]);

    private static UiDocsTopic Baseline() =>
        UiDocsTopic.Key("baseline").Heading("Baseline alignment").Paragraphs([
            "Baseline alignment lines the content's first line up with the indicator's text. An indicator holding an "
            + "icon has no text, so every indicator carries a hidden, empty first line (`data-ui-timeline-baseline`) to "
            + "stand in for it. Give that line the content's font size when it is not the default: "
            + "`Ui.TimelineItem.Baseline.Class(\"[&_[data-ui-timeline-baseline]]:text-2xl\")`.",
        ]);

    private static UiDocsTopic Spacing() =>
        UiDocsTopic.Key("spacing").Heading("Spacing").Paragraphs([
            "Spacing is two CSS variables on the timeline: `--ui-timeline-item-gap` between items and "
            + "`--ui-timeline-content-gap` between an indicator and its content, Flux's `--flux-timeline-item-gap` and "
            + "`--flux-timeline-content-gap`.",
        ]);
}
