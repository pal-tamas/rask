using System.Globalization;

namespace Rask.Native.Components;

/// <summary>
///     A type-safe, cross-platform color for native bar chrome — a background, a tint (bar buttons / the
///     selected tab), or title text. Like <see cref="NativeIcon" /> it is a single authored value the platform
///     head resolves to its own type (iOS <c>UIColor</c>, Android <c>Color</c>). Build one from hex
///     (<see cref="Hex" />) or channels (<see cref="Rgba" />); pair a light- and dark-appearance color with
///     <see cref="Adaptive" /> so the bar tracks the system theme. The <c>default</c> value is
///     <see cref="System" /> — "leave the platform default" — so an unset optional style prop keeps today's
///     system look, and native styling stays fully opt-in.
/// </summary>
/// <remarks>
///     Modelled as a <c>readonly record struct</c> (like <see cref="NativeIcon" />) so it is allocation-free and
///     compares by value. It stores the already-resolved wire token — <c>null</c> for <see cref="System" />, a
///     single <c>#RRGGBBAA</c> for a fixed color, or <c>light|dark</c> (two <c>#RRGGBBAA</c> tokens) for an
///     adaptive pair — which is exactly what the native-chrome descriptor serializes, so the heads parse a plain
///     string and never see this type. There is deliberately no implicit conversion from <c>string</c>.
/// </remarks>
public readonly record struct NativeColor
{
    private readonly string? _token;

    private NativeColor(string? token) => _token = token;

    /// <summary>The platform's default appearance for this slot (the look before any styling). The default value.</summary>
    public static NativeColor System => default;

    /// <summary><c>true</c> when this is <see cref="System" /> — no explicit color, so the head keeps the platform default.</summary>
    public bool IsSystem => _token is null;

    /// <summary>The serialized wire token: <c>null</c> when <see cref="System" />, else <c>#RRGGBBAA</c> or <c>light|dark</c>.</summary>
    public string? ToToken() => _token;

    // --- Constructors ----------------------------------------------------------------------------------------
    /// <summary>
    ///     A fixed color from a hex string — <c>#RGB</c>, <c>#RGBA</c>, <c>#RRGGBB</c>, or <c>#RRGGBBAA</c>, with
    ///     or without the leading <c>#</c>. Shorthand forms are expanded; alpha defaults to fully opaque.
    /// </summary>
    /// <exception cref="FormatException"><paramref name="hex" /> is not a recognised hex color.</exception>
    public static NativeColor Hex(string hex) => new(Normalize(hex));

    /// <summary>A fixed color from 8-bit channels; <paramref name="a" /> defaults to fully opaque.</summary>
    public static NativeColor Rgba(byte r, byte g, byte b, byte a = 255) => new(Format(r, g, b, a));

    /// <summary>
    ///     A color that resolves to <paramref name="light" /> in light appearance and <paramref name="dark" /> in
    ///     dark appearance, so the bar tracks the system theme. A <see cref="System" /> side reuses the other
    ///     (so the pair is always concrete); if both are <see cref="System" /> the result is <see cref="System" />.
    /// </summary>
    public static NativeColor Adaptive(NativeColor light, NativeColor dark)
    {
        var l = light.LightHalf();
        var d = dark.DarkHalf();
        if (l is null && d is null)
        {
            return System;
        }

        l ??= d;
        d ??= l;
        return new NativeColor(string.Equals(l, d, StringComparison.Ordinal) ? l : $"{l}|{d}");
    }

    // --- Curated vocabulary (kept minimal — hex is the escape hatch) ------------------------------------------
    /// <summary>Opaque white (<c>#FFFFFFFF</c>).</summary>
    public static NativeColor White => new("#FFFFFFFF");

    /// <summary>Opaque black (<c>#000000FF</c>).</summary>
    public static NativeColor Black => new("#000000FF");

    /// <summary>Fully transparent (<c>#00000000</c>).</summary>
    public static NativeColor Clear => new("#00000000");

    // --- Resolution for the heads (same assembly) ------------------------------------------------------------
    /// <summary>
    ///     Parse a wire token into light/dark channel tuples. Returns <c>false</c> for a <c>null</c>/empty token
    ///     (i.e. <see cref="System" />), leaving the caller on the platform default. A fixed color yields the same
    ///     tuple for both; an adaptive token yields its two halves.
    /// </summary>
    internal static bool TryResolve(
        string? token, out (byte R, byte G, byte B, byte A) light, out (byte R, byte G, byte B, byte A) dark)
    {
        light = default;
        dark = default;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var bar = token.IndexOf('|');
        if (bar < 0)
        {
            light = dark = Channels(token);
            return true;
        }

        light = Channels(token[..bar]);
        dark = Channels(token[(bar + 1)..]);
        return true;
    }

    private string? LightHalf() => Half(0);

    private string? DarkHalf() => Half(^1);

    private string? Half(Index end)
    {
        if (_token is null)
        {
            return null;
        }

        var bar = _token.IndexOf('|');
        if (bar < 0)
        {
            return _token;
        }

        // end.IsFromEnd selects the dark half; otherwise the light half.
        return end.IsFromEnd ? _token[(bar + 1)..] : _token[..bar];
    }

    private static (byte R, byte G, byte B, byte A) Channels(string hex)
    {
        // hex is always a Normalize()d "#RRGGBBAA" by the time it reaches here.
        var r = Byte(hex, 1);
        var g = Byte(hex, 3);
        var b = Byte(hex, 5);
        var a = Byte(hex, 7);
        return (r, g, b, a);
    }

    private static byte Byte(string hex, int start) =>
        byte.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static string Format(byte r, byte g, byte b, byte a) =>
        string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}{a:X2}");

    private static string Normalize(string hex)
    {
        var s = hex.AsSpan().Trim();
        if (s.Length > 0 && s[0] == '#')
        {
            s = s[1..];
        }

        // Expand #RGB / #RGBA shorthand to full width.
        Span<char> full = stackalloc char[8];
        switch (s.Length)
        {
            case 3: // RGB
            case 4: // RGBA
                for (var i = 0; i < s.Length; i++)
                {
                    full[i * 2] = s[i];
                    full[(i * 2) + 1] = s[i];
                }

                if (s.Length == 3)
                {
                    full[6] = full[7] = 'F';
                }

                break;
            case 6: // RRGGBB
                s.CopyTo(full);
                full[6] = full[7] = 'F';
                break;
            case 8: // RRGGBBAA
                s.CopyTo(full);
                break;
            default:
                throw new FormatException($"'{hex}' is not a hex color (#RGB, #RGBA, #RRGGBB, or #RRGGBBAA).");
        }

        // Validate and uppercase in place, then emit "#RRGGBBAA" in a single allocation (like Format).
        Span<char> result = stackalloc char[9];
        result[0] = '#';
        for (var i = 0; i < full.Length; i++)
        {
            if (!Uri.IsHexDigit(full[i]))
            {
                throw new FormatException($"'{hex}' is not a hex color (#RGB, #RGBA, #RRGGBB, or #RRGGBBAA).");
            }

            result[i + 1] = char.ToUpperInvariant(full[i]);
        }

        return new string(result);
    }
}
