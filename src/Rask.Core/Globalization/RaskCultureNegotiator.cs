using System.Globalization;

namespace Rask.Core.Globalization;

/// <summary>Where a negotiated culture came from.</summary>
public enum CultureSource
{
    /// <summary>Nothing matched; the configured default was used.</summary>
    Default,

    /// <summary>The visitor's own preference (<c>Accept-Language</c> / <c>navigator.languages</c>).</summary>
    Client,

    /// <summary>A remembered explicit choice.</summary>
    Cookie,

    /// <summary>An explicit override in the URL.</summary>
    Query,
}

/// <summary>The outcome of negotiation: which cultures to use, and what decided it.</summary>
public readonly record struct CultureNegotiation(
    CultureInfo Culture,
    CultureInfo UICulture,
    CultureSource Source);

/// <summary>
///     Chooses a visitor's culture from the signals a host can offer. Pure, and free of host types, so
///     the decision is unit-testable without a server or a browser.
/// </summary>
/// <remarks>
///     <para>
///         Order: <b>URL → cookie → client preference → default</b>. An explicit act beats a remembered
///         one, which beats an inferred one. Nothing here inspects the route: <b>Rask keeps culture out
///         of the URL path</b>, so one link is the same page for everyone and the router, the generated
///         <c>Url()</c>/<c>Go()</c> helpers, and every route value stay culture-neutral.
///     </para>
///     <para>
///         Matching walks <see cref="CultureInfo.Parent" /> (<c>hu-HU</c> → <c>hu</c>) and will also
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
    ///     Two passes. The first walks the request's own parent chain, so <c>hu-HU</c> is served by a
    ///     supported <c>hu-HU</c> and then by a supported <c>hu</c>. The second compares the neutral
    ///     language of each side, so a request for <c>hu</c> is served by a supported <c>hu-HU</c> —
    ///     the app has the language, just in a region the visitor did not name.
    /// </remarks>
    private static CultureInfo? Match(string? requested, IList<string>? supported)
    {
        if (supported is null || supported.Count == 0 || !RaskCultureResolver.TryResolve(requested, out var wanted))
        {
            return null;
        }

        for (var candidate = wanted; !candidate.Equals(CultureInfo.InvariantCulture); candidate = candidate.Parent)
        {
            foreach (var name in supported)
            {
                if (string.Equals(name, candidate.Name, StringComparison.OrdinalIgnoreCase)
                    && RaskCultureResolver.TryResolve(name, out var exact))
                {
                    return exact;
                }
            }

            // Parent of a neutral culture is the invariant culture, which ends the walk.
            if (candidate.Parent.Equals(candidate))
            {
                break;
            }
        }

        var wantedNeutral = Neutral(wanted);
        foreach (var name in supported)
        {
            if (RaskCultureResolver.TryResolve(name, out var supportedCulture)
                && string.Equals(
                    Neutral(supportedCulture).Name, wantedNeutral.Name, StringComparison.OrdinalIgnoreCase))
            {
                return supportedCulture;
            }
        }

        return null;
    }

    /// <summary>The neutral (language-only) culture behind a specific one: <c>hu-HU</c> → <c>hu</c>.</summary>
    private static CultureInfo Neutral(CultureInfo culture)
    {
        var current = culture;
        while (!current.IsNeutralCulture
               && !current.Equals(CultureInfo.InvariantCulture)
               && !current.Parent.Equals(current)
               && !current.Parent.Equals(CultureInfo.InvariantCulture))
        {
            current = current.Parent;
        }

        return current;
    }
}
