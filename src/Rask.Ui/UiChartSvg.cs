using System.Globalization;
using System.Text;

namespace Rask;

/// <summary>
/// The drawing itself: put the lines, areas, points, bars, axes and the cursor inside. Flux UI's <c>chart.svg</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Width" /> and <see cref="Height" /> are the box the chart is drawn for, in pixels. Flux measures
/// its element in the browser and draws again whenever it changes size; a chart drawn in C# is told its box, and
/// the SVG then scales as a whole to whatever box it is given. State the size the chart is usually shown at, so
/// its labels are their own size there.
/// </para>
/// </remarks>
public sealed partial class UiChartSvg : Component
{
    private static readonly UiChartData Nothing = new UiChartRows<double>([]);

    /// <summary>
    /// The space kept clear inside each edge, as one to four numbers in the order of a CSS padding:
    /// <c>"12 0 12 8"</c>. 8 all round unless this says otherwise; <c>"0"</c> for a sparkline.
    /// </summary>
    public string? Gutter { get; set; }

    /// <summary>The width the chart is drawn for, in pixels. 600 unless this says otherwise.</summary>
    public double? Width { get; set; }

    /// <summary>The height the chart is drawn for, in pixels. 200 unless this says otherwise.</summary>
    public double? Height { get; set; }

    public string? Class { get; set; }

    // What it draws is the chart's rows, which arrive through the context rather than as a prop of its own.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var scope = Context.Get<UiChartScope>();
        var data = scope?.Data ?? Nothing;
        var width = Width ?? 600;
        var height = Height ?? 200;
        var parts = new UiChartParts(Children, data);
        var svg = new StringBuilder(4096)
            .Append("<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" viewBox=\"0 0 ").Number(width).Append(' ').Number(height)
            .Append("\" class=\"").Append(System.Net.WebUtility.HtmlEncode(UiClass.Compose("absolute inset-0 block size-full overflow-hidden", Class))).Append("\">");
        if (parts.Pie is { } pie)
        {
            UiChartPieDrawing.Write(svg, pie, data, width, height, UiChartPlot.Gutter(Gutter));
            return Raw.Value(svg.Append("<rect fill=\"none\"></rect></svg>").ToString());
        }

        var plot = new UiChartPlot(parts, data, scope?.Horizontal == true, width, height, Gutter);
        svg.Append(new UiChartDrawing(plot, parts, data).Write());
        svg.Append("<rect fill=\"none\" pointer-events=\"all\" width=\"").Number(width).Append("\" height=\"").Number(height).Append("\" x=\"0\" y=\"0\"></rect></svg>");
        return RaskFragment([Raw.Value(svg.ToString()), UiChartHover.For(plot, parts, data, scope?.Tooltip)]);
    }
}
