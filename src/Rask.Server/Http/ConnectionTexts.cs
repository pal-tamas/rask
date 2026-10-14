using System.Collections.Concurrent;
using System.Text.Json;
using Rask.Core;
using Rask.Core.Globalization;

namespace Rask.Server.Http;

/// <summary>
///     The words of the reconnect overlay in the page's language, as the JSON array <c>rask.js</c> reads from the
///     runtime's own <c>&lt;script&gt;</c> (<c>data-rask-texts</c>).
/// </summary>
/// <remarks>
///     <see cref="RaskStrings" /> answers, so an app's translation wins over the kit's and both over the English here,
///     which is also the English <c>rask.js</c> falls back to. The order is the one <c>CONNECTION_TEXTS</c> in
///     <c>rask.ts</c> reads by index. Written once per language: this is asked on every render of every page.
/// </remarks>
internal static class ConnectionTexts
{
    private static readonly ConcurrentDictionary<string, AttrBag> _byLanguage = new(StringComparer.Ordinal);

    /// <summary>The <c>data-rask-texts</c> attribute for the page being rendered, one shared bag per language.</summary>
    /// <remarks>Built once: the runtime script renders with every live update, and a bag made per render was 200 B each.</remarks>
    public static AttrBag Attribute()
    {
        // Without languages configured nothing translates, and the culture is not asked for — which would also mark
        // the component being rendered as depending on it.
        var language = RaskCulture.IsEnabled ? RaskCulture.CurrentUI.Name : string.Empty;
        return _byLanguage.GetOrAdd(language, static _ => new AttrBag("rask-texts", JsonSerializer.Serialize(Read())));
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
