using System.Globalization;

namespace Rask.Ui;

/// <summary>
/// A line, area or bar chart of rows, drawn as SVG on the server.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's <c>chart</c>, and like <see cref="UiDataGrid{T,TKey}" /> the series arrive through a factory whose
/// parameter is the chart — which is what gives each lambda its row type:
/// <c>UiChart.Data(sales).Label("Revenue")[c =&gt; [c.X(s =&gt; s.Month), c.Line(s =&gt; s.Revenue)]]</c>. Mix
/// <see cref="Line(Func{T,double})" />, <see cref="Area(Func{T,double})" /> and <see cref="Bar(Func{T,double})" />
/// series on one set of axes; each takes <c>Label</c> and <c>Tone</c>, and a series with no tone takes the next
/// colour in turn.
/// </para>
/// <para>
/// <b>No script at all.</b> The plot is an SVG in a fixed coordinate space that stretches to the box — size it with
/// a height class — and its strokes keep their width while it does. The axis labels are HTML beside it rather than
/// SVG text inside it, so they never stretch with the plot. Hovering a column shows its values in CSS.
/// </para>
/// <para>
/// A picture of numbers is no use to somebody who cannot see it, so the figure is named by <see cref="Label" /> and
/// carries a visually hidden table of every value it draws, with the series as its columns. The drawing itself is
/// hidden from assistive technology.
/// </para>
/// </remarks>
/// <typeparam name="T">The row type.</typeparam>
public sealed partial class UiChart<T> : Component
{
    // The plot's own coordinate space. Arbitrary, since preserveAspectRatio="none" stretches it to the box; wide and
    // short because charts are.
    private const double Width = 1000;
    private const double Height = 300;

    private static readonly UiTone[] Palette =
    [
        UiTone.Primary, UiTone.Secondary, UiTone.Accent, UiTone.Info, UiTone.Success, UiTone.Warning, UiTone.Error,
    ];

    // What each series and the axis read from a row, keyed by the component the factory handed back. Refilled on every
    // render, since the factory builds its series again each time.
    private readonly Dictionary<UiChartSeries, Func<T, double>> _values = new(ReferenceEqualityComparer.Instance);
    private Func<T, object?>? _axis;
    private UiChartAxis? _axisComponent;
    private Func<UiChart<T>, IEnumerable<Component?>>? _factory;

    /// <summary>The rows, one point per row, in the order they are drawn.</summary>
    public required IEnumerable<T> Data { get; set; }

    /// <summary>What the chart shows — its accessible name, and the caption of the table read in its place.</summary>
    public required string Label { get; set; }

    /// <summary>The lowest value on the value axis. Zero, or the lowest value drawn if that is below zero.</summary>
    public double? Min { get; set; }

    /// <summary>The highest value on the value axis. A round number above the highest value drawn.</summary>
    public double? Max { get; set; }

    /// <summary>
    ///     How values are written on the axis and in the tooltips — a .NET numeric format, <c>"C0"</c>,
    ///     <c>"N1"</c>, <c>"P0"</c>. Whole numbers, or two places for a small scale, unless this says otherwise.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    ///     Whether the series are named in a legend above the plot. Shown when there is more than one series.
    /// </summary>
    public bool? Legend { get; set; }

    /// <summary>Whether horizontal lines mark the value axis's steps. On unless this says otherwise.</summary>
    public bool? Grid { get; set; }

    public string? Class { get; set; }

    // The series and the axis are read off the factory, which runs every render.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <summary>The series and the axis, built given the chart they belong to — see the remarks on the type.</summary>
    /// <param name="series">Builds the series.</param>
    public Component this[Func<UiChart<T>, IEnumerable<Component?>> series]
    {
        get
        {
            _factory = series;
            return this;
        }
    }

    /// <summary>What each row is called along the bottom axis, and in its tooltip and table row.</summary>
    public UiChartAxis X(Func<T, object?> label)
    {
        var axis = UiChartAxis;
        _axis = label;
        _axisComponent = axis;
        return axis;
    }

    /// <summary>A line through each row's value.</summary>
    public UiChartSeries Line(Func<T, double> value) => Series(UiChartKind.Line, value);

    /// <inheritdoc cref="Line(Func{T,double})" />
    public UiChartSeries Line(Func<T, decimal> value) => Series(UiChartKind.Line, r => (double)value(r));

    /// <inheritdoc cref="Line(Func{T,double})" />
    public UiChartSeries Line(Func<T, int> value) => Series(UiChartKind.Line, r => value(r));

    /// <inheritdoc cref="Line(Func{T,double})" />
    public UiChartSeries Line(Func<T, long> value) => Series(UiChartKind.Line, r => value(r));

    /// <summary>A line through each row's value, with the area under it filled.</summary>
    public UiChartSeries Area(Func<T, double> value) => Series(UiChartKind.Area, value);

    /// <inheritdoc cref="Area(Func{T,double})" />
    public UiChartSeries Area(Func<T, decimal> value) => Series(UiChartKind.Area, r => (double)value(r));

