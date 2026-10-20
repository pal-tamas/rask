namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SkeletonExample : Component
{
    protected override Component? Render() =>
        Ui.SkeletonGroup.Shimmer.Class("flex max-w-64 items-center gap-4")[
            Ui.Skeleton.Class("size-10 rounded-full"),
            Div.Class("flex-1")[
                Ui.SkeletonLine,
                Ui.SkeletonLine.Class("w-1/2")
            ]
        ];
}
