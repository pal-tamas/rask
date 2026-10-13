using System.Globalization;

namespace Rask;

/// <summary>
///     A CSS colour as a browser writes it back once computed: what Flux's picker reads its numbers from, and
///     so what <see cref="UiColorText" /> reads them from.
/// </summary>
/// <remarks>
///     A colour of the old syntaxes — hex, a name, <c>rgb()</c>, <c>hsl()</c>, <c>hwb()</c> — is held in eight
///     bits a channel and comes back as <c>rgb(r, g, b)</c> or <c>rgba(r, g, b, a)</c>. One of the newer
///     functions (<c>lab()</c>, <c>lch()</c>, <c>oklab()</c>, <c>oklch()</c>, <c>color()</c>) comes back as
///     itself, percentages resolved, lightness and chroma held to their range and the hue brought into one
///     turn. Each rule here was checked against what Flux's picker made of a typed colour.
/// </remarks>
internal static partial class UiColorComputed
{
    private static readonly char[] Loose = [' ', ',', '/'];

    /// <summary>The computed text of <paramref name="text" />, or <c>null</c> for text that is no colour this reads.</summary>
    internal static string? Of(string? text)
    {
        var css = text?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(css))
        {
            return null;
        }

        if (string.Equals(css, "transparent", StringComparison.Ordinal))
        {
            return Stored(0, 0, 0, 0);
        }

        if (UiColorNames.TryGet(css, out var named))
        {
            return Stored(named >> 16, (named >> 8) & 0xff, named & 0xff, 255);
        }

        var open = css.IndexOf('(', StringComparison.Ordinal);
        if (open < 0)
        {
            return Hex(css);
        }

        if (!css.EndsWith(')'))
        {
            return null;
        }

        var body = css[(open + 1)..^1];

        return css[..open] switch
        {
            "rgb" or "rgba" => Rgb(body.Split(Loose, StringSplitOptions.RemoveEmptyEntries)),
            "hsl" or "hsla" => Hsl(body.Split(Loose, StringSplitOptions.RemoveEmptyEntries)),
            "hwb" => Hwb(body.Split(Loose, StringSplitOptions.RemoveEmptyEntries)),
            "lab" => Rectangular("lab", body, 100, 125),
            "oklab" => Rectangular("oklab", body, 1, 0.4),
            "lch" => Polar("lch", body, 100, 150),
            "oklch" => Polar("oklch", body, 1, 0.4),
            "color" => Predefined(body),
            _ => null,
        };
    }

    // With its # in every length; bare, Flux takes three, six and eight digits and not four.
    private static string? Hex(string css)
    {
        var bare = css[0] != '#';
        var digits = bare ? css : css[1..];
        if (digits.Length is not (3 or 4 or 6 or 8) || (bare && digits.Length == 4) || !digits.All(char.IsAsciiHexDigit))
        {
            return null;
        }

        // #abc is #aabbcc: every digit doubled.
        var full = digits.Length <= 4 ? string.Concat(digits.Select(digit => new string(digit, 2))) : digits;
        var value = uint.Parse(full, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var alpha = 255;
        if (full.Length == 8)
        {
            alpha = (int)(value & 0xff);
            value >>= 8;
        }

        return Stored((int)(value >> 16), (int)((value >> 8) & 0xff), (int)(value & 0xff), alpha);
    }

    // A colour held in eight bits a channel, alpha too, as the browser writes it back.
    private static string Stored(int red, int green, int blue, int alpha) =>
        alpha == 255
            ? string.Create(CultureInfo.InvariantCulture, $"rgb({red}, {green}, {blue})")
            : string.Create(CultureInfo.InvariantCulture, $"rgba({red}, {green}, {blue}, {StoredAlpha(alpha)})");

    // Two decimals when those give the same eight bits back, three when they do not: 128 is 0.5, 136 is 0.533.
    private static string StoredAlpha(int alpha)
    {
        var hundredths = Half(alpha * 100 / 255d) / 100d;
        var written = Half(hundredths * 255) == alpha ? hundredths : Half(alpha * 1000 / 255d) / 1000d;

        return written.ToString("0.###", CultureInfo.InvariantCulture);
    }

    // A half goes up, as the browser rounds a channel.
    private static int Half(double value) => (int)Math.Floor(value + 0.5);

    /// <summary>
    ///     A number as the browser writes one: the shortest text of the float it keeps, in plain digits down to
    ///     a millionth and with an exponent below that — which Flux then reads as two numbers.
    /// </summary>
    private static string Written(double value)
    {
        var kept = (float)value;
        var size = MathF.Abs(kept);
        if (size == 0)
        {
            return "0";
        }

        if (size < 1e-6f)
        {
            return kept.ToString("0.#######e-0", CultureInfo.InvariantCulture);
        }

        return size < 1e15f ? ((decimal)kept).ToString(CultureInfo.InvariantCulture) : kept.ToString("0", CultureInfo.InvariantCulture);
    }

    private static bool TryNumber(string part, out double number) =>
        double.TryParse(part, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number);

    // An angle in any of CSS's units, in degrees; a bare number is degrees.
    private static bool TryAngle(string part, out double degrees)
    {
        var (digits, scale) = part switch
        {
            _ when part.EndsWith("deg", StringComparison.Ordinal) => (part[..^3], 1d),
            _ when part.EndsWith("grad", StringComparison.Ordinal) => (part[..^4], 0.9),
            _ when part.EndsWith("rad", StringComparison.Ordinal) => (part[..^3], 180 / Math.PI),
            _ when part.EndsWith("turn", StringComparison.Ordinal) => (part[..^4], 360d),
            _ => (part, 1d),
        };
        var read = TryNumber(digits, out degrees);
        degrees = (((degrees * scale) % 360) + 360) % 360;

        return read;
    }
}
