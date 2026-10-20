using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's timeline and its parts as a C# component: each example with its own code, and its reference.</summary>
[Route("timeline")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiTimelinePage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Timeline.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Timeline)
            .Lead("Events or steps in order, down the page or across it, with a line drawn between their indicators. "
                  + "Each item holds an indicator (an icon, a number, a word) and the content beside it. A list, so "
                  + "there is nothing to hold in C#.")
            .Api(UiApi.Timeline)[
            UiDocsExample.Key("ui-timeline").Heading("Timeline").File("TimelineExample.cs").Demo(TimelineExample).Testid("ui-timeline")
                .Blurb("Events down the page, each beside its indicator."),
            UiDocsExample.Key("ui-timeline-large").Heading("Large").File("TimelineLargeExample.cs").Demo(TimelineLargeExample).Testid("ui-timeline-large")
                .Blurb("48px indicators on a 2px line with wider gaps, for numbered steps."),
            UiDocsExample.Key("ui-timeline-horizontal").Heading("Horizontal").File("TimelineHorizontalExample.cs").Demo(TimelineHorizontalExample).Testid("ui-timeline-horizontal")
                .Blurb("Indicators in a row, the content under each."),
            UiDocsExample.Key("ui-timeline-status").Heading("Status").File("TimelineStatusExample.cs").Demo(TimelineStatusExample).Testid("ui-timeline-status")
                .Blurb("Complete is filled with a dark line on to the next, current a dark ring, incomplete a faint "
                       + "ring with its content dimmed."),
            UiDocsExample.Key("ui-timeline-color").Heading("Indicator color").File("TimelineColorExample.cs").Demo(TimelineColorExample).Testid("ui-timeline-color")
                .Blurb("A coloured circle in any of the seventeen hues."),
            UiDocsExample.Key("ui-timeline-bare").Heading("Bare indicator").File("TimelineBareExample.cs").Demo(TimelineBareExample).Testid("ui-timeline-bare")
                .Blurb("No circle and no size: a larger icon standing on its own."),
            UiDocsExample.Key("ui-timeline-block").Heading("Block item").File("TimelineBlockExample.cs").Demo(TimelineBlockExample).Testid("ui-timeline-block")
                .Blurb("A callout as an item, spanning the timeline's width with the line running into it and out again."),
            UiDocsExample.Key("ui-timeline-subgrid").Heading("Block subgrid").File("TimelineSubgridExample.cs").Demo(TimelineSubgridExample).Testid("ui-timeline-subgrid")
                .Blurb("A comment thread whose avatars line up with the indicators around it."),
            UiDocsExample.Key("ui-timeline-align").Heading("Alignment").File("TimelineAlignExample.cs").Demo(TimelineAlignExample).Testid("ui-timeline-align")
                .Blurb("Where the content sits beside its indicator: start, baseline, centre or end."),
            UiDocsExample.Key("ui-timeline-baseline").Heading("Baseline adjustment").File("TimelineBaselineExample.cs").Demo(TimelineBaselineExample).Testid("ui-timeline-baseline")
                .Blurb("Give the indicator's hidden first line the heading's size, and the two baselines meet."),
            UiDocsExample.Key("ui-timeline-spacing").Heading("Spacing").File("TimelineSpacingExample.cs").Demo(TimelineSpacingExample).Testid("ui-timeline-spacing")
                .Blurb("Two CSS variables: the gap between items, and between an indicator and its content."),
            UiTimelineBehaviour
        ];
}