    /// <inheritdoc cref="Area(Func{T,double})" />
    public UiChartSeries Area(Func<T, int> value) => Series(UiChartKind.Area, r => value(r));

    /// <inheritdoc cref="Area(Func{T,double})" />
    public UiChartSeries Area(Func<T, long> value) => Series(UiChartKind.Area, r => value(r));

    /// <summary>A bar for each row's value. Several bar series stand side by side.</summary>
    public UiChartSeries Bar(Func<T, double> value) => Series(UiChartKind.Bar, value);

    /// <inheritdoc cref="Bar(Func{T,double})" />
    public UiChartSeries Bar(Func<T, decimal> value) => Series(UiChartKind.Bar, r => (double)value(r));

    /// <inheritdoc cref="Bar(Func{T,double})" />
    public UiChartSeries Bar(Func<T, int> value) => Series(UiChartKind.Bar, r => value(r));

    /// <inheritdoc cref="Bar(Func{T,double})" />
    public UiChartSeries Bar(Func<T, long> value) => Series(UiChartKind.Bar, r => value(r));

    private UiChartSeries Series(UiChartKind kind, Func<T, double> value)
    {
        var series = UiChartSeries.Kind(kind);
        _values[series] = value;
        return series;
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var rows = Data as IReadOnlyList<T> ?? [.. Data];
        var (series, axis) = Resolve();

        var labels = rows.Select((row, i) => Text(axis, row, i)).ToList();
        var values = series.Select(s => rows.Select(s.Value).ToArray()).ToList();
        var scale = UiChartScale.For(values.SelectMany(v => v), Min, Max);
        var format = Format ?? (scale.Step >= 1 ? "N0" : "0.##");

        return Figure
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            // m-0: a <figure> carries the browser's own 40px side margins, and the kit puts no outer margin on anything.
            // Each row says self-stretch rather than trusting the figure's alignment: a host stylesheet that centres a
            // figure's children — the site's own demo frame does — would otherwise shrink the plot to its axis labels.
            .Class(UiClass.Compose("m-0 flex min-h-48 flex-col gap-3", Class))[
            Legend ?? series.Count > 1 ? LegendRow(series) : null,
            Div.Class("flex min-h-0 grow gap-2 self-stretch")[
                Div.Class("flex w-12 shrink-0 flex-col justify-between text-right text-xs text-ui-muted")
                    .Aria("hidden", "true")[
                    scale.Ticks.Reverse().Select((tick, i) => Span.Key(i)[Number(tick, format)])
                ],
                Div.Class("relative grow")[
                    Plot(series, values, rows.Count, scale),
                    Hover(series, values, labels, format)
                ]
            ],
            XLabels(labels),
            DataTable(series, values, labels, format)
        ];
    }

    private (List<Resolved> Series, Func<T, object?>? Axis) Resolve()
    {
        _values.Clear();
        _axis = null;
        _axisComponent = null;
        if (_factory is null)
        {
            return ([], null);
        }

        var series = new List<Resolved>();
        Func<T, object?>? axis = null;
        foreach (var child in _factory(this))
        {
            // Anything that is not one of the chart's own is ignored, which is what lets one arm of a conditional be
            // null — `showCost ? c.Area(s => s.Cost) : null`.
            if (child is UiChartAxis a && ReferenceEquals(a, _axisComponent))
            {
                axis = _axis;
            }
            else if (child is UiChartSeries s && _values.TryGetValue(s, out var value))
            {
                series.Add(new Resolved(
                    s.Kind ?? UiChartKind.Line,
                    value,
                    s.Tone ?? Palette[series.Count % Palette.Length],
                    s.Label ?? "Series " + (series.Count + 1).ToString(CultureInfo.CurrentCulture)));
            }
        }

        return (series, axis);
    }

