using System.Globalization;
using System.Text.RegularExpressions;

namespace Rask;

/// <summary>
///     The width a heading's own class gives it, in rem, read from the Tailwind utilities that set one on screen:
///     <c>w-32</c>, <c>min-w-24</c>, <c>w-[9.5rem]</c>, <c>min-w-[200px]</c>. What a table counts towards its least width
///     for a column the call site sized itself.
/// </summary>
/// <remarks>
///     Only a bare utility counts: one under a variant (<c>print:min-w-60</c>, <c>sm:w-40</c>) is not the screen's width
///     at every size. Where the class sets both a width and a least, the larger is what the column takes.
/// </remarks>
internal static partial class UiColumnClassWidth
{
    // Tailwind's spacing scale: one step is a quarter of a rem.
    private const decimal Step = 0.25m;

    private const decimal PixelsPerRem = 16m;

    /// <summary>The rem <paramref name="classes" /> size a heading to, or null where none of them does.</summary>
    internal static decimal? Rem(string? classes)
    {
        if (string.IsNullOrWhiteSpace(classes))
        {
            return null;
        }

        decimal? widest = null;
        foreach (var token in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Of(token.TrimEnd('!')) is { } rem && (widest is null || rem > widest))
            {
                widest = rem;
            }
        }

        return widest;
    }

    private static decimal? Of(string token)
    {
        if (Scale().Match(token) is { Success: true } scale)
        {
            return Number(scale.Groups["n"].Value) * Step;
        }

        if (Arbitrary().Match(token) is { Success: true } arbitrary)
        {
            var value = Number(arbitrary.Groups["n"].Value);
            return string.Equals(arbitrary.Groups["unit"].Value, "px", StringComparison.Ordinal) ? value / PixelsPerRem : value;
        }

        return null;
    }

    private static decimal Number(string digits) => decimal.Parse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?:min-)?w-(?<n>\d+(?:\.\d+)?)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Scale();

    [GeneratedRegex(@"^(?:min-)?w-\[(?<n>\d+(?:\.\d+)?)(?<unit>rem|px)\]$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Arbitrary();
}
