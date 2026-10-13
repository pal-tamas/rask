using System.Collections.Frozen;

namespace Rask;

// lab(), lch(), oklab(), oklch() and color(): the browser keeps the function and resolves what is inside it.
internal static partial class UiColorComputed
{
    // The spaces color() takes, and the name each is written back under: plain xyz is xyz-d65.
    private static readonly FrozenDictionary<string, string> Spaces = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["srgb"] = "srgb",
        ["srgb-linear"] = "srgb-linear",
        ["display-p3"] = "display-p3",
        ["a98-rgb"] = "a98-rgb",
        ["prophoto-rgb"] = "prophoto-rgb",
        ["rec2020"] = "rec2020",
        ["xyz"] = "xyz-d65",
        ["xyz-d50"] = "xyz-d50",
        ["xyz-d65"] = "xyz-d65",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // Lightness and two axes. A percentage is of the lightness's top and of the axes' reference; the lightness
    // is held to its range, the axes are not.
    private static string? Rectangular(string name, string body, double top, double reference)
    {
        if (Split(body) is not var (parts, alpha) || parts.Length != 3
            || !TryScaled(parts[0], top, out var lightness)
            || !TryScaled(parts[1], reference, out var first) || !TryScaled(parts[2], reference, out var second))
        {
            return null;
        }

        return name + "(" + Written(lightness is { } l ? Math.Clamp(l, 0, top) : null) + " " + Written(first) + " " + Written(second) + alpha + ")";
    }

    // Lightness, chroma and hue: the chroma never below nothing, the hue within one turn.
    private static string? Polar(string name, string body, double top, double reference)
    {
        if (Split(body) is not var (parts, alpha) || parts.Length != 3
            || !TryScaled(parts[0], top, out var lightness) || !TryScaled(parts[1], reference, out var chroma))
        {
            return null;
        }

        double? hue = null;
        if (!IsNone(parts[2]))
        {
            if (!TryAngle(parts[2], out var degrees))
            {
                return null;
            }

            hue = degrees;
        }

        return name + "(" + Written(lightness is { } l ? Math.Clamp(l, 0, top) : null) + " "
            + Written(chroma is { } c ? Math.Max(c, 0) : null) + " " + Written(hue) + alpha + ")";
    }

    // color(space a b c): the channels as written, a percentage as its hundredth, held to no range.
    private static string? Predefined(string body)
    {
        if (Split(body) is not var (parts, alpha) || parts.Length != 4 || !Spaces.TryGetValue(parts[0], out var space)
            || !TryScaled(parts[1], 1, out var first) || !TryScaled(parts[2], 1, out var second) || !TryScaled(parts[3], 1, out var third))
        {
            return null;
        }

        return "color(" + space + " " + Written(first) + " " + Written(second) + " " + Written(third) + alpha + ")";
    }

    // The channels, and the alpha as it is written back: nothing when the colour is opaque. These functions
    // take no commas.
    private static (string[] Parts, string Alpha)? Split(string body)
    {
        var halves = body.Split('/');
        if (halves.Length > 2 || body.Contains(',', StringComparison.Ordinal))
        {
            return null;
        }

        var alpha = string.Empty;
        if (halves.Length == 2)
        {
            if (!TryScaled(halves[1].Trim(), 1, out var value))
            {
                return null;
            }

            var held = value is { } a ? Math.Clamp(a, 0, 1) : (double?)null;
            alpha = held is >= 1 ? string.Empty : " / " + Written(held);
        }

        return (halves[0].Split(' ', StringSplitOptions.RemoveEmptyEntries), alpha);
    }

    // A number, or a percentage of `whole`; `none` is no number, and is written back as the word.
    private static bool TryScaled(string part, double whole, out double? value)
    {
        value = null;
        if (IsNone(part))
        {
            return true;
        }

        var percent = part.EndsWith('%');
        var read = TryNumber(percent ? part[..^1] : part, out var number);
        value = percent ? number / 100 * whole : number;

        return read;
    }

    private static bool IsNone(string part) => string.Equals(part, "none", StringComparison.Ordinal);

    private static string Written(double? value) => value is { } number ? Written(number) : "none";
}
