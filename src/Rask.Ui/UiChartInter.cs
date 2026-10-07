using System.Collections.Frozen;

namespace Rask;

/// <summary>How wide a tick label is, without a browser to ask.</summary>
/// <remarks>
/// Flux lays a chart out around the measured width of its tick labels: the widest value label sets the left
/// gutter, a label that would run off the edge pulls the plot in. A chart drawn in C# has to know those widths
/// before it writes a coordinate, so it carries Inter's metrics (<c>UiChartInter.Metrics.cs</c>, generated from
/// the face itself) and adds them up exactly as Chromium does. In another face the labels still fit; the
/// gutters are simply those Inter would have needed.
/// </remarks>
internal static partial class UiChartInter
{
    private const double UnitsPerEm = 2048;

    // Built on first use rather than in a field initialiser: the tables are in this type's other file, and the
    // order two files' initialisers run in is not one to lean on.
    private static readonly Lazy<FrozenDictionary<int, short>> Regular = new(() => Pairs(Kerning400));

    private static readonly Lazy<FrozenDictionary<int, short>> Medium = new(() => Pairs(Kerning500));

    /// <summary>The width of <paramref name="text" /> at <paramref name="size" /> pixels, regular or medium (500).</summary>
    internal static double Measure(ReadOnlySpan<char> text, bool medium, double size = 12)
    {
        var advance = medium ? Advance500 : Advance400;
        var kerning = (medium ? Medium : Regular).Value;
        var units = 0;
        var previous = -1;
        foreach (var c in text)
        {
            var glyph = Glyphs.IndexOf(c, StringComparison.Ordinal);
            if (glyph < 0)
            {
                // A glyph the table does not hold is taken for a digit's width: wrong by a pixel, never by a gutter.
                units += advance[Glyphs.IndexOf('0', StringComparison.Ordinal)];
                previous = -1;
                continue;
            }

            units += advance[glyph];
            if (previous >= 0 && kerning.TryGetValue((previous << 7) | glyph, out var kern))
            {
                units += kern;
            }

            previous = glyph;
        }

        // Chromium reports a text run's length in 64ths of a pixel, rounded up.
        return Math.Ceiling(units * size / UnitsPerEm * 64) / 64;
    }

    private static FrozenDictionary<int, short> Pairs(int[] packed)
    {
        var pairs = new Dictionary<int, short>(packed.Length);
        foreach (var entry in packed)
        {
            pairs[((entry >> 17) << 7) | ((entry >> 10) & 0x7f)] = (short)((entry & 0x3ff) - 512);
        }

        return pairs.ToFrozenDictionary();
    }
}
