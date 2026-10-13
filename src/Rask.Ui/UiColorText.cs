using System.Globalization;
using System.Text.RegularExpressions;

namespace Rask;

/// <summary>
///     Reads CSS colour text into red, green, blue and alpha exactly as Flux's picker does, done in C# for
///     <see cref="UiColorValue" />.
/// </summary>
/// <remarks>
///     <para>
///     Measured on Flux's live picker with a table of typed colours: it hands the text to the browser, takes
///     back the colour as the browser writes a COMPUTED one, and reads the first numbers it finds there as
///     red, green and blue, with a fourth as alpha. For hex, a name, <c>rgb()</c>, <c>hsl()</c> and
///     <c>hwb()</c> the computed text is <c>rgb(r, g, b)</c> and the reading is right. For the wide-gamut
///     functions the browser keeps the function — <c>oklch(0.7 0.1 200)</c> stays that — so Flux reads
///     lightness, chroma and hue as red, green and blue: <c>#0100c8</c>. A space's own name counts too
///     (<c>display-p3</c> begins with a 3). The owner's decision (2026-10-07) is to accept those inputs and
///     make of them exactly what Flux makes, so this writes the browser's computed text
///     (<see cref="UiColorComputed" />) and reads it the same way.
///     </para>
///     <para>
///     Not read, where Flux reads them through the browser: <c>color-mix()</c>, relative colours,
///     <c>calc()</c>, <c>currentColor</c> and the system colours — each needs the browser's own answer.
///     </para>
/// </remarks>
internal static partial class UiColorText
{
    /// <summary>The colour <paramref name="text" /> names: channels as Flux finds them, alpha 0 to 1.</summary>
    /// <returns>False for text that is no colour.</returns>
    internal static bool TryRead(string? text, out double red, out double green, out double blue, out double alpha)
    {
        (red, green, blue, alpha) = (0, 0, 0, 1);
        if (UiColorComputed.Of(text) is not { } computed)
        {
            return false;
        }

        var found = Numbers().Matches(computed);
        (red, green, blue) = (At(found, 0), At(found, 1), At(found, 2));
        alpha = found.Count > 3 ? Math.Min(At(found, 3), 1) : 1;

        return true;
    }

    // A channel that is not there is not a number: Flux's "#NaNNaNNaN" for oklch(0.7 none 200).
    private static double At(MatchCollection found, int index) =>
        index < found.Count ? double.Parse(found[index].ValueSpan, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture) : double.NaN;

    // Unsigned: Flux reads lab(50 -40 30) as 50, 40, 30.
    [GeneratedRegex(@"\d+(?:\.\d+)?", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Numbers();
}
