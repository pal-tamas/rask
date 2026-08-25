using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Globalization;

namespace Rask.Server;

/// <summary>
///     Negotiates a visitor's culture from an HTTP request and seeds it onto their live session.
/// </summary>
/// <remarks>
///     <para>
///         This runs at the request layer, beside the code that seeds the principal and the route, and
///         <b>not</b> inside <c>LiveSessionStore</c>: the headers only exist here, and the session store
///         is also used by the resume path, which has its own request to read.
///     </para>
///     <para>
///         Rask does not use <c>RequestLocalizationMiddleware</c> for this, and cannot. That middleware
///         sets the culture for the duration of an HTTP request — but only the FIRST render is an HTTP
///         request. Every subsequent render runs on the WebSocket receive loop, long after the request
///         ended, so a middleware-set culture would be right once and wrong forever after. The culture
///         has to live on the session, which is what <see cref="SessionCulture" /> is. An app may still
///         call <c>UseRequestLocalization()</c> for its own non-Rask endpoints; because Rask reads and
///         writes ASP.NET's own culture cookie, the two agree.
///     </para>
/// </remarks>
internal static class ServerCultureNegotiation
{
    /// <summary>
    ///     Negotiates a culture from <paramref name="request" />, or answers <c>false</c> when the app
    ///     configured no languages.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="Apply" /> because the two happen at different moments on the resume
    ///     path: the signals live on the WebSocket upgrade request, but the session they belong to is
    ///     rebuilt later, in a fresh DI scope, once the client's resume record has been opened.
    /// </remarks>
    public static bool TryNegotiate(
        HttpRequest request,
        IServiceProvider services,
        out CultureNegotiation negotiation)
    {
        negotiation = default;

        var options = services.GetService<RaskCultureOptions>();
        if (options is null || options.SupportedCultures.Count == 0)
        {
            return false;
        }

        negotiation = RaskCultureNegotiator.Negotiate(
            options.UseQueryString ? request.Query[options.QueryKey].ToString() : null,
            options.UseCookie ? request.Cookies[options.CookieName] : null,
            options.UseClientPreference ? ClientLanguages(request) : null,
            options);

        return true;
    }

    /// <summary>Seeds a session's culture, before its first render.</summary>
    public static void Apply(IServiceProvider sessionServices, CultureNegotiation negotiation) =>
        sessionServices.GetRequiredService<SessionCulture>().Seed(negotiation);

    /// <summary>
    ///     Remembers a culture that arrived in the URL, and declares what the response varied on.
    /// </summary>
    /// <remarks>
    ///     Writing the cookie here is the one place a server-side culture cookie is free — the response
    ///     is already open. Without it a shared <c>?culture=hu</c> link would switch exactly one page
    ///     load and then snap back on the next navigation, which reads as the feature being broken.
    ///     Only a culture that came from the URL is persisted: a cookie that merely round-trips is
    ///     already stored, and an <c>Accept-Language</c> match is an inference, not a choice.
    /// </remarks>
    public static void Persist(HttpResponse response, CultureNegotiation negotiation, RaskCultureOptions options)
    {
        // The shell is already Cache-Control: no-store (it embeds the session id), so Vary changes no
        // caching decision today. It is still correct to declare, and it is what keeps the page honest
        // if that policy is ever relaxed.
        response.Headers.Vary = options.UseCookie ? "Accept-Language, Cookie" : "Accept-Language";

        if (negotiation.Source != CultureSource.Query || !options.UseCookie)
        {
            return;
        }

        response.Cookies.Append(
            options.CookieName,
            RaskCultureCookie.Format(negotiation.Culture.Name, negotiation.UICulture.Name),
            new Microsoft.AspNetCore.Http.CookieOptions
            {
                MaxAge = TimeSpan.FromDays(options.CookieMaxAgeDays),
                Path = "/",
                // Deliberately NOT HttpOnly: the WASM host reads this cookie before the runtime boots,
                // to stamp lang/dir on the document and avoid a flash of the wrong language. It carries
                // a language tag and never a credential.
                HttpOnly = false,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = response.HttpContext.Request.IsHttps,
            });
    }

    /// <summary>
    ///     The visitor's languages, most-preferred first, from <c>Accept-Language</c>.
    /// </summary>
    /// <remarks>
    ///     Parsed with ASP.NET's own typed header reader rather than by splitting on commas, so quality
    ///     values, whitespace and malformed entries are handled the way the rest of the stack handles
    ///     them. <c>q=0</c> means "explicitly not this one" and is dropped rather than ranked last.
    /// </remarks>
    private static IReadOnlyList<string>? ClientLanguages(HttpRequest request)
    {
        var header = request.GetTypedHeaders().AcceptLanguage;
        if (header is not { Count: > 0 })
        {
            return null;
        }

        var ranked = new List<string>(header.Count);
        foreach (var entry in header.OrderByDescending(static h => h.Quality ?? 1d))
        {
            if ((entry.Quality ?? 1d) <= 0d)
            {
                continue;
            }

            var value = entry.Value.Value;
            if (!string.IsNullOrWhiteSpace(value) && value != "*")
            {
                ranked.Add(value);
            }
        }

        return ranked.Count > 0 ? ranked : null;
    }
}
