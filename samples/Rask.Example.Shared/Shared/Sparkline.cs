using System.Globalization;
using System.Text;

namespace Rask.Example.Shared;

// A reusable, stateless SVG line chart — built entirely from the core typed SVG components
// (Svg/Polyline/Polygon/Line/Circle/SvgText/SvgTitle), so it renders identically on the Server
// and WASM transports with zero JavaScript. The chart is part of the normal render output, so a
// live re-render simply re-emits an updated <svg>; there is no post-render hook or canvas redraw.
//
// LiveTicker composes this to draw its price history. Values are mapped into a fixed 600×160
// coordinate space and the surrounding <svg> stretches to fill its container
// (Width/Height "100%", PreserveAspectRatio "none"), so the consumer controls the rendered size
// via CSS — same approach Chart.js used with maintainAspectRatio:false.
public sealed class Sparkline : Component
{
    // Fixed internal coordinate space; the <svg> scales to its container box.
    private const double W = 600;
    private const double H = 160;
    private const double PadX = 8;
    private const double PadTop = 12;
    private const double PadBottom = 12;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // The data series, in chronological order. Non-nullable reference + no initializer ⇒ the
    // generator emits this as the first required positional factory parameter. Sparkline has no
    // DI constructor, so we also mark it `required` for language-level enforcement (RASK001's
    // suggestion) — no CS8618 suppression needed, and no RASK002 since there's a parameterless ctor.
    public required IReadOnlyList<double> Values { get; set; }

    // Optional overrides; the defaults live at the read sites below so they stay out of the
    // generated factory (an initializer would exclude the property entirely).
    public string? Stroke { get; set; }
    public string? AreaFill { get; set; }

    // Forwarded onto the root <svg> so consumers can size/style it (Sparkline is a composite
    // Component, not an Element, so it doesn't inherit Element.Class).
    public string? Class { get; set; }

    // Numeric format for the min/max/last axis labels (e.g. "0.0'%'" for percentages). The
    // default reproduces the original money formatting, so existing callers are unaffected.
    public string? ValueFormat { get; set; }

    private string StrokeColor => Stroke ?? "#0d6efd";
    private string AreaColor => AreaFill ?? "rgba(13, 110, 253, 0.15)";
    private string LabelFormat => ValueFormat ?? "$#,##0.00";

    protected override Component? Render()
    {
        var values = Values;
        var n = values.Count;

        // Nothing to plot — render an empty, labelled frame so the box doesn't collapse.
        if (n == 0)
        {
            return Frame()[
                SvgText(Num(W / 2), Num(H / 2), TextAnchor: "middle",
                    DominantBaseline: "middle", FontFamily: "sans-serif", FontSize: "12",
                    Fill: "#adb5bd")["No data"]
            ];
        }

        var min = values[0];
        var max = values[0];
        for (var i = 1; i < n; i++)
        {
            if (values[i] < min)
            {
                min = values[i];
            }

            if (values[i] > max)
            {
                max = values[i];
            }
        }

        var plotW = W - (2 * PadX);
        var plotH = H - PadTop - PadBottom;
        var baseY = H - PadBottom;

        double X(int i) => n == 1 ? PadX + (plotW / 2) : PadX + (plotW * i / (n - 1));

        // Higher value ⇒ smaller y (SVG y grows downward). Flat series ⇒ centre the line.
        double Y(double v) => max <= min ? PadTop + (plotH / 2) : PadTop + (plotH * (1 - ((v - min) / (max - min))));

        var sb = new StringBuilder();
        for (var i = 0; i < n; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(Num(X(i))).Append(',').Append(Num(Y(values[i])));
        }

        var points = sb.ToString();

        var lastX = X(n - 1);
        var lastY = Y(values[n - 1]);

        var children = new List<Component>
        {
            // Light horizontal gridlines (top / middle / baseline).
            Line(Num(PadX), Num(PadTop), Num(W - PadX), Num(PadTop),
                Stroke: "rgba(0,0,0,0.05)", StrokeWidth: "1"),
            Line(Num(PadX), Num(PadTop + (plotH / 2)), Num(W - PadX), Num(PadTop + (plotH / 2)),
                Stroke: "rgba(0,0,0,0.05)", StrokeWidth: "1"),
            Line(Num(PadX), Num(baseY), Num(W - PadX), Num(baseY),
                Stroke: "rgba(0,0,0,0.05)", StrokeWidth: "1"),

            // Filled area under the line: the trend points closed down to the baseline.
            Polygon($"{Num(X(0))},{Num(baseY)} {points} {Num(lastX)},{Num(baseY)}",
                Fill: AreaColor, Stroke: "none")
        };

        // A single point has no line to draw; skip the polyline and just mark the point.
        if (n > 1)
        {
            children.Add(Polyline(points, Fill: "none", Stroke: StrokeColor,
                StrokeWidth: "2", StrokeLinejoin: "round", StrokeLinecap: "round"));
        }

        // Min / max value labels on the y-axis.
        children.Add(SvgText(Num(PadX + 2), Num(PadTop + 10), FontFamily: "sans-serif",
            FontSize: "11", Fill: "#6c757d")[Label(max)]);
        children.Add(SvgText(Num(PadX + 2), Num(baseY - 3), FontFamily: "sans-serif",
            FontSize: "11", Fill: "#6c757d")[Label(min)]);

        // Last-point marker carrying a native SVG <title> tooltip (no JS).
        children.Add(Circle(Num(lastX), Num(lastY), "3.5", Fill: StrokeColor)[
            SvgTitle()[Label(values[n - 1])]
        ]);

        return Frame()[children];
    }

    private Component Frame() =>
        Svg("100%", "100%", $"0 0 {Num(W)} {Num(H)}",
            "none", Class: Class);

    private static string Num(double v) => v.ToString("0.##", Inv);

    private string Label(double v) => v.ToString(LabelFormat, Inv);
}
