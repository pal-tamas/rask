namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ChartSliceColorsExample : Component
{
    private static readonly Share[] Shares =
    [
        new("Subscriptions", 52, Ui.Color.Blue, "bg-blue-500"),
        new("Services", 28, Ui.Color.Emerald, "bg-emerald-500"),
        new("Training", 20, Ui.Color.Amber, "bg-amber-500"),
    ];

    protected override Component? Render() =>
        Ui.Card.Class("max-w-md")[
            Ui.Chart.Value(Shares).Class("flex items-center gap-8")[
                Ui.ChartViewport.Class("aspect-square w-36 shrink-0")[
                    Ui.ChartSvg[
                        Ui.ChartPie.Field((Share s) => s.Value).LabelField((Share s) => s.Label).ColorField((Share s) => s.Color)
                            .InnerRadius("60%").Radius(4).Class("dark:stroke-zinc-800")
                    ]
                ],
                Div.Class("flex-1")[
                    Shares.Select(share => Div.Key(share.Label).Class("flex items-center justify-between")[
                        Ui.ChartLegend.Label(share.Label)[Ui.ChartLegendIndicator.Class(share.Dot)],
                        Ui.Text.Sm.Class("tabular-nums")[$"{share.Value}%"]
                    ])
                ]
            ]
        ];

    private sealed record Share(string Label, int Value, Ui.Color Color, string Dot);
}
