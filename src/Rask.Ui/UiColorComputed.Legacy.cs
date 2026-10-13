namespace Rask;

// rgb(), hsl() and hwb(), in the comma and the space syntax: each ends as eight bits a channel.
internal static partial class UiColorComputed
{
    private static string? Rgb(string[] parts) =>
        TryAlpha(parts, out var alpha) && TryChannel(parts[0], out var red) && TryChannel(parts[1], out var green) && TryChannel(parts[2], out var blue)
            ? Stored(Half(red), Half(green), Half(blue), alpha)
            : null;

    private static string? Hsl(string[] parts)
    {
        if (!TryAlpha(parts, out var alpha) || !TryAngle(parts[0], out var hue)
            || !TryPercent(parts[1], out var saturation) || !TryPercent(parts[2], out var lightness))
        {
            return null;
        }

        return Stored(Half(HslChannel(0, hue, saturation, lightness)), Half(HslChannel(8, hue, saturation, lightness)), Half(HslChannel(4, hue, saturation, lightness)), alpha);
    }

    private static string? Hwb(string[] parts)
    {
        if (!TryAlpha(parts, out var alpha) || !TryAngle(parts[0], out var hue)
            || !TryPercent(parts[1], out var white) || !TryPercent(parts[2], out var black))
        {
            return null;
        }

        if (white + black >= 1)
        {
            var grey = Half(white / (white + black) * 255);

            return Stored(grey, grey, grey, alpha);
        }

        int Mix(int n) => Half(((HslChannel(n, hue, 1, 0.5) / 255 * (1 - white - black)) + white) * 255);

        return Stored(Mix(0), Mix(8), Mix(4), alpha);
    }

    // CSS Color 4's hsl-to-rgb, one channel at a time.
    private static double HslChannel(int n, double hue, double saturation, double lightness)
    {
        var k = (n + (hue / 30)) % 12;
        var a = saturation * Math.Min(lightness, 1 - lightness);

        return (lightness - (a * Math.Max(-1, Math.Min(k - 3, Math.Min(9 - k, 1))))) * 255;
    }

    // Three channels, or four with the alpha last: the alpha in eight bits, opaque when it is not written.
    private static bool TryAlpha(string[] parts, out int alpha)
    {
        alpha = 255;
        if (parts.Length == 3)
        {
            return true;
        }

        if (parts.Length != 4)
        {
            return false;
        }

        var percent = parts[3].EndsWith('%');
        var read = TryNumber(percent ? parts[3][..^1] : parts[3], out var value);
        alpha = Half(Math.Clamp(percent ? value / 100 : value, 0, 1) * 255);

        return read;
    }

    private static bool TryChannel(string part, out double channel)
    {
        channel = 0;
        if (string.Equals(part, "none", StringComparison.Ordinal))
        {
            return true;
        }

        var percent = part.EndsWith('%');
        var read = TryNumber(percent ? part[..^1] : part, out channel);
        channel = Math.Clamp(percent ? channel * 2.55 : channel, 0, 255);

        return read;
    }

    private static bool TryPercent(string part, out double fraction)
    {
        var read = TryNumber(part.TrimEnd('%'), out fraction);
        fraction = Math.Clamp(fraction / 100, 0, 1);

        return read;
    }
}
