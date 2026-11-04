namespace Rask.Core.Globalization;

/// <summary>
///     Matches BCP&#160;47 language tags against the app's configured list as plain strings.
/// </summary>
/// <remarks>
///     Strings, never <see cref="System.Globalization.CultureInfo" />, because a runtime without culture data
///     cannot answer the question a <c>CultureInfo</c> would be asked: under invariant globalization every
///     culture's <c>Parent</c> is the invariant one, so walking <c>hu-HU</c> up to a supported <c>hu</c> found
///     nothing and a Hungarian visitor was served the default language. The tag itself carries the answer.
/// </remarks>
internal static class LanguageTag
{
    /// <summary>
    ///     The entry of <paramref name="supported" /> that best serves <paramref name="requested" />, as the app
    ///     spelled it, or <c>null</c>.
    /// </summary>
    /// <remarks>
    ///     Two passes. The first walks the request's own subtags, so <c>hu-HU</c> is served by a supported
    ///     <c>hu-HU</c> and then by a supported <c>hu</c>. The second compares the language subtag of each side,
    ///     so a request for <c>hu</c> is served by a supported <c>hu-HU</c> — the app has the language, just in a
    ///     region the visitor did not name. Case and <c>_</c> for <c>-</c> are not differences.
    /// </remarks>
    public static string? Match(string? requested, IEnumerable<string>? supported)
    {
        if (supported is null || Normalize(requested) is not { Length: > 0 } wanted)
        {
            return null;
        }

        var list = supported as IReadOnlyList<string> ?? [.. supported];
        for (var candidate = wanted; candidate.Length > 0; candidate = Parent(candidate))
        {
            var exact = candidate;
            if (list.FirstOrDefault(name => string.Equals(Normalize(name), exact, StringComparison.OrdinalIgnoreCase)) is { } found)
            {
                return found;
            }
        }

        var language = Language(wanted);
        return list.FirstOrDefault(name =>
            Normalize(name) is { Length: > 0 } tag && string.Equals(Language(tag), language, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether two tags name the same language and region, ignoring case and separator.</summary>
    public static bool Same(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? tag) => (tag ?? string.Empty).Trim().Replace('_', '-');

    // hu-Latn-HU -> hu-Latn -> hu -> "".
    private static string Parent(string tag)
    {
        var cut = tag.LastIndexOf('-');
        return cut <= 0 ? string.Empty : tag[..cut];
    }

    private static string Language(string tag)
    {
        var cut = tag.IndexOf('-', StringComparison.Ordinal);
        return cut < 0 ? tag : tag[..cut];
    }
}
