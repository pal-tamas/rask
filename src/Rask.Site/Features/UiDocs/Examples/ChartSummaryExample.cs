namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ChartSummaryExample : Component
{
    private static readonly UiChartFormat Dollars = new() { Style = Ui.ChartFormatStyle.Currency, Currency = "USD" };

    // Yesterday's figure is today's, nudged by the hour: worked out, so the drawing is the same on every visit.
    private static readonly Hour[] Today =
    [
        .. new[] { 299, 598, 1247, 1896, 2145, 2394, 2643, 2892, 3041, 3190, 3239 }
            .Select((sales, hour) => new Hour(new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Unspecified).AddHours(hour), sales, sales + (hour % 3 * 150) - 100)),
    ];

    protected override Component? Render() =>
        Ui.Card[
            Ui.Chart.Value(Today).Class("grid gap-6")[
                Ui.ChartSummary.Class("flex gap-12")[
                    Div[
                        Ui.Text["Today"],
                        Ui.Heading.Xl.Class("mt-2 tabular-nums")[Ui.ChartSummaryValue.Field((Hour h) => h.Sales).Format(Dollars)],
                        Ui.Text.Class("mt-2 tabular-nums")[
                            Ui.ChartSummaryValue.Field((Hour h) => h.At)
                                .Format(new() { Hour = Ui.ChartFormatPart.Numeric, Minute = Ui.ChartFormatPart.Numeric, Hour12 = true })
                        ]
                    ],
                    Div[
                        Ui.Text["Yesterday"],
                        Ui.Heading.Lg.Class("mt-2 tabular-nums")[Ui.ChartSummaryValue.Field((Hour h) => h.Yesterday).Format(Dollars)]
                    ]
                ],
                Ui.ChartViewport.Class("aspect-3/1")[
                    Ui.ChartSvg[
                        Ui.ChartLine.Field((Hour h) => h.Yesterday).Class("text-zinc-300 dark:text-white/40").StrokeDasharray("4 4").Curve(Ui.ChartCurve.None),
                        Ui.ChartLine.Field((Hour h) => h.Sales).Class("text-sky-500 dark:text-sky-400").Curve(Ui.ChartCurve.None),
                        Ui.ChartAxis.X.Field((Hour h) => h.At)[Ui.ChartAxisGrid, Ui.ChartAxisTick, Ui.ChartAxisLine],
                        Ui.ChartAxis.Y[Ui.ChartAxisTick],
                        Ui.ChartCursor
                    ]
                ]
            ]
        ];

    private sealed record Hour(DateTime At, decimal Sales, decimal Yesterday);
}
