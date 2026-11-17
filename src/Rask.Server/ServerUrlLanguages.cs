using Microsoft.AspNetCore.Http;
using Rask.Core.Globalization;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Server;

/// <summary>
///     The languages this host writes into its URLs (<see cref="RaskCultureOptions.UrlPrefix" />), worked out once
///     from its options and handed to every session's <see cref="RouteState" /> as it is made.
/// </summary>
/// <remarks>
///     <para>
///         One per host, not one per process: two hosts in a process may disagree, and a test host beside another
///         must not hand its languages on. The process-wide <see cref="UrlLanguages.IsEnabled" /> is only the fast
///         path that tells a route URL there may be a session to ask.
///     </para>
///     <para>
///         A first request is matched before its session exists, so the GET handler asks this rather than a route.
///     </para>
/// </remarks>
internal sealed class ServerUrlLanguages(RaskCultureOptions? options)
{
    /// <summary>The host's URL languages, or <c>null</c> when it keeps languages out of its URLs.</summary>
    public UrlLanguages? Languages { get; } = UrlLanguages.Enable(options);

    /// <summary><paramref name="path" /> without its language prefix: what the routes are matched against.</summary>
    public string RoutePathOf(string path) => Languages?.Split(path).RoutePath ?? path;

    /// <summary><paramref name="url" />, a page of this app, in the language of the page at <paramref name="address" />.</summary>
    public string Keep(string address, string url) => Languages?.Keep(address, url) ?? url;

    /// <summary>
    ///     <paramref name="html" />, the page a session renders, with its <c>canonical</c> and <c>hreflang</c>
    ///     alternates in its head (<see cref="LanguageAlternates" />) — unchanged when the host keeps languages out of
    ///     its URLs.
    /// </summary>
    /// <remarks>
    ///     Absolute, because a crawler takes nothing else, and written against the origin the request arrived at:
    ///     a server has one, where a prerender's static files must be told theirs. That origin is what the host's
    ///     own filtering (<c>AllowedHosts</c>) and forwarded headers made of it, and every value is HTML-encoded.
    /// </remarks>
    public string AddAlternates(string html, IServiceProvider sessionServices, HttpRequest request)
    {
        if (Languages is not { } languages
            || sessionServices.GetService(typeof(RouteState)) is not RouteState { Language: { } language } route)
        {
            return html;
        }

        var root = request.Scheme + "://" + request.Host.ToUriComponent() + request.PathBase.ToUriComponent()
                   + LiveOptions.PathBase;
        var routePath = route.RoutePath;
        return LanguageAlternates.AddTo(html, languages, language, each => root + languages.Join(each, routePath));
    }
}
