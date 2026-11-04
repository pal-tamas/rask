using System.Globalization;

namespace Rask.Core.Globalization;

/// <summary>
///     Chooses a visitor's culture from the signals a host can offer. Pure, and free of host types, so
///     the decision is unit-testable without a server or a browser.
/// </summary>
/// <remarks>
///     <para>
///         Order: <b>URL → cookie → client preference → default</b>. An explicit act beats a remembered
///         one, which beats an inferred one. Nothing here inspects the route path: by default <b>Rask keeps
///         culture out of it</b>, so one link is the same page for everyone. An app that puts the language in
///         its URLs (<see cref="RaskCultureOptions.UrlPrefix" />) reads it off the path instead, and asks this
///         only for a first visit to the bare root.
///     </para>
///     <para>
///         Matching walks the tag's subtags (<c>hu-HU</c> → <c>hu</c>) and will also
///         accept a sibling region — <c>hu</c> matches a supported <c>hu-HU</c> — because a visitor
///         asking for a language the app has in another region wants that language, not English.
///     </para>
/// </remarks>
public static class RaskCultureNegotiator
{
    /// <summary>Runs the negotiation described on this type.</summary>
    /// <param name="queryValue">The <c>?culture=</c> value, or <c>null</c>.</param>
    /// <param name="cookieValue">The raw culture cookie, or <c>null</c>.</param>
    /// <param name="clientPreferences">The visitor's languages, most-preferred first.</param>
    /// <param name="options">The app's configured languages.</param>
    public static CultureNegotiation Negotiate(
        string? queryValue,
        string? cookieValue,
        IReadOnlyList<string>? clientPreferences,
        RaskCultureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // No configured languages means culture support is off; the caller should not have asked, but
        // answering with the invariant culture is the only honest result.
        if (options.SupportedCultures.Count == 0)
        {
            return new CultureNegotiation(
                CultureInfo.InvariantCulture, CultureInfo.InvariantCulture, CultureSource.Default);
        }

        if (options.UseQueryString && TrySelect(queryValue, options, out var fromQuery))
        {
            return fromQuery with { Source = CultureSource.Query };
        }

        if (options.UseCookie
            && RaskCultureCookie.TryParse(cookieValue, out var cookieCulture, out var cookieUI)
            && TrySelect(cookieCulture, options, out var fromCookie))
        {
            return fromCookie with
            {
                UICulture = Match(cookieUI, options.SupportedUICultures) ?? fromCookie.UICulture,
                Source = CultureSource.Cookie,
            };
        }

        if (options.UseClientPreference && clientPreferences is { Count: > 0 })
        {
            foreach (var candidate in clientPreferences)
            {
                if (TrySelect(candidate, options, out var fromClient))
                {
                    return fromClient with { Source = CultureSource.Client };
                }
            }
        }

        var defaultName = options.DefaultCulture ?? options.SupportedCultures[0];
        var culture = RaskCultureResolver.TryResolve(defaultName, out var resolved)
            ? resolved
            : CultureInfo.InvariantCulture;

        return new CultureNegotiation(
            culture,
            Match(defaultName, options.SupportedUICultures) ?? culture,
            CultureSource.Default);
    }

    /// <summary>
    ///     Matches one requested tag against the app's languages, ignoring where it came from.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="Negotiate" /> because an explicit switch is not a negotiation: when a
    ///     visitor picks a language from a menu, <see cref="RaskCultureOptions.UseQueryString" /> and the
    ///     cookie settings decide how the choice is <em>carried</em>, never whether it is honoured.
    ///     Routing a switch through the query branch would have made <c>UseQueryString = false</c>
    ///     silently disable the culture switcher.
    /// </remarks>
    public static bool TrySelect(string? requested, RaskCultureOptions options, out CultureNegotiation selection)
    {
        ArgumentNullException.ThrowIfNull(options);

        selection = default;
        if (Match(requested, options.SupportedCultures) is not { } culture)
        {
            return false;
        }

        selection = new CultureNegotiation(
            culture,
            Match(requested, options.SupportedUICultures) ?? culture,
            CultureSource.Query);
        return true;
    }

    /// <summary>
    ///     The supported culture that best serves <paramref name="requested" />, or <c>null</c>.
    /// </summary>
    /// <remarks>
    ///     Matched on the tags (<see cref="LanguageTag.Match" />), and only the winner is turned into a
    ///     culture: a runtime with no culture data gives every culture the invariant one as its parent, so a
    ///     walk over <see cref="CultureInfo.Parent" /> never reached a supported <c>hu</c> from <c>hu-HU</c>.
    /// </remarks>
    private static CultureInfo? Match(string? requested, IList<string>? supported) =>
        LanguageTag.Match(requested, supported) is { } name && RaskCultureResolver.TryResolve(name, out var culture)
            ? culture
            : null;
}
