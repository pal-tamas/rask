using System.Net;
using System.Text;

namespace Rask;

/// <summary>Writes a pie's slices: a full circle, clockwise from twelve o'clock, one slice per row with a value.</summary>
internal static class UiChartPieDrawing
{
    private const double Stroke = 3;

    // Flux's built-in palette, in the order it hands the hues out.
    private static readonly Ui.Color[] Palette =
    [
        Ui.Color.Sky, Ui.Color.Lime, Ui.Color.Orange, Ui.Color.Violet, Ui.Color.Teal, Ui.Color.Pink, Ui.Color.Amber,
        Ui.Color.Indigo, Ui.Color.Emerald, Ui.Color.Rose, Ui.Color.Cyan, Ui.Color.Fuchsia, Ui.Color.Yellow, Ui.Color.Blue,
        Ui.Color.Green, Ui.Color.Purple, Ui.Color.Red,
    ];

    internal static void Write(StringBuilder svg, UiChartPie pie, UiChartData data, double width, double height, double[] gutter)
    {
        var total = 0d;
        for (var row = 0; row < data.Count; row++)
        {
            total += Size(pie, data, row);
        }

        // The stroke that parts the slices is centred on their edge: half of it is kept inside the gutter too.
        var outer = (Math.Min(width, height) / 2) - gutter[0] - (Stroke / 2);
        var inner = Inner(pie.InnerRadius, outer);
        var turned = 0d;
        var slice = 0;
        svg.Append("<g>");
        for (var row = 0; row < data.Count && total > 0; row++)
        {
            var size = Size(pie, data, row);
            if (size <= 0)
            {
                continue;
            }

            var from = turned / total * Math.Tau;
            turned += size;
            svg.Append("<path stroke-width=\"3\" stroke-linejoin=\"round\" class=\"").Append(Fill(Hue(pie, data, row, slice++)))
                .Append(" stroke-white dark:stroke-zinc-900");
            if (!string.IsNullOrWhiteSpace(pie.Class))
            {
                svg.Append(' ').Append(WebUtility.HtmlEncode(pie.Class.Trim()));
            }

            svg.Append("\" d=\"");
            UiChartPaths.Sector(svg, width / 2, height / 2, outer, inner, pie.Radius ?? 0, from, turned / total * Math.Tau);
            svg.Append("\" data-rask-plot-row=\"").Append(row).Append("\"></path>");
        }

        svg.Append("</g>");
    }

    /// <summary>The hue of the slice drawn for <paramref name="row" />, the <paramref name="slice" />th one drawn.</summary>
    internal static Ui.Color Hue(UiChartPie pie, UiChartData data, int row, int slice) =>
        pie.ColorField?.Raw(data, row) is Ui.Color chosen ? chosen : Palette[slice % Palette.Length];

    internal static double Size(UiChartPie pie, UiChartData data, int row)
    {
        var size = pie.Field is null ? data.Number(row) : pie.Field.Number(data, row);
        return double.IsNaN(size) || size < 0 ? 0 : size;
    }

    private static double Inner(string? radius, double outer)
    {
        if (string.IsNullOrWhiteSpace(radius))
        {
            return 0;
        }

        var text = radius.AsSpan().Trim();
        return text[^1] == '%'
            ? outer * double.Parse(text[..^1], System.Globalization.CultureInfo.InvariantCulture) / 100
            : double.Parse(text.TrimEnd("px"), System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static string Fill(Ui.Color hue) => hue switch
    {
        Ui.Color.Red => "fill-red-500",
        Ui.Color.Orange => "fill-orange-500",
        Ui.Color.Amber => "fill-amber-500",
        Ui.Color.Yellow => "fill-yellow-500",
        Ui.Color.Lime => "fill-lime-500",
        Ui.Color.Green => "fill-green-500",
        Ui.Color.Emerald => "fill-emerald-500",
        Ui.Color.Teal => "fill-teal-500",
        Ui.Color.Cyan => "fill-cyan-500",
        Ui.Color.Sky => "fill-sky-500",
        Ui.Color.Blue => "fill-blue-500",
        Ui.Color.Indigo => "fill-indigo-500",
        Ui.Color.Violet => "fill-violet-500",
        Ui.Color.Purple => "fill-purple-500",
        Ui.Color.Fuchsia => "fill-fuchsia-500",
        Ui.Color.Pink => "fill-pink-500",
        Ui.Color.Rose => "fill-rose-500",
        _ => "fill-zinc-500",
    };

    internal static string Background(Ui.Color hue) => hue switch
    {
        Ui.Color.Red => "bg-red-500",
        Ui.Color.Orange => "bg-orange-500",
        Ui.Color.Amber => "bg-amber-500",
        Ui.Color.Yellow => "bg-yellow-500",
        Ui.Color.Lime => "bg-lime-500",
        Ui.Color.Green => "bg-green-500",
        Ui.Color.Emerald => "bg-emerald-500",
        Ui.Color.Teal => "bg-teal-500",
        Ui.Color.Cyan => "bg-cyan-500",
        Ui.Color.Sky => "bg-sky-500",
        Ui.Color.Blue => "bg-blue-500",
        Ui.Color.Indigo => "bg-indigo-500",
        Ui.Color.Violet => "bg-violet-500",
        Ui.Color.Purple => "bg-purple-500",
        Ui.Color.Fuchsia => "bg-fuchsia-500",
        Ui.Color.Pink => "bg-pink-500",
        Ui.Color.Rose => "bg-rose-500",
        _ => "bg-zinc-500",
    };
}
