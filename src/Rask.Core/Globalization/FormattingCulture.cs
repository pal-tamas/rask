using System.Collections.Concurrent;
using System.Globalization;

namespace Rask.Core.Globalization;

/// <summary>
///     The culture a page writes its numbers, money and dates in, for the language it is written in (#1305).
/// </summary>
/// <remarks>
///     <para>
///         A language alone — <c>"en"</c>, <c>"hu"</c>, the scaffold's <c>Rask:Cultures</c> — is a NEUTRAL culture, and a
///         neutral culture names no country and so no currency: <c>{price:C}</c> wrote <c>¤19.99</c>. It is read as its
///         main country's culture (<c>"en"</c> → <c>en-US</c>, <c>"hu"</c> → <c>hu-HU</c>), the one .NET's own
///         <see cref="CultureInfo.CreateSpecificCulture" /> names, so a page writes what a job or an email of the same
///         app writes. The visitor's language — the text, <c>&lt;html lang&gt;</c>, the URL's language, the cookie —
///         stays the culture they were given; only how values are written changes.
///     </para>
///     <para>
///         A runtime with no culture data (a browser app that ships no ICU because it is English only) knows no country
///         at all, and English is read as US English from <see cref="InvariantEnglish" />. Any other language there
///         formats as the invariant culture, as it did.
///     </para>
/// </remarks>
internal static class FormattingCulture
{
    private static readonly ConcurrentDictionary<string, CultureInfo> Countries = new(StringComparer.Ordinal);

    /// <summary>The culture <paramref name="culture" />'s values are written in: itself, or for a language its country's.</summary>
    internal static CultureInfo Of(CultureInfo culture)
    {
        if (!RaskCultureResolver.IsGlobalizationSupported)
        {
            return InvariantEnglish.Serves(culture.Name) ? InvariantEnglish.Culture : culture;
        }

        return culture.IsNeutralCulture ? Countries.GetOrAdd(culture.Name, Country) : culture;
    }

    private static CultureInfo Country(string language)
    {
        var country = CultureInfo.CreateSpecificCulture(language);
        // A language .NET knows no country of answers the invariant culture; it keeps its own then.
        return country.Name.Length == 0 ? CultureInfo.GetCultureInfo(language) : CultureInfo.GetCultureInfo(country.Name);
    }
}
