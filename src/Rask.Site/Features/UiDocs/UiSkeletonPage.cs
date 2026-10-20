using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's skeleton as a C# component: each example with its own code, and its reference.</summary>
[Route("skeleton")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiSkeletonPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Skeleton.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Skeleton)
            .Lead("The shape of what is coming, so the layout does not jump when it arrives. A skeleton is a block the "
                  + "call site sizes and rounds, a line keeps the height of the text it stands in for, and a group "
                  + "animates everything inside it.")
            .Api(UiApi.Skeleton)[
            UiDocsExample.Key("ui-skeleton").Heading("Skeleton").File("SkeletonExample.cs").Demo(SkeletonExample).Testid("ui-skeleton")
                .Blurb("An avatar and two lines, shimmering together."),
            UiDocsExample.Key("ui-skeleton-lines").Heading("Text").File("SkeletonLinesExample.cs").Demo(SkeletonLinesExample).Testid("ui-skeleton-lines")
                .Blurb("Lines for a paragraph, each as wide as a class makes it."),
            UiDocsExample.Key("ui-skeleton-animation").Heading("Animation").File("SkeletonAnimationExample.cs").Demo(SkeletonAnimationExample).Testid("ui-skeleton-animation")
                .Blurb("Still, a shimmer, or a pulse."),
            UiDocsExample.Key("ui-skeleton-table").Heading("Table").File("SkeletonTableExample.cs").Demo(SkeletonTableExample).Testid("ui-skeleton-table")
                .Blurb("A loading placeholder for a data table."),
            UiDocsExample.Key("ui-skeleton-chart").Heading("Chart").File("SkeletonChartExample.cs").Demo(SkeletonChartExample).Testid("ui-skeleton-chart")
                .Blurb("A loading placeholder for a chart card."),
            UiSkeletonBehaviour
        ];
}