    private static string Text(Func<T, object?>? axis, T row, int index)
    {
        var value = axis?.Invoke(row);
        return value switch
        {
            null => (index + 1).ToString(CultureInfo.CurrentCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static string Number(double value, string format) => value.ToString(format, CultureInfo.CurrentCulture);

    private static string Coord(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private Component LegendRow(List<Resolved> series) =>
        Div.Class("flex flex-wrap gap-x-4 gap-y-1 self-stretch text-sm").Aria("hidden", "true")[
            series.Select((s, i) => Span.Key(i).Class("inline-flex items-center gap-2")[
                Span.Class(UiClass.Compose("size-2.5 shrink-0 rounded-full", UiClassNames.ChartSwatch(s.Tone))),
                s.Label
            ])
        ];

    private Component Plot(List<Resolved> series, List<double[]> values, int count, UiChartScale scale)
    {
        // Every kind is placed in BANDS, one per row, with a line's points at the middle of theirs — so a line over a
        // bar chart passes through the tops of the bars rather than beside them, and the hover columns and the labels
        // along the bottom line up with both.
        var band = count == 0 ? Width : Width / count;
        double Y(double v) => Height - ((v - scale.Min) / (scale.Max - scale.Min) * Height);
        double Mid(int i) => (i + 0.5) * band;
        var baseline = Y(Math.Clamp(0, scale.Min, scale.Max));

        var bars = series.Where(s => s.Kind == UiChartKind.Bar).ToList();
        var barWidth = bars.Count == 0 ? 0 : band * 0.7 / bars.Count;

        return Svg
            .ViewBox("0 0 " + Coord(Width) + " " + Coord(Height))
            .Attributes(("preserveAspectRatio", "none"), ("aria-hidden", "true"), ("focusable", "false"))
            .Class("absolute inset-0 size-full overflow-visible")[
            Grid == false
                ? null
                : G.Class("stroke-base-300")[
                    scale.Ticks.Select((tick, i) => RaskMarkup.Line.Key(i)
                        .X1("0").X2(Coord(Width)).Y1(Coord(Y(tick))).Y2(Coord(Y(tick)))
                        .Attributes(("vector-effect", "non-scaling-stroke")))
                ],
            series.Select((s, index) =>
            {
                var points = values[index];
                if (s.Kind == UiChartKind.Bar)
                {
                    var slot = bars.IndexOf(s);
                    return (Component)G.Key(index).Class(UiClassNames.ChartFill(s.Tone))[
                        points.Select((v, i) =>
                        {
                            var top = Math.Min(Y(v), baseline);
                            return Rect.Key(i)
                                .X(Coord((i * band) + (band * 0.15) + (slot * barWidth)))
                                .Y(Coord(top))
                                .Width(Coord(Math.Max(barWidth - 2, 1)))
                                .Height(Coord(Math.Abs(baseline - Y(v))));
                        })
                    ];
                }

                if (points.Length == 0)
                {
                    return null;
                }

                var line = string.Concat(points.Select((v, i) =>
                    (i == 0 ? "M" : "L") + Coord(Mid(i)) + " " + Coord(Y(v))));

                return G.Key(index)[
                    s.Kind == UiChartKind.Area
                        ? SvgPath
                            .D(line + "L" + Coord(Mid(points.Length - 1)) + " " + Coord(baseline)
                               + "L" + Coord(Mid(0)) + " " + Coord(baseline) + "Z")
                            .Class(UiClassNames.ChartArea(s.Tone))
                        : null,
                    SvgPath
                        .D(line)
                        .Fill("none")
                        .StrokeWidth("2")
                        .StrokeLinejoin("round")
                        .StrokeLinecap("round")
                        .Class(UiClassNames.ChartStroke(s.Tone))
                        .Attributes(("vector-effect", "non-scaling-stroke"))
                ];
            })
        ];
    }

    // A column per row, the width of its band, that shows the row's values while the pointer is over it. CSS only —
    // `group-hover` — so it works before the runtime boots and costs nothing per point.
    private static Component Hover(List<Resolved> series, List<double[]> values, List<string> labels, string format) =>
        Div.Class("absolute inset-0 flex").Aria("hidden", "true")[
            labels.Select((label, i) => Div.Key(i).Class("group relative h-full grow basis-0")[
                Div.Class("pointer-events-none absolute inset-y-0 left-1/2 hidden w-px bg-base-content/20 group-hover:block"),
                Div.Class(
                    "pointer-events-none absolute bottom-full left-1/2 z-10 mb-2 hidden -translate-x-1/2 "
                    + "whitespace-nowrap rounded-box border border-base-300 bg-base-100 px-3 py-2 text-xs shadow-sm "
                    + "group-hover:block")[
                    Div.Class("mb-1 font-medium")[label],
                    series.Select((s, index) => Div.Key(index).Class("flex items-center gap-2")[
                        Span.Class(UiClass.Compose("size-2 shrink-0 rounded-full", UiClassNames.ChartSwatch(s.Tone))),
                        Span.Class("grow opacity-70")[s.Label],
                        Span.Class("font-medium tabular-nums")[Number(values[index][i], format)]
                    ])
                ]
            ])
        ];

    // Every row labelled when they fit; past a dozen, every few, so they never run into one another. The row still
    // has its name in its tooltip and in the table.
    private static Component XLabels(List<string> labels)
    {
        var every = Math.Max(1, (int)Math.Ceiling(labels.Count / 12.0));
        return Div.Class("ml-14 flex self-stretch text-xs text-ui-muted").Aria("hidden", "true")[
            labels.Select((label, i) => Span.Key(i).Class("grow basis-0 truncate text-center")[
                i % every == 0 ? label : null
            ])
        ];
    }

    private Component DataTable(List<Resolved> series, List<double[]> values, List<string> labels, string format) =>
        Table.Class("sr-only")[
            Caption[Label],
            Thead[
                Tr[
                    Td,
                    series.Select((s, i) => Th.Key(i).Scope("col")[s.Label])
                ]
            ],
            Tbody[
                labels.Select((label, row) => Tr.Key(row)[
                    Th.Scope("row")[label],
                    series.Select((_, i) => Td.Key(i)[Number(values[i][row], format)])
                ])
            ]
        ];

    private sealed record Resolved(UiChartKind Kind, Func<T, double> Value, UiTone Tone, string Label);
}
