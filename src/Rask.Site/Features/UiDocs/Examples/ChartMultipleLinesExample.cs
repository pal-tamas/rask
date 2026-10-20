namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ChartMultipleLinesExample : Component
{
    private static readonly Social[] Networks =
    [
        new(new(2021, 6, 1), 0.26, 0.21, 0.31), new(new(2022, 6, 1), 0.19, 0.14, 0.35), new(new(2023, 6, 1), 0.22, 0.09, 0.59),
        new(new(2024, 6, 1), 0.11, 0.16, 0.43), new(new(2025, 6, 1), 0.18, 0.13, 0.36),
    ];

    protected override Component? Render() =>
        Ui.Chart.Value(Networks)[
            Ui.ChartViewport.Class("aspect-3/1")[
                Ui.ChartSvg[
                    Ui.ChartLine.Field((Social s) => s.Twitter).Class("text-blue-500").Curve(Ui.ChartCurve.None),
                    Ui.ChartPoint.Field((Social s) => s.Twitter).Class("text-blue-500").R(6).StrokeWidth(3),
                    Ui.ChartLine.Field((Social s) => s.Facebook).Class("text-red-500").Curve(Ui.ChartCurve.None),
                    Ui.ChartPoint.Field((Social s) => s.Facebook).Class("text-red-500").R(6).StrokeWidth(3),
                    Ui.ChartLine.Field((Social s) => s.Instagram).Class("text-green-500").Curve(Ui.ChartCurve.None),
                    Ui.ChartPoint.Field((Social s) => s.Instagram).Class("text-green-500").R(6).StrokeWidth(3),
                    Ui.ChartAxis.X.Field((Social s) => s.Day)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                    Ui.ChartAxis.Y.TickStart(0).TickEnd(1)
                        .Format(new() { Style = Ui.ChartFormatStyle.Percent, MaximumFractionDigits = 0 })[Ui.ChartAxisGrid, Ui.ChartAxisTick]
                ]
            ],
            Div.Class("flex justify-center gap-4 pt-4")[
                Ui.ChartLegend.Key("instagram").Label("Instagram")[Ui.ChartLegendIndicator.Class("bg-green-400")],
                Ui.ChartLegend.Key("twitter").Label("Twitter")[Ui.ChartLegendIndicator.Class("bg-blue-400")],
                Ui.ChartLegend.Key("facebook").Label("Facebook")[Ui.ChartLegendIndicator.Class("bg-red-400")]
            ]
        ];

    private sealed record Social(DateOnly Day, double Twitter, double Facebook, double Instagram);
}
