namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ChartPieExample : Component
{
    private static readonly Share[] Shares = [new("Subscriptions", 52), new("Services", 28), new("Training", 20)];

    protected override Component? Render() =>
        Div.Class("grid grid-cols-3 gap-6")[
            Ui.Chart.Key("pie").Value(Shares)[
                Ui.ChartViewport.Class("aspect-square")[
                    Ui.ChartSvg[Ui.ChartPie.Field((Share s) => s.Value).LabelField((Share s) => s.Label)]
                ]
            ],
            Ui.Chart.Key("donut").Value(Shares)[
                Ui.ChartViewport.Class("aspect-square")[
                    Ui.ChartSvg[Ui.ChartPie.Field((Share s) => s.Value).LabelField((Share s) => s.Label).InnerRadius("60%").Radius(4)]
                ]
            ],
            Ui.Chart.Key("centre").Value(Shares)[
                Ui.ChartViewport.Class("aspect-square")[
                    Ui.ChartSvg[Ui.ChartPie.Field((Share s) => s.Value).LabelField((Share s) => s.Label).InnerRadius("60%").Radius(4)],
                    Div.Class("pointer-events-none absolute inset-0 grid place-items-center")[
                        Div.Class("text-center")[Ui.Heading.Lg["$128k"], Ui.Text["Revenue"]]
                    ]
                ]
            ]
        ];

    private sealed record Share(string Label, int Value);
}
