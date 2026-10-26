using System.Globalization;

namespace Rask;

/// <summary>
///     What a datetime picker's button writes between a moment's date and its time in the picker's locale, as Flux's
///     does from the browser's Intl: the data is Unicode CLDR's (<c>UiDatetimePickerJoins.g.cs</c>, written by
///     <c>scripts/upstream/cldr.mjs</c>).
/// </summary>
internal static partial class UiDatetimePickerJoins
{
    /// <summary>
    ///     <c>" at "</c> where the date, that word in grey and the time are written; else the pattern the moment is
    ///     written in as one text, <c>{1}</c> the date and <c>{0}</c> the time — <c>"{1}, {0}"</c>, ja <c>"{1} {0}"</c>.
    ///     A region the data does not name is its language's (<c>fr-CA</c> → <c>fr</c>), and an unknown language CLDR's root.
    /// </summary>
    internal static string Of(CultureInfo culture)
    {
        for (var name = culture.Name; name.Length > 0; name = Parent(name))
        {
            if (Own.TryGetValue(name, out var join))
            {
                return join;
            }
        }

        return Root;
    }

    /// <summary>Whether <paramref name="join" /> is a pattern the whole moment is written in, not a word between.</summary>
    internal static bool IsPattern(string join) => join.Contains("{0}", StringComparison.Ordinal);

    /// <summary>The moment written in <paramref name="pattern" />.</summary>
    internal static string Fill(string pattern, string date, string time) =>
        pattern.Replace("{1}", date, StringComparison.Ordinal).Replace("{0}", time, StringComparison.Ordinal);

    private static string Parent(string name)
    {
        var dash = name.LastIndexOf('-');
        return dash < 0 ? string.Empty : name[..dash];
    }
}
