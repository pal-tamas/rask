using System.Globalization;
using System.Runtime.InteropServices;

namespace Rask;

/// <summary>
///     One colour as <see cref="UiColorPicker" /> holds it: hue, saturation and brightness (HSV) with an alpha,
///     which is what its area and sliders move, read from and written as the CSS text a page stores.
/// </summary>
/// <remarks>
///     <para>
///     Kept as HSV rather than as the text because the text forgets: black has no hue and grey no
///     saturation, so a picker that re-read its own output would snap the hue slider back to red the moment
///     the thumb touched the bottom edge.
///     </para>
///     <para>
///     The arithmetic is Flux's, found by typing 126 colours with channels on a half into its live picker and
///     keeping the one pair of textbook formulas that reproduced every digit: channels are divided by 255
///     first, saturation and brightness are held as percentages, and the way back goes through chroma. The
///     order of the operations is part of it — a half rounds up or down by the last bit of a double.
///     </para>
/// </remarks>
/// <param name="Hue">0 to 360.</param>
/// <param name="Saturation">0 to 100.</param>
/// <param name="Brightness">0 to 100: HSV's value.</param>
/// <param name="Alpha">0 to 1.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct UiColorValue(double Hue, double Saturation, double Brightness, double Alpha)
{
    /// <summary>What an empty picker starts from: black, opaque.</summary>
    internal static readonly UiColorValue Black = new(0, 0, 0, 1);

    /// <summary>The colour as CSS a style attribute paints: <c>rgb(186 218 85)</c>, with the alpha when it has one.</summary>
    internal string Css => Alpha >= 1 ? Inv($"rgb({Channels})") : Inv($"rgb({Channels} / {Hundredths(Alpha)})");

    /// <summary>The same colour with no alpha: what the alpha slider's track fades to.</summary>
    internal string OpaqueCss => (this with { Alpha = 1 }).Css;

    /// <summary>The hue alone, at full saturation and brightness: the area's backdrop.</summary>
    internal string HueCss => new UiColorValue(double.IsNaN(Hue) ? 0 : Hue, 100, 100, 1).Css;

    /// <summary><c>#rrggbb</c>, which is what a swatch is matched by.</summary>
    internal string Hex
    {
        get
        {
            var (r, g, b) = Rgb;

            return "#" + HexDigits(r) + HexDigits(g) + HexDigits(b);
        }
    }

    private string Channels
    {
        get
        {
            var (r, g, b) = Rgb;

            return Whole(r) + " " + Whole(g) + " " + Whole(b);
        }
    }

    // Red, green and blue as the way back makes them, each still a fraction of a step from a whole number.
    private (double R, double G, double B) Rgb
    {
        get
        {
            var s = Saturation / 100;
            var v = Brightness / 100;
            var chroma = v * s;
            var x = chroma * (1 - Math.Abs((Hue / 60 % 2) - 1));
            var m = v - chroma;
            if (double.IsNaN(Hue))
            {
                return (double.NaN, double.NaN, double.NaN);
            }

            var (r, g, b) = ((int)Math.Floor(Hue / 60) % 6) switch
            {
                0 => (chroma, x, 0d),
                1 => (x, chroma, 0d),
                2 => (0d, chroma, x),
                3 => (0d, x, chroma),
                4 => (x, 0d, chroma),
                _ => (chroma, 0d, x),
            };

            return ((r + m) * 255, (g + m) * 255, (b + m) * 255);
        }
    }

    // Hue, saturation and lightness of the WHOLE channels, as Flux writes them: rgb(1 1 7) is hsl(240 75% 2%),
    // though the colour it was rounded from has another hue.
    private (string H, string S, string L) Hsl
    {
        get
        {
            var (red, green, blue) = Rgb;
            var (r, g, b) = (Math.Floor(red + 0.5) / 255, Math.Floor(green + 0.5) / 255, Math.Floor(blue + 0.5) / 255);
            var max = Math.Max(r, Math.Max(g, b));
            var delta = max - Math.Min(r, Math.Min(g, b));
            var lightness = (max + (max - delta)) / 2;
            var saturation = delta == 0 ? 0 : delta / (1 - Math.Abs((2 * lightness) - 1));

            return (Whole(HueOf(r, g, b, max, delta)), Whole(saturation * 100), Whole(lightness * 100));
        }
    }

    /// <summary>The colour as <paramref name="format" /> writes it.</summary>
    internal string Format(Ui.ColorPickerFormat format)
    {
        var (h, s, l) = Hsl;

        return format switch
        {
            Ui.ColorPickerFormat.Hexa => Hex + HexDigits(Alpha * 255),
            Ui.ColorPickerFormat.Rgb => Inv($"rgb({Channels})"),
            Ui.ColorPickerFormat.Rgba => Inv($"rgb({Channels} / {Hundredths(Alpha)})"),
            Ui.ColorPickerFormat.Hsl => Inv($"hsl({h} {s}% {l}%)"),
            Ui.ColorPickerFormat.Hsla => Inv($"hsl({h} {s}% {l}% / {Hundredths(Alpha)})"),
            _ => Hex,
        };
    }

    /// <summary>Reads a CSS colour as Flux's picker does (<see cref="UiColorText" />).</summary>
    /// <returns>False for text that is no colour.</returns>
    internal static bool TryParse(string? text, out UiColorValue color)
    {
        color = Black;
        if (!UiColorText.TryRead(text, out var red, out var green, out var blue, out var alpha))
        {
            return false;
        }

        color = FromRgb(red, green, blue, alpha);

        return true;
    }

    /// <summary>The colour of three channels, each 0 to 255 — or whatever number stood where a channel was looked for.</summary>
    internal static UiColorValue FromRgb(double red, double green, double blue, double alpha)
    {
        var (r, g, b) = (red / 255, green / 255, blue / 255);
        var max = Math.Max(r, Math.Max(g, b));
        var delta = max - Math.Min(r, Math.Min(g, b));

        return new UiColorValue(HueOf(r, g, b, max, delta), max == 0 ? 0 : delta / max * 100, max * 100, alpha);
    }

    // Which sixth of the wheel the strongest channel puts the colour in, and how far into it the other two do.
    // "At least the greatest" is "is the greatest", and false for what is not a number.
    private static double HueOf(double red, double green, double blue, double max, double delta)
    {
        if (delta == 0)
        {
            return 0;
        }

        double sixths;
        if (red >= max)
        {
            sixths = (green - blue) / delta % 6;
        }
        else if (green >= max)
        {
            sixths = ((blue - red) / delta) + 2;
        }
        else
        {
            sixths = ((red - green) / delta) + 4;
        }

        var hue = sixths * 60;

        return hue < 0 ? hue + 360 : hue;
    }

    // JavaScript's Math.round and its number-to-text, which Flux's values come from: a half goes up (.NET's
    // goes to even), nothing is held to 0–255, and what is not a number says so.
    private static string Whole(double value) =>
        double.IsNaN(value) ? "NaN" : ((long)Math.Floor(value + 0.5)).ToString(CultureInfo.InvariantCulture);

    private static string HexDigits(double value) =>
        double.IsNaN(value) ? "NaN" : ((long)Math.Floor(value + 0.5)).ToString("x2", CultureInfo.InvariantCulture);

    private static string Hundredths(double alpha) =>
        (Math.Floor((alpha * 100) + 0.5) / 100).ToString("0.##", CultureInfo.InvariantCulture);

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
