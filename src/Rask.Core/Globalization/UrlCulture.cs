using Rask.Core.Diagnostics;
using Rask.Core.Routing;

namespace Rask.Core.Globalization;

/// <summary>
///     The culture of a session whose URLs carry its language (<see cref="RaskCultureOptions.UrlPrefix" />): read
///     off the address, and moved with it.
/// </summary>
/// <remarks>
///     <b>The URL always wins.</b> A deep link opens in the language it names, and nothing renegotiates it after
///     the first render — a German browser on a Hungarian URL reads Hungarian, rather than watching the page
///     change language under an address that did not. Negotiation is asked only when a visitor arrives at the
///     bare root, and its answer is a redirect to that language's root rather than a different page at the same
///     address.
/// </remarks>
internal static class UrlCulture
{
    /// <summary>
    ///     The culture a session arriving at <paramref name="path" /> starts in, and where it is sent on to — a
    ///     first visit to the bare root whose negotiation found another language — or <c>null</c> to stay.
    /// </summary>
    /// <param name="path">The address arrived at, without the deploy's path base or the query.</param>
    /// <param name="languages">The app's URL languages.</param>
    /// <param name="options">The app's languages.</param>
    /// <param name="negotiate">The host's negotiation from its signals, asked only at the bare root.</param>
    public static (CultureNegotiation Culture, string? SendTo) Arrive(
        string path, UrlLanguages languages, RaskCultureOptions options, Func<CultureNegotiation> negotiate)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(negotiate);

        if (path is "" or "/")
        {
            var negotiated = negotiate();
            if (negotiated.Source != CultureSource.Default
                && languages.Resolve(negotiated.UICulture.Name) is { } wanted
                && !LanguageTag.Same(wanted, languages.Default))
            {
                return (negotiated, languages.Join(wanted, "/"));
            }
        }

        var language = languages.Split(path).Language;
        return RaskCultureNegotiator.TrySelect(language, options, out var selection)
            ? (selection, null)
            : (RaskCultureNegotiator.Negotiate(null, null, null, options), null);
    }

    /// <summary>
    ///     Switches <paramref name="culture" /> whenever <paramref name="route" /> moves to an address in another
    ///     language, which also remembers the choice — the address IS the visitor's choice of language.
    /// </summary>
    /// <remarks>
    ///     A move within one language costs a string comparison. The switch raises the culture's own change, so
    ///     the session repaints in the new language exactly as it does for <see cref="IRaskCulture.Set(string)" />.
    /// </remarks>
    public static void Follow(RouteState route, IRaskCulture culture)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(culture);
        if (route.Languages is null)
        {
            return;
        }

        var current = route.Language;
        route.Changed += (_, _) =>
        {
            var language = route.Language;
            if (string.Equals(language, current, StringComparison.Ordinal) || language is null)
            {
                return;
            }

            current = language;
            _ = Switch(culture, language);
        };
    }

    private static async Task Switch(IRaskCulture culture, string language)
    {
        try
        {
            await culture.Set(language).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Only remembering the choice can fail (a cookie write); the culture has already moved.
            RaskDiagnostics.Report(
                RaskLogLevel.Warning, "Rask.Globalization", "[Rask] remembering the URL's language failed", ex);
        }
    }
}
