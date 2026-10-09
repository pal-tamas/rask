using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Components;

namespace Rask.UiTests.Flux.Parity;

/// <summary>Flux UI's <c>components/chart</c> page, example by example, drawn from the data that page drew.</summary>
/// <remarks>
/// <para>
/// Flux's docs make their chart data up on every request — random values, dates counted back from now — so
/// there is no fixed page to compare against. <c>scripts/flux/parity-chart.mjs --measure</c> pins one load: it
/// measures it, and writes the rows each chart drew to <c>ChartParity.data.json</c>, which is what is drawn
/// here. Data, not code. The same script then compares the two drawings' geometry number by number, which
/// <c>parity.mjs</c> (boxes and styles) cannot see.
/// </para>
/// <para>
/// The props are the ones the LIVE examples carry where they differ from the listing beside them: points of
/// radius 5 (the listing says 6), a gutter of <c>4 0 0 0</c> on the dashboard stat, <c>tick-values</c> handed
/// over as a string and so ignored. Colours the examples hand over by class are the page's, under names of
/// its own. Every <see cref="UiChartSvg" /> is told the box Flux measured for it.
/// </para>
/// </remarks>
public sealed partial class ChartParity : FluxParity
{
    // Tailwind's palette, for the classes Flux's examples put on a line, a bar or a legend dot.
    private const string Colours =
        "<style>"
        + ".c-pink{color:oklch(.656 .241 354.308)}.dark .c-pink{color:oklch(.718 .202 349.761)}"
        + ".c-pink-500{color:oklch(.656 .241 354.308)}.c-pink-400{color:oklch(.718 .202 349.761)}"
        + ".c-blue{color:oklch(.623 .214 259.815)}.dark .c-blue{color:oklch(.707 .165 254.624)}"
        + ".c-blue-area{color:color-mix(in oklab,oklch(.882 .059 254.128) 50%,transparent)}"
        + ".dark .c-blue-area{color:color-mix(in oklab,oklch(.707 .165 254.624) 30%,transparent)}"
        + ".c-blue-600{color:oklch(.546 .245 262.881)}.c-blue-500{color:oklch(.623 .214 259.815)}"
        + ".c-blue-400{color:oklch(.707 .165 254.624)}.c-blue-300{color:oklch(.809 .105 251.813)}"
        + ".c-blue-37{color:oklch(.809 .105 251.813)}.dark .c-blue-37{color:oklch(.488 .243 264.376)}"
        + ".c-red-500{color:oklch(.637 .237 25.331)}.c-green-500{color:oklch(.723 .219 149.579)}"
        + ".c-green{color:oklch(.723 .219 149.579)}.dark .c-green{color:oklch(.792 .209 151.711)}"
        + ".c-red{color:oklch(.645 .246 16.439)}.dark .c-red{color:oklch(.712 .194 13.428)}"
        + ".c-sky{color:oklch(.685 .169 237.323)}.dark .c-sky{color:oklch(.746 .16 232.661)}"
        + ".c-sky-line{color:oklch(.901 .058 230.902)}.dark .c-sky-line{color:oklch(.746 .16 232.661)}"
        + ".c-sky-area{color:oklch(.951 .026 236.824)}.dark .c-sky-area{color:color-mix(in oklab,oklch(.746 .16 232.661) 30%,transparent)}"
        + ".c-yesterday{color:oklch(.871 .006 286.286)}.dark .c-yesterday{color:color-mix(in oklab,#fff 40%,transparent)}"
        + ".b-green-400{background:oklch(.792 .209 151.711)}.b-blue-400{background:oklch(.707 .165 254.624)}"
        + ".b-red-400{background:oklch(.704 .191 22.216)}.b-blue-600{background:oklch(.546 .245 262.881)}"
        + ".b-blue-500{background:oklch(.623 .214 259.815)}.b-blue-300{background:oklch(.809 .105 251.813)}"
        + ".b-emerald-500{background:oklch(.696 .17 162.48)}.b-amber-500{background:oklch(.769 .188 70.08)}"
        + ".dark .s-zinc-800{stroke:oklch(.274 .006 286.033)}"
        + ".p-dim{transition:opacity .15s cubic-bezier(.4,0,.2,1)}"
        + ".p-thick{transition:stroke-width .15s cubic-bezier(.4,0,.2,1)}"
        + ".p-scale{transform-origin:center;transition-property:transform,translate,scale,rotate;transition-timing-function:cubic-bezier(.4,0,.2,1);transition-duration:.15s}"
        + ".x-trend{max-width:80px}"
        + ".dark .c-blue-500.l{color:oklch(.707 .165 254.624)}.dark .c-blue-500.p{color:oklch(.809 .105 251.813)}"
        + ".dark .c-red-500.l{color:oklch(.704 .191 22.216)}.dark .c-red-500.p{color:oklch(.808 .114 19.571)}"
        + ".dark .c-green-500.l{color:oklch(.792 .209 151.711)}.dark .c-green-500.p{color:oklch(.871 .15 154.449)}"
        + ".x-3-1{aspect-ratio:3/1}.x-2-1{aspect-ratio:2/1}.x-1-1{aspect-ratio:1}.x-min-20{min-height:20rem}"
        + ".x-flex-8{display:flex;align-items:center;gap:32px}.x-w-36{width:144px;flex-shrink:0;aspect-ratio:1}"
        + ".x-grid-6{display:grid;gap:24px}.x-summary{display:flex;flex-direction:row;gap:48px}"
        + ".x-spark{width:5rem;aspect-ratio:3/1}.x-stat{margin:0 -32px -32px;height:3rem}.x-card{overflow:hidden;min-width:12rem}"
        + "</style>";

