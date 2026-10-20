namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SkeletonChartExample : Component
{
    protected override Component? Render() =>
        Ui.Card[
            Div.Class("flex flex-col gap-6")[
                Div.Class("flex gap-12")[
                    Div[
                        Ui.Text["Today"],
                        Ui.Heading.Xl.Class("mt-2 tabular-nums")["$---"],
                        Ui.Text.Class("mt-2 tabular-nums")["-:-- PM"]
                    ],
                    Div[
                        Ui.Text["Yesterday"],
                        Ui.Heading.Lg.Class("mt-2 tabular-nums")["$---"]
                    ]
                ],
                Ui.Skeleton.Shimmer.Class("aspect-[4/1] size-full rounded-lg")
            ]
        ];
}
