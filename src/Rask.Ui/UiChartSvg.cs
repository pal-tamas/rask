using System.Globalization;
using System.Text;

namespace Rask;

/// <summary>
/// The drawing itself: put the lines, areas, points, bars, axes and the cursor inside. Flux UI's <c>chart.svg</c>.
/// </summary>
/// <remarks>
/// <para>
/// It fills the box its chart (or its <see cref="UiChartViewport" />) gives it, and is drawn for that box: the
/// browser measures it and the chart is drawn again at that size — when it first appears, and whenever the box
/// changes, as Flux's is. A chart that is hidden keeps the drawing it had.
/// </para>
/// <para>
/// The FIRST render happens before any browser has measured anything. <see cref="Width" /> and
/// <see cref="Height" /> say what box to draw for until then — 600 by 200 unless stated — and the SVG is scaled
/// as a whole to the real one meanwhile. Stated as the size the chart is usually shown at, the first drawing is
/// already the right one and nothing is drawn twice.
/// </para>
/// </remarks>
public sealed partial class UiChartSvg : Component
{
    private static readonly UiChartData Nothing = new UiChartRows<double>([]);

    // The box the browser measured, once it has: it outranks the stated one from then on.
    private (double Width, double Height)? _measured;

    // The last drawing and what it was drawn from. A part that reads the chart's context is rendered on every
    // walk of the page — sixty charts on one page were each drawn again whenever one of them was measured.
    private Drawing? _drawn;

    // Nearer than any two boxes a layout tells apart (a sixty-fourth of a pixel).
    private const double Same = 1d / 1024;

    private sealed record Drawing(
        IEnumerable<Component?>? Children, UiChartData Data, bool Horizontal, double Width, double Height, string? Gutter, string? Class,
        CultureInfo Culture, string Svg, UiChartPlot? Plot, UiChartParts Parts, (string Box, IReadOnlyDictionary<string, string?> Marks)? Place)
    {
        // The parts and the rows by IDENTITY: a page that renders again hands over new ones, and one that does
        // not cannot have changed them.
        internal bool IsOf(IEnumerable<Component?>? children, UiChartData data, bool horizontal, double width, double height, string? gutter, string? @class) =>
            ReferenceEquals(Children, children) && ReferenceEquals(Data, data) && Horizontal == horizontal
            && Math.Abs(Width - width) < Same && Math.Abs(Height - height) < Same
            && string.Equals(Gutter, gutter, StringComparison.Ordinal) && string.Equals(Class, @class, StringComparison.Ordinal)
            && ReferenceEquals(Culture, CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// The space kept clear inside each edge, as one to four numbers in the order of a CSS padding:
    /// <c>"12 0 12 8"</c>. 8 all round unless this says otherwise; <c>"0"</c> for a sparkline.
    /// </summary>
    public string? Gutter { get; set; }

    /// <summary>The width the chart is drawn for until the browser has measured it, in pixels. 600 unless this says otherwise.</summary>
    public double? Width { get; set; }

    /// <summary>The height the chart is drawn for until the browser has measured it, in pixels. 200 unless this says otherwise.</summary>
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
        var horizontal = scope?.Horizontal == true;
        var (width, height) = _measured ?? (Width ?? 600, Height ?? 200);
        if (_drawn?.IsOf(Children, data, horizontal, width, height, Gutter, Class) != true)
        {
            _drawn = Draw(data, horizontal, width, height);
        }

        var area = _drawn is { Plot: { } plot, Place: { } place } ? UiChartHover.Area(place, plot, _drawn.Parts) : null;
        return RaskFragment([Raw.Value(_drawn.Svg), UiChartHover.Over(Size(width, height), Measured, area)]);
    }

    private Drawing Draw(UiChartData data, bool horizontal, double width, double height)
    {
        var parts = new UiChartParts(Children, data);
        var svg = new StringBuilder(16384)
            .Append("<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" viewBox=\"0 0 ").Number(width).Append(' ').Number(height)
            .Append("\" class=\"").Append(System.Net.WebUtility.HtmlEncode(UiClass.Compose("absolute inset-0 block size-full overflow-hidden", Class))).Append("\">");
        UiChartPlot? plot = null;
        if (parts.Pie is { } pie)
        {
            UiChartPieDrawing.Write(svg, pie, data, width, height, UiChartPlot.Gutter(Gutter));
            svg.Append("<rect fill=\"none\"></rect></svg>");
        }
        else
        {
            plot = new UiChartPlot(parts, data, horizontal, width, height, Gutter);
            new UiChartDrawing(plot, parts, data).Write(svg);
            svg.Append("<rect fill=\"none\" pointer-events=\"all\" width=\"").Number(width).Append("\" height=\"").Number(height).Append("\" x=\"0\" y=\"0\"></rect></svg>");
        }

        return new Drawing(
            Children, data, horizontal, width, height, Gutter, Class, CultureInfo.CurrentCulture, svg.ToString(), plot, parts,
            plot is null ? null : UiChartHover.Place(plot));
    }

    // A box as the runtime's measuring hook writes one — "<width> <height>" to a hundredth — so a chart drawn
    // at the size it turns out to have is told nothing, and is not drawn again.
    private static string Size(double width, double height) =>
        string.Create(CultureInfo.InvariantCulture, $"{Hundredth(width):R} {Hundredth(height):R}");

    private static double Hundredth(double pixels) => Math.Floor((pixels * 100) + 0.5) / 100;

    // The browser's word for the box. Anything that is not two sizes a chart could have is not a box.
    private void Measured(string size)
    {
        var space = size.IndexOf(' ', StringComparison.Ordinal);
        if (space > 0
            && double.TryParse(size.AsSpan(0, space), NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
            && double.TryParse(size.AsSpan(space + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
            && width is > 0 and <= MaxSide && height is > 0 and <= MaxSide)
        {
            _measured = (width, height);
        }
    }

    private const double MaxSide = 16384;
}
