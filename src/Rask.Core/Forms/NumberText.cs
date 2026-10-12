using System.Buffers;
using System.Globalization;
using System.Text;
using Rask.Core.Globalization;

namespace Rask.Core.Forms;

// The text of a number as a bound field receives it, made into the one spelling the parsers read: digits, a
// dot for the fraction.
//
// A number input always sends that spelling, whatever the reader typed and in whatever language — "12,5" in a
// Hungarian browser arrives as "12.5" — so text made of digits, a sign, a dot and an exponent is left exactly
// as it is, in every culture. Anything else was typed into a TEXT field (or written by hand into a frame), and
// is the reader's own spelling: read in their culture, with its decimal separator and its groups of three.
// The parsers must never be shown it as it is: to them a comma is a separator of thousands wherever it stands,
// and "12,5" is one hundred and twenty-five.
internal static class NumberText
{
    private static readonly SearchValues<char> Plain = SearchValues.Create("0123456789+-.eE");
    private static readonly SearchValues<char> Digits = SearchValues.Create("0123456789");

    /// <summary>The number in the parsers' spelling, or null when the text is no number in the reader's culture.</summary>
    internal static string? Invariant(string raw) =>
        raw.AsSpan().ContainsAnyExcept(Plain) ? Invariant(raw, RaskCulture.Current.NumberFormat) : raw;

    /// <inheritdoc cref="Invariant(string)" />
    internal static string? Invariant(string raw, NumberFormatInfo format)
    {
        var text = raw.AsSpan().Trim();
        var negative = text.StartsWith("-", StringComparison.Ordinal);
        if (negative || text.StartsWith("+", StringComparison.Ordinal))
        {
            text = text[1..];
        }

        var point = text.IndexOf(format.NumberDecimalSeparator, StringComparison.Ordinal);
        var fraction = point < 0 ? default : text[(point + format.NumberDecimalSeparator.Length)..];
        if (point >= 0 && (fraction.IsEmpty || fraction.ContainsAnyExcept(Digits)))
        {
            return null;
        }

        var whole = Ungrouped(point < 0 ? text : text[..point], format.NumberGroupSeparator);
        if (whole is null)
        {
            return null;
        }

        return string.Concat(negative ? "-" : string.Empty, whole, point < 0 ? string.Empty : ".", fraction);
    }

    /// <summary>A number written with an exponent, written out: <c>1e3</c> is <c>1000</c>. Null for anything else.</summary>
    internal static string? WrittenOut(string text) =>
        text.AsSpan().ContainsAny('e', 'E') && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : null;

    // Digits alone, or groups of three after a first group of one to three: "1 234 567". A separator anywhere
    // else — "12,5" where the comma groups — is not this number written differently, it is no number.
    private static string? Ungrouped(ReadOnlySpan<char> whole, string separator)
    {
        var digits = new StringBuilder(whole.Length);
        var (group, groups) = (0, 0);
        foreach (var c in whole)
        {
            if (char.IsAsciiDigit(c))
            {
                digits.Append(c);
                group++;
                continue;
            }

            if (!Separates(c, separator) || group == 0 || (groups == 0 ? group > 3 : group != 3))
            {
                return null;
            }

            groups++;
            group = 0;
        }

        return group > 0 && (groups == 0 || group == 3) ? digits.ToString() : null;
    }

    // A culture that groups with a space writes a no-break one; a reader types the one on the keyboard.
    private static bool Separates(char c, string separator) =>
        separator.Length == 1 && (c == separator[0] || (IsSpace(c) && IsSpace(separator[0])));

    private static bool IsSpace(char c) => c is ' ' or ' ' or ' ';
}