    private static readonly JsonElement Pinned = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(RepoRoot.FullPath, "tests", "Rask.Ui.Tests", "Flux", "Parity", "ChartParity.data.json"))).RootElement;

    private static readonly UiChartFormat Usd = new() { Style = Ui.ChartFormatStyle.Currency, Currency = "USD" };

    public override string Page => "chart";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        // Flux's docs are read in en-US, and Intl writes a number as the reader does: so is every example here,
        // whatever machine writes the page.
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            lock (Boxes)
            {
                return [.. Measured()];
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    // Each example drawn as the browser would have it drawn: every drawing told the box Flux measured for its own.
    private static IEnumerable<(string Section, Component Example)> Measured()
    {
        var index = 0;
        Boxes.Clear();

        // Reading an example off Drawn() builds it, which is what writes its boxes down.
        foreach (var (section, example) in Drawn())
        {
            var html = MeasuredCharts.Html(Framed(index, example), [.. Boxes]);
            Boxes.Clear();
            yield return (section, Raw.Value((index == 0 ? Colours : "") + html));
            index++;
        }
    }

    // The boxes of the example being built, in the order its drawings are written.
    private static readonly List<(double Width, double Height)> Boxes = [];

    private static UiChartSvg Svg(double width, double height)
    {
        Boxes.Add((width, height));
        return Ui.ChartSvg;
    }

    private static IEnumerable<(string Section, Component Example)> Drawn()
    {
        yield return ("", Ui.Chart.Value(Rows(0, 0, "date", "visitors")).Class("x-3-1")[
            Svg(606, 202)[
                Ui.ChartLine.Field((Row r) => r.A).Class("c-pink"),
                Ui.ChartAxis.X.Field((Row r) => r.Date)[Ui.ChartAxisLine, Ui.ChartAxisTick],
                Ui.ChartAxis.Y[Ui.ChartAxisGrid, Ui.ChartAxisTick],
                Ui.ChartCursor
            ],
            Ui.ChartTooltip[
                Ui.ChartTooltipHeading.Field((Row r) => r.Date).Format(new() { Year = Ui.ChartFormatPart.Numeric, Month = Ui.ChartFormatPart.Numeric, Day = Ui.ChartFormatPart.Numeric }),
                Ui.ChartTooltipValue.Field((Row r) => r.A).Label("Visitors")
            ]
        ]);

        yield return ("line-chart", Div[
            Ui.Chart.Value(Rows(1, 0, "date", "memory")).Class("x-3-1")[
                Svg(606, 202)[
                    Ui.ChartLine.Field((Row r) => r.A).Class("c-pink-500"),
                    Ui.ChartPoint.Field((Row r) => r.A).Class("c-pink-400"),
                    Ui.ChartAxis.X.Field((Row r) => r.Date)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                    Ui.ChartAxis.Y.Format(new() { Style = Ui.ChartFormatStyle.Unit, Unit = "megabyte" })[Ui.ChartAxisGrid, Ui.ChartAxisTick]
                ]
            ]
        ]);

        yield return ("area-chart", Div[
            Ui.Chart.Value(Rows(2, 0, "date", "stock")).Class("x-3-1")[
                Svg(606, 202)[
                    Ui.ChartLine.Field((Row r) => r.A).Class("c-blue").Curve(Ui.ChartCurve.None),
                    Ui.ChartArea.Field((Row r) => r.A).Class("c-blue-area").Curve(Ui.ChartCurve.None),
                    Ui.ChartAxis.Y.Position(Ui.Position.Right).TickPrefix("$")
                        .Format(new() { Notation = Ui.ChartFormatNotation.Compact, MaximumFractionDigits = 1 })[Ui.ChartAxisGrid, Ui.ChartAxisTick],
                    Ui.ChartAxis.X.Field((Row r) => r.Date)[Ui.ChartAxisTick, Ui.ChartAxisLine]
                ]
            ]
        ]);

        yield return ("multiple-lines", Ui.Chart.Value(Rows(3, 0, "date", "twitter", "facebook", "instagram"))[
            Ui.ChartViewport.Class("x-min-20")[
                Svg(606, 320).Gutter("0")[
                    Ui.ChartLine.Field((Row r) => r.A).Class("c-blue-500 l").Curve(Ui.ChartCurve.None),
                    Ui.ChartPoint.Field((Row r) => r.A).Class("c-blue-500 p").R(5).StrokeWidth(3),
                    Ui.ChartLine.Field((Row r) => r.B).Class("c-red-500 l").Curve(Ui.ChartCurve.None),
                    Ui.ChartPoint.Field((Row r) => r.B).Class("c-red-500 p").R(5).StrokeWidth(3),
                    Ui.ChartLine.Field((Row r) => r.C).Class("c-green-500 l").Curve(Ui.ChartCurve.None),
                    Ui.ChartPoint.Field((Row r) => r.C).Class("c-green-500 p").R(5).StrokeWidth(3),
                    Ui.ChartAxis.X.Field((Row r) => r.Date)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                    Ui.ChartAxis.Y.TickStart(0).TickEnd(1)
                        .Format(new() { Style = Ui.ChartFormatStyle.Percent, MinimumFractionDigits = 0, MaximumFractionDigits = 0 })[Ui.ChartAxisGrid, Ui.ChartAxisTick]
                ]
            ],
            Legends(("Instagram", "b-green-400"), ("Twitter", "b-blue-400"), ("Facebook", "b-red-400"))
        ]);

        yield return ("bar-chart", Ui.Chart.Value(Rows(4, 0, "date", "revenue")).Class("x-3-1")[
            Svg(606, 202)[
                Ui.ChartBar.Field((Row r) => r.A).Class("c-blue-500").Radius("0").Width("85%"),
                Ui.ChartAxis.X.Field((Row r) => r.Date).TickCount(10)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                Ui.ChartAxis.Y.Format(new() { UseGrouping = true }).TickPrefix("$")[Ui.ChartAxisGrid, Ui.ChartAxisTick],
                Ui.ChartCursor.Type(Ui.ChartCursorType.Area)
            ],
            // Flux writes a heading with no format as the row holds it, and its rows hold the date as text.
            Ui.ChartTooltip[
                Ui.ChartTooltipHeading.Field((Row r) => r.Name),
                Ui.ChartTooltipValue.Field((Row r) => r.A).Label("Revenue").Format(new() { UseGrouping = true }).Prefix("$")
            ]
        ]);

        yield return ("bar-chart", Ui.Chart.Value(Rows(5, 0, "month", "tickets")).Class("x-3-1")[
            Svg(606, 202)[
                Ui.ChartBar.Field((Row r) => r.A).Class("c-blue-37"),
                Ui.ChartAxis.X.Field((Row r) => r.Name)[Ui.ChartAxisTick],
                Ui.ChartAxis.Y[Ui.ChartAxisGrid, Ui.ChartAxisTick]
            ],
            Ui.ChartTooltip[Ui.ChartTooltipValue.Field((Row r) => r.A).Label("Tickets")]
        ]);

        yield return ("horizontal-charts", Ui.Chart.Horizontal().Value(Rows(6, 0, "category", "online")).Class("x-2-1")[
            Svg(606, 303)[
                Ui.ChartBar.Field((Row r) => r.A).Class("c-blue-500").Radius("4 0").Width("70%"),
                Ui.ChartAxis.Y.Field((Row r) => r.Name)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                Ui.ChartAxis.X[Ui.ChartAxisGrid, Ui.ChartAxisTick],
                Ui.ChartCursor.Type(Ui.ChartCursorType.Area)
            ],
            Ui.ChartTooltip[
                Ui.ChartTooltipHeading.Field((Row r) => r.Name),
                Ui.ChartTooltipValue.Field((Row r) => r.A).Label("Orders")
            ]
        ]);

        yield return ("grouped-bar-chart", Ui.Chart.Value(Rows(7, 0, "year", "chrome", "firefox", "safari"))[
            Ui.ChartViewport.Class("x-3-1")[
                Svg(606, 202)[
                    Ui.ChartGroup[
                        Ui.ChartBar.Field((Row r) => r.A).Class("c-blue-600"),
                        Ui.ChartBar.Field((Row r) => r.B).Class("c-blue-500"),
                        Ui.ChartBar.Field((Row r) => r.C).Class("c-blue-300")
                    ],
                    Ui.ChartAxis.X.Field((Row r) => r.Name)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                    Ui.ChartAxis.Y[Ui.ChartAxisGrid, Ui.ChartAxisTick]
                ]
            ],
            Legends(("Chrome", "b-blue-600"), ("Firefox", "b-blue-500"), ("Safari", "b-blue-300"))
        ]);

        yield return ("stacked-bar-chart", Ui.Chart.Value(Rows(8, 0, "category", "online", "retail", "wholesale"))[
            Ui.ChartViewport.Class("x-3-1")[
                Svg(606, 202)[
                    Ui.ChartStack.Width("65%")[
                        Ui.ChartBar.Field((Row r) => r.A).Class("c-blue-600"),
                        Ui.ChartBar.Field((Row r) => r.B).Class("c-blue-400"),
                        Ui.ChartBar.Field((Row r) => r.C).Class("c-blue-300").Radius("4 0")
                    ],
                    Ui.ChartAxis.X.Field((Row r) => r.Name)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                    Ui.ChartAxis.Y[Ui.ChartAxisGrid, Ui.ChartAxisTick]
                ]
            ],
            Legends(("Online", "b-blue-600"), ("Retail", "b-blue-400"), ("Wholesale", "b-blue-300"))
        ]);

        yield return ("pie-chart", Column(256, Ui.Chart.Value(Rows(9, 0, "label", "value"))[
            Ui.ChartViewport.Class("x-1-1")[Svg(256, 256)[Pie()]],
            SliceTooltip()
        ]));

        yield return ("donut-chart", Column(256, Ui.Chart.Value(Rows(10, 0, "label", "value"))[
            Ui.ChartViewport.Class("x-1-1")[Svg(256, 256)[Pie().InnerRadius("60%").Radius(4)]],
            SliceTooltip()
        ]));

        yield return ("donut-chart", Column(256, Ui.Chart.Value(Rows(11, 0, "label", "value"))[
            Ui.ChartViewport.Class("x-1-1")[
                Svg(256, 256)[Pie().InnerRadius("60%").Radius(4)],
                Div.Style("pointer-events:none;position:absolute;inset:0;display:grid;place-items:center")[
                    Div.Style("text-align:center")[Ui.Heading.Xl["$128k"], Ui.Text["Revenue"]]
                ]
            ],
            SliceTooltip()
        ]));

        yield return ("slice-colors", Column(384, Ui.Card[
            Ui.Chart.Value(Rows(12, 0, "label", "value")).Class("x-flex-8")[
                Ui.ChartViewport.Class("x-w-36")[
                    Svg(144, 144)[Pie().ColorField((Row r) => r.Color).InnerRadius("60%").Radius(4).Class("s-zinc-800")]
                ],
                Div.Style("flex:1")[
                    Share("Subscriptions", "b-blue-500", "52%"),
                    Share("Services", "b-emerald-500", "28%"),
                    Share("Training", "b-amber-500", "20%")
                ],
                SliceTooltip()
            ]
        ]));

        yield return ("hover-styles", Div.Style("display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:32px")[
            Hovered("Dim others", 0, 180.65625, "p-dim"),
            Hovered("Thicker border", 1, 180.671875, "p-thick"),
            Hovered("Scale active", 2, 180.671875, "p-scale")
        ]);

        yield return ("live-summary", Ui.Card[
            Ui.Chart.Value(Rows(14, 0, "date", "sales", "yesterday")).Class("x-grid-6")[
                Ui.ChartSummary.Class("x-summary")[
                    Div[
                        Ui.Text["Today"],
                        Ui.Heading.Xl.Style("margin-top:8px;font-variant-numeric:tabular-nums")[Ui.ChartSummaryValue.Field((Row r) => r.A).Format(Usd)],
                        Ui.Text.Style("margin-top:8px;font-variant-numeric:tabular-nums")[
                            Ui.ChartSummaryValue.Field((Row r) => r.Date).Format(new() { Hour = Ui.ChartFormatPart.Numeric, Minute = Ui.ChartFormatPart.Numeric, Hour12 = true })
                        ]
                    ],
                    Div[
                        Ui.Text["Yesterday"],
                        Ui.Heading.Lg.Style("margin-top:8px;font-variant-numeric:tabular-nums")[Ui.ChartSummaryValue.Field((Row r) => r.B).Format(Usd)]
                    ]
                ],
                Ui.ChartViewport.Class("x-3-1")[
                    Svg(556, 185.328125)[
                        Ui.ChartLine.Field((Row r) => r.B).Class("c-yesterday").StrokeDasharray("4 4").Curve(Ui.ChartCurve.None),
                        Ui.ChartLine.Field((Row r) => r.A).Class("c-sky").Curve(Ui.ChartCurve.None),
                        Ui.ChartAxis.X.Field((Row r) => r.Date)[Ui.ChartAxisGrid, Ui.ChartAxisTick, Ui.ChartAxisLine],
                        Ui.ChartAxis.Y[Ui.ChartAxisTick],
                        Ui.ChartCursor
                    ]
                ]
            ]
        ]);

        yield return ("sparkline", Ui.Table[
            Ui.TableColumns[Ui.TableColumn["Stock"], Ui.TableColumn["Price"], Ui.TableColumn["Change"], Ui.TableColumn["Trend"]],
            Ui.TableRows[
                Stock("AAPL", "$193.45", "+2.4%", 0, "c-green"),
                Stock("MSFT", "$338.12", "+1.8%", 1, "c-green"),
                Stock("TSLA", "$242.68", "-3.2%", 2, "c-red"),
                Stock("GOOGL", "$129.87", "+0.9%", 3, "c-green")
            ]
        ]);

        yield return ("dashboard-stat", Div.Style("display:flex;justify-content:center")[
            Ui.Card.Class("x-card")[
                Ui.Text["Revenue"],
                Ui.Heading.Xl.Style("margin-top:8px;font-variant-numeric:tabular-nums")["$12,345"],
                Ui.Chart.Value(Numbers(16, 0)).Class("x-stat")[
                    Svg(206, 48).Gutter("4 0 0 0")[
                        Ui.ChartLine.Class("c-sky-line"),
                        Ui.ChartArea.Class("c-sky-area")
                    ]
                ]
            ]
        ]);
    }

    // The 606px column Flux's page sets every example in, and the heading it puts over seven of them.
    private static Component Framed(int index, Component example) =>
        Div.Style("width:606px;margin:0 auto")[
            Titles.TryGetValue(index, out var title) ? Ui.Heading.Style("margin-bottom:24px")[title] : null,
            example
        ];

    private static readonly Dictionary<int, string> Titles = new()
    {
        [1] = "Memory usage",
        [2] = "Stock price",
        [4] = "Revenue",
        [5] = "Support tickets",
        [6] = "Online orders",
        [7] = "Browser usage",
        [8] = "Number of orders",
    };

    private static UiChartPie Pie() => Ui.ChartPie.Field((Row r) => r.A).LabelField((Row r) => r.Name);

    private static Component SliceTooltip() =>
        Ui.ChartTooltip[
            Ui.ChartTooltipValue.LabelField((Row r) => r.Name).Field((Row r) => r.A).Suffix("%")[Ui.ChartTooltipIndicator]
        ];

    private static Component Legends(params (string Label, string Dot)[] entries) =>
        Div.Style("display:flex;justify-content:center;gap:16px;padding-top:16px")[
            entries.Select(entry => Ui.ChartLegend.Key(entry.Label).Label(entry.Label)[Ui.ChartLegendIndicator.Class(entry.Dot)])
        ];

    private static Component Share(string label, string dot, string share) =>
        Div.Style("display:flex;align-items:center;justify-content:space-between")[
            Ui.ChartLegend.Label(label)[Ui.ChartLegendIndicator.Class(dot)],
            Ui.Text.Sm.Style("font-variant-numeric:tabular-nums")[share]
        ];

    private static Component Hovered(string title, int chart, double box, string treatment) =>
        Div[
            Ui.Text.Style("margin-bottom:12px;text-align:center;font-weight:500")[title],
            Ui.Chart.Value(Rows(13, chart, "label", "value"))[
                Ui.ChartViewport.Class("x-1-1")[Svg(box, box)[Pie().Class(treatment)]]
            ]
        ];

    private static Component Stock(string symbol, string price, string change, int chart, string colour) =>
        Ui.TableRow.Key(symbol)[
            Ui.TableCell[symbol],
            Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[price],
            Ui.TableCell[
                Ui.Badge.Sm.Inset(Ui.Inset.Top | Ui.Inset.Bottom).Color(change[0] == '-' ? Ui.Color.Rose : Ui.Color.Green)[change]
            ],
            Ui.TableCell.Class("x-trend")[
                Ui.Chart.Value(Numbers(15, chart)).Class("x-spark")[
                    Svg(80, 26.65625).Gutter("0")[Ui.ChartLine.Class(colour)]
                ]
            ]
        ];

    private static Component Column(int width, Component chart) =>
        Div.Style(string.Create(CultureInfo.InvariantCulture, $"width:{width}px;margin:0 auto"))[chart];

    private sealed record Row(DateTime Date, string Name, double A, double B, double C, Ui.Color Color);

    private static List<Row> Rows(int example, int chart, string name, string a, string? b = null, string? c = null) =>
        [.. Pinned[example].GetProperty("charts")[chart].EnumerateArray().Select(row => new Row(
            name == "date" ? FluxDate(row.GetProperty(name).GetString()!) : default,
            row.GetProperty(name).ToString(),
            row.GetProperty(a).GetDouble(),
            b is null ? 0 : row.GetProperty(b).GetDouble(),
            c is null ? 0 : row.GetProperty(c).GetDouble(),
            row.TryGetProperty("color", out var colour) ? Enum.Parse<Ui.Color>(colour.GetString()!, ignoreCase: true) : Ui.Color.Zinc))];

    private static double[] Numbers(int example, int chart) =>
        [.. Pinned[example].GetProperty("charts")[chart].EnumerateArray().Select(value => value.GetDouble())];

    // A date as Flux's chart reads one: the wall-clock parts it names, in no zone — and the digits after the
    // seconds as that many milliseconds, so PHP's six-digit ".427709" is seven minutes, not 0.43 seconds.
    private static DateTime FluxDate(string text)
    {
        var parts = FluxDatePattern().Match(text);
        int Part(int group) => parts.Groups[group].Success ? int.Parse(parts.Groups[group].Value, CultureInfo.InvariantCulture) : 0;
        return new DateTime(Part(1), Part(2), Part(3), Part(4), Part(5), Part(6), DateTimeKind.Unspecified).AddMilliseconds(Part(7));
    }

    [GeneratedRegex(@"^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?)?")]
    private static partial Regex FluxDatePattern();
}
