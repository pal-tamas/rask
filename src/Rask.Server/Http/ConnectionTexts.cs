using System.Collections.Concurrent;
using System.Text.Json;
using Rask.Core.Globalization;

namespace Rask.Server.Http;

/// <summary>
///     The words of the reconnect overlay in the page's language, as the JSON array <c>rask.js</c> reads from the
///     <c>&lt;script type="application/json" id="rask-texts"&gt;</c> the first response carries.
/// </summary>
/// <remarks>
///     <see cref="RaskStrings" /> answers, so an app's translation wins over the kit's and both over the English here,
///     which is also the English <c>rask.js</c> falls back to. The order is the one <c>CONNECTION_TEXTS</c> in
///     <c>rask.ts</c> reads by index.
///     <para>
///         Written into the FIRST response only, as <see cref="HookBundleTag" /> is: the body a session diffs against never
///         contains it, so a live update neither builds nor re-sends it. Cached per language — the key is the negotiated
///         culture, one of the app's own list, never a value from the request.
///     </para>
/// </remarks>
internal static class ConnectionTexts
{
    private static readonly ConcurrentDictionary<string, string> _byLanguage = new(StringComparer.Ordinal);

    /// <summary><paramref name="html" /> with the texts as the last thing in <c>&lt;body&gt;</c>, in the language of this request.</summary>
    /// <param name="html">The page as it was rendered.</param>
    /// <param name="services">The request's services, which hold the negotiated culture.</param>
    public static string AddTo(string html, IServiceProvider services)
    {
        // The LAST one: a </body> can appear inside a script's string.
        var bodyClose = html.LastIndexOf("</body>", StringComparison.Ordinal);
        if (bodyClose < 0)
        {
            return html;
        }

        using var scope = RaskCultureScope.PushFrom(services);
        var tag = string.Concat("<script type=\"application/json\" id=\"rask-texts\" data-rask-managed>", Json(), "</script>");
        return string.Concat(html.AsSpan(0, bodyClose), tag.AsSpan(), html.AsSpan(bodyClose));
    }

    /// <summary>The array for the language in scope.</summary>
    internal static string Json()
    {
        // Without languages configured nothing translates, and the culture is not asked for — which would also mark
        // the component being rendered as depending on it.
        var language = RaskCulture.IsEnabled ? RaskCulture.CurrentUI.Name : string.Empty;
        return _byLanguage.GetOrAdd(language, static _ => JsonSerializer.Serialize(Read()));
    }

    private static string[] Read() =>
    [
        RaskStrings.Get(RaskString.ConnectionReconnecting, "Reconnecting…"),
        RaskStrings.Get(RaskString.ConnectionOffline, "You're offline — waiting to reconnect…"),
        RaskStrings.Get(RaskString.ConnectionStillTrying, "Still trying to reconnect…"),
        RaskStrings.Get(RaskString.ConnectionRetry, "Retry now"),
        RaskStrings.Get(RaskString.ConnectionReload, "Reload"),
        RaskStrings.Get(RaskString.ConnectionUpdating, "Updating…"),
        RaskStrings.Get(RaskString.ConnectionAuthenticating, "Authenticating…"),
        RaskStrings.Get(RaskString.ConnectionSessionTimedOut, "Your session timed out. Reload to continue."),
    ];
}
