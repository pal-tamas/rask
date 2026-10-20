namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SkeletonLinesExample : Component
{
    protected override Component? Render() =>
        Ui.SkeletonGroup.Shimmer.Class("max-w-sm")[
            Ui.SkeletonLine.Class("mb-2 w-1/4"),
            Ui.SkeletonLine,
            Ui.SkeletonLine,
            Ui.SkeletonLine.Class("w-3/4")
        ];
}
