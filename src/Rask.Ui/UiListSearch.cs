using System.Globalization;
using System.Text;

namespace Rask;

/// <summary>
///     How a drawn list is searched where the browser cannot search it: a windowed list, whose rows are not all on the
///     page. The same rules the runtime keeps for a list it holds whole (rask-list.ts <c>fold</c> and <c>filtered</c>,
///     rask-menu-keys.ts <c>typeAhead</c>), so a long list finds what a short one would.
/// </summary>
internal static class UiListSearch
{
    /// <summary>Text as a search reads it: whatever the case or the accents.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text decomposed, its accents dropped, lower case.</returns>
    internal static string Fold(string text)
    {
        // U+0300 to U+036F, the combining diacritical marks: what rask-list.ts strips.
        var kept = text.Normalize(NormalizationForm.FormD).Where(c => c is < (char)0x0300 or > (char)0x036F);

        return string.Concat(kept).ToLower(CultureInfo.CurrentCulture);
    }

    /// <summary>Whether a search for <paramref name="needle" /> leaves <paramref name="option" /> in the list.</summary>
    /// <param name="option">The row's option.</param>
    /// <param name="needle">What was typed, already <see cref="Fold" />ed.</param>
    /// <returns>True when nothing was typed, the row is kept by every search, or its words or keywords hold the text.</returns>
    internal static bool Finds(UiSelectOption option, string needle) =>
        needle.Length == 0
        || option.Filterable == false
        || Fold(option.Text).Contains(needle, StringComparison.Ordinal)
        || (option.Keywords is { } keywords && Fold(keywords).Contains(needle, StringComparison.Ordinal));

    /// <summary>
    ///     Type-ahead: the row the letters typed so far land on, or -1. One letter looks from the row after
    ///     <paramref name="cursor" />, and the same letter again goes on to the next that starts with it; several
    ///     different letters are a prefix, looked for from the cursor itself.
    /// </summary>
    /// <param name="buffer">Every letter typed within the type-ahead pause.</param>
    /// <param name="key">The last of them.</param>
    /// <param name="cursor">The row the cursor is on, or -1.</param>
    /// <param name="texts">Each row's words, or null for a row the cursor steps over.</param>
    /// <returns>The row's index, or -1 when no row starts so.</returns>
    internal static int TypeAhead(string buffer, string key, int cursor, IReadOnlyList<string?> texts)
    {
        var repeated = buffer.Length > 0 && buffer.All(c => c == buffer[0]);
        var needle = (repeated ? key : buffer).ToLower(CultureInfo.CurrentCulture);
        if (needle.Length == 0 || texts.Count == 0)
        {
            return -1;
        }

        var start = needle.Length == 1 ? cursor + 1 : cursor;
        for (var i = 0; i < texts.Count; i++)
        {
            var at = (((start + i) % texts.Count) + texts.Count) % texts.Count;
            if (texts[at] is { } text && text.ToLower(CultureInfo.CurrentCulture).StartsWith(needle, StringComparison.Ordinal))
            {
                return at;
            }
        }

        return -1;
    }
}
