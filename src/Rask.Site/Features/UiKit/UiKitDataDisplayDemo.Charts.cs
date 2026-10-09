using Rask.Core;

namespace Rask.Site.Features.UiKit;

/// <summary>Flux's chart examples, one after another, as the showcase's Chart section.</summary>
public sealed partial class UiKitDataDisplayDemo
{
    private sealed record Visit(DateOnly Day, int Visitors);

    private sealed record Reading(DateTime At, int Memory, double Stock);

    private sealed record Social(DateOnly Day, double Twitter, double Facebook, double Instagram);

    private sealed record Sale(string Category, int Online, int Retail, int Wholesale);

    private sealed record Share(string Label, int Value, Ui.Color Color);

    private sealed record Hour(DateTime At, decimal Sales, decimal Yesterday);

    // The numbers on Flux's own page, where it shows them.
    private static readonly Visit[] Visits =
    [
        .. new[] { 171, 227, 269, 223, 249, 251, 300, 246, 162, 115, 176, 144, 184, 269, 259, 267 }
            .Select((visitors, day) => new Visit(new DateOnly(2026, 1, 1).AddDays(day), visitors)),
    ];

    private static readonly Reading[] Readings =
    [
        .. new[] { 268, 243, 236, 201, 228, 251, 231, 267, 202, 206, 280, 268, 265, 215, 254, 237, 253, 266, 257, 297, 269 }
            .Select((memory, minute) => new Reading(new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Unspecified).AddMinutes(minute), memory, 200 + (minute * minute * 7))),
    ];

    private static readonly Social[] Networks =
    [
        new(new(2021, 6, 1), 0.26, 0.21, 0.31), new(new(2022, 6, 1), 0.19, 0.14, 0.35), new(new(2023, 6, 1), 0.22, 0.09, 0.59),
        new(new(2024, 6, 1), 0.11, 0.16, 0.43), new(new(2025, 6, 1), 0.18, 0.13, 0.36),
    ];

    private static readonly Sale[] Orders =
    [
        new("Beauty", 342, 215, 108), new("Books", 356, 256, 215), new("Clothes", 432, 284, 176), new("Garden", 398, 190, 143),
        new("Pets", 487, 301, 122), new("Sports", 548, 327, 190), new("Toys", 643, 412, 236),
    ];

    private static readonly Share[] Shares =
    [
        new("Subscriptions", 52, Ui.Color.Blue), new("Services", 28, Ui.Color.Emerald), new("Training", 20, Ui.Color.Amber),
    ];

    private static readonly Hour[] Today =
    [
        .. new[] { 299, 598, 1247, 1896, 2145, 2394, 2643, 2892, 3041, 3190, 3239 }
            .Select((sales, hour) => new Hour(new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Unspecified).AddHours(hour), sales, sales + (hour % 3 * 150) - 100)),
    ];

    private static readonly UiChartFormat Dollars = new() { Style = Ui.ChartFormatStyle.Currency, Currency = "USD" };

    private static Component ChartSection() =>
        Section(
            "Chart",
            "Flux's chart, part by part: a Ui.ChartSvg holding lines, areas, points, bars and axes, with a tooltip, "
            + "a summary or a legend beside it. Drawn as SVG during the render, with no chart library — the scales, "
            + "the ticks and every path are computed in C#, for the box the chart turns out to have: resize the "
            + "window and it is drawn again. Move the pointer over one: the cursor, the tooltip, the summary and a "
            + "pie's slice follow it in the browser, with no round trip.",
            Div.Data(Testid("ui-chart")).Class("grid gap-10 lg:grid-cols-2")[
                Example("Line", LineChart()),
                Example("Line with points", PointChart()),
                Example("Area", AreaChart()),
                Example("Multiple lines", MultipleLines()),
                Example("Bars", BarChart()),
                Example("Horizontal", HorizontalChart()),
                Example("Grouped bars", GroupedBars()),
                Example("Stacked bars", StackedBars()),
                Example("Pie and donut", Pies()),
                Example("Slice colours", SliceColours()),
                Example("Live summary", LiveSummary()),
                Example("Sparkline and dashboard stat", Small())
            ]);

    private static Component Example(string title, Component chart) =>
        Div.Key(title)[Ui.Heading.Class("mb-4")[title], chart];

    private static Component LineChart() =>
        Ui.Chart.Value(Visits).Class("aspect-3/1")[
            Ui.ChartSvg[
                Ui.ChartLine.Field((Visit v) => v.Visitors).Class("text-pink-500 dark:text-pink-400"),
                Ui.ChartAxis.X.Field((Visit v) => v.Day)[Ui.ChartAxisLine, Ui.ChartAxisTick],
                Ui.ChartAxis.Y[Ui.ChartAxisGrid, Ui.ChartAxisTick],
                Ui.ChartCursor
            ],
            Ui.ChartTooltip[
                Ui.ChartTooltipHeading.Field((Visit v) => v.Day),
                Ui.ChartTooltipValue.Field((Visit v) => v.Visitors).Label("Visitors")
            ]
        ];

    private static Component PointChart() =>
        Ui.Chart.Value(Readings).Class("aspect-3/1")[
            Ui.ChartSvg[
                Ui.ChartLine.Field((Reading r) => r.Memory).Class("text-pink-500"),
                Ui.ChartPoint.Field((Reading r) => r.Memory).Class("text-pink-400"),
                Ui.ChartAxis.X.Field((Reading r) => r.At)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                Ui.ChartAxis.Y.Format(new() { Style = Ui.ChartFormatStyle.Unit, Unit = "megabyte" })[Ui.ChartAxisGrid, Ui.ChartAxisTick]
            ]
        ];

    private static Component AreaChart() =>
        Ui.Chart.Value(Readings).Class("aspect-3/1")[
            Ui.ChartSvg[
                Ui.ChartLine.Field((Reading r) => r.Stock).Class("text-blue-500 dark:text-blue-400").Curve(Ui.ChartCurve.None),
                Ui.ChartArea.Field((Reading r) => r.Stock).Class("text-blue-200/50 dark:text-blue-400/30").Curve(Ui.ChartCurve.None),
                Ui.ChartAxis.Y.Position(Ui.Position.Right).TickPrefix("$")
                    .Format(new() { Notation = Ui.ChartFormatNotation.Compact, MaximumFractionDigits = 1 })[Ui.ChartAxisGrid, Ui.ChartAxisTick],
                Ui.ChartAxis.X.Field((Reading r) => r.At)[Ui.ChartAxisTick, Ui.ChartAxisLine]
            ]
        ];

    private static Component MultipleLines() =>
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
            Legend(("Instagram", "bg-green-400"), ("Twitter", "bg-blue-400"), ("Facebook", "bg-red-400"))
        ];

    private static Component BarChart() =>
        Ui.Chart.Value(Visits).Class("aspect-3/1")[
            Ui.ChartSvg[
                Ui.ChartBar.Field((Visit v) => v.Visitors).Class("text-blue-500").Radius("0").Width("85%"),
                Ui.ChartAxis.X.Field((Visit v) => v.Day).TickCount(8)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                Ui.ChartAxis.Y.Format(new() { UseGrouping = true })[Ui.ChartAxisGrid, Ui.ChartAxisTick],
                Ui.ChartCursor.Type(Ui.ChartCursorType.Area)
            ],
            Ui.ChartTooltip[
                Ui.ChartTooltipHeading.Field((Visit v) => v.Day),
                Ui.ChartTooltipValue.Field((Visit v) => v.Visitors).Label("Visitors")
            ]
        ];

    private static Component HorizontalChart() =>
        Ui.Chart.Horizontal().Value(Orders).Class("aspect-2/1")[
            Ui.ChartSvg[
                Ui.ChartBar.Field((Sale s) => s.Online).Class("text-blue-500").Radius("4 0").Width("70%"),
                Ui.ChartAxis.Y.Field((Sale s) => s.Category)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                Ui.ChartAxis.X[Ui.ChartAxisGrid, Ui.ChartAxisTick],
                Ui.ChartCursor.Type(Ui.ChartCursorType.Area)
            ],
            Ui.ChartTooltip[
                Ui.ChartTooltipHeading.Field((Sale s) => s.Category),
                Ui.ChartTooltipValue.Field((Sale s) => s.Online).Label("Orders")
            ]
        ];

    private static Component GroupedBars() =>
        Ui.Chart.Value(Orders)[
            Ui.ChartViewport.Class("aspect-3/1")[
                Ui.ChartSvg[
                    Ui.ChartGroup[
                        Ui.ChartBar.Field((Sale s) => s.Online).Class("text-blue-600"),
                        Ui.ChartBar.Field((Sale s) => s.Retail).Class("text-blue-500"),
                        Ui.ChartBar.Field((Sale s) => s.Wholesale).Class("text-blue-300")
                    ],
                    Ui.ChartAxis.X.Field((Sale s) => s.Category)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                    Ui.ChartAxis.Y[Ui.ChartAxisGrid, Ui.ChartAxisTick]
                ]
            ],
            Legend(("Online", "bg-blue-600"), ("Retail", "bg-blue-500"), ("Wholesale", "bg-blue-300"))
        ];

    private static Component StackedBars() =>
        Ui.Chart.Value(Orders)[
            Ui.ChartViewport.Class("aspect-3/1")[
                Ui.ChartSvg[
                    Ui.ChartStack.Width("65%")[
                        Ui.ChartBar.Field((Sale s) => s.Online).Class("text-blue-600"),
                        Ui.ChartBar.Field((Sale s) => s.Retail).Class("text-blue-400"),
                        Ui.ChartBar.Field((Sale s) => s.Wholesale).Class("text-blue-300").Radius("4 0")
                    ],
                    Ui.ChartAxis.X.Field((Sale s) => s.Category)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                    Ui.ChartAxis.Y[Ui.ChartAxisGrid, Ui.ChartAxisTick]
                ]
            ],
            Legend(("Online", "bg-blue-600"), ("Retail", "bg-blue-400"), ("Wholesale", "bg-blue-300"))
        ];

    private static Component Pies() =>
        Div.Class("grid grid-cols-3 gap-6")[
            Slices(Ui.ChartPie.Field((Share s) => s.Value).LabelField((Share s) => s.Label), centre: null),
            Slices(Ui.ChartPie.Field((Share s) => s.Value).LabelField((Share s) => s.Label).InnerRadius("60%").Radius(4), centre: null),
            Slices(
                Ui.ChartPie.Field((Share s) => s.Value).LabelField((Share s) => s.Label).InnerRadius("60%").Radius(4),
                Div.Class("pointer-events-none absolute inset-0 grid place-items-center")[
                    Div.Class("text-center")[Ui.Heading.Lg["$128k"], Ui.Text["Revenue"]]
                ])
        ];

    private static Component Slices(UiChartPie pie, Component? centre) =>
        Ui.Chart.Value(Shares)[
            Ui.ChartViewport.Class("aspect-square")[Ui.ChartSvg[pie], centre]
        ];

    private static Component SliceColours() =>
        Ui.Card[
            Ui.Chart.Value(Shares).Class("flex items-center gap-8")[
                Ui.ChartViewport.Class("aspect-square w-36 shrink-0")[
                    Ui.ChartSvg[
                        Ui.ChartPie.Field((Share s) => s.Value).LabelField((Share s) => s.Label).ColorField((Share s) => s.Color)
                            .InnerRadius("60%").Radius(4).Class("dark:stroke-zinc-800")
                    ]
                ],
                Div.Class("flex-1")[
                    Shares.Select(share => Div.Key(share.Label).Class("flex items-center justify-between")[
                        Ui.ChartLegend.Label(share.Label)[Ui.ChartLegendIndicator.Class(Dot(share.Color))],
                        Ui.Text.Sm.Class("tabular-nums")[$"{share.Value}%"]
                    ])
                ]
            ]
        ];

    private static string Dot(Ui.Color color) => color switch
    {
        Ui.Color.Blue => "bg-blue-500",
        Ui.Color.Emerald => "bg-emerald-500",
        _ => "bg-amber-500",
    };

    private static Component LiveSummary() =>
        Ui.Card[
            Ui.Chart.Value(Today).Class("grid gap-6")[
                Ui.ChartSummary.Class("flex gap-12")[
                    Div[
                        Ui.Text["Today"],
                        Ui.Heading.Xl.Class("mt-2 tabular-nums")[Ui.ChartSummaryValue.Field((Hour h) => h.Sales).Format(Dollars)],
                        Ui.Text.Class("mt-2 tabular-nums")[
                            Ui.ChartSummaryValue.Field((Hour h) => h.At).Format(new() { Hour = Ui.ChartFormatPart.Numeric, Minute = Ui.ChartFormatPart.Numeric, Hour12 = true })
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

    private static Component Small() =>
        Div.Class("flex items-start gap-8")[
            Ui.Chart.Value([15, 18, 16, 19, 22, 25, 28, 25, 29, 28, 32, 35]).Class("aspect-3/1 w-20")[
                Ui.ChartSvg.Gutter("0")[Ui.ChartLine.Class("text-green-500 dark:text-green-400")]
            ],
            Ui.Card.Class("min-w-48 overflow-hidden")[
                Ui.Text["Revenue"],
                Ui.Heading.Xl.Class("mt-2 tabular-nums")["$12,345"],
                Ui.Chart.Value([10, 12, 11, 13, 15, 14, 16, 18, 17, 19, 21, 20]).Class("-mx-6 -mb-6 h-12")[
                    Ui.ChartSvg.Gutter("4 0 0 0")[
                        Ui.ChartLine.Class("text-sky-200 dark:text-sky-400"),
                        Ui.ChartArea.Class("text-sky-100 dark:text-sky-400/30")
                    ]
                ]
            ]
        ];

    private static Component Legend(params (string Label, string Dot)[] entries) =>
        Div.Class("flex justify-center gap-4 pt-4")[
            entries.Select(entry => Ui.ChartLegend.Key(entry.Label).Label(entry.Label)[Ui.ChartLegendIndicator.Class(entry.Dot)])
        ];
}
