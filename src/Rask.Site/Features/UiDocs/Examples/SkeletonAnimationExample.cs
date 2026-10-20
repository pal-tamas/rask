namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SkeletonAnimationExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex max-w-sm flex-col gap-6")[
            Div[Div.Class("mb-2 text-sm font-medium")["None"], Ui.Skeleton],
            Div[Div.Class("mb-2 text-sm font-medium")["Shimmer"], Ui.Skeleton.Shimmer],
            Div[Div.Class("mb-2 text-sm font-medium")["Pulse"], Ui.Skeleton.Pulse]
        ];
}
