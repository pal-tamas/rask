using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

namespace Rask.Testing;

/// <summary>
///     Builds the routing state a page under test reads: a <see cref="RouteState" /> at a given URL, and a
///     <see cref="Navigator" /> over it.
/// </summary>
/// <remarks>
///     Seeding a path was already a one-liner (<c>new RouteState { Path = "/orders" }</c>) and is done that
///     way in a dozen places. Seeding a <em>query string</em> was not: <c>RouteState.Query</c> takes an
///     <c>IQueryCollection</c>, which nothing wrapped — so testing a <c>[QueryParam]</c>-bound page meant
///     building one by hand, and most tests simply didn't.
/// </remarks>
public static class TestRoute
{
    /// <summary>
    ///     A <see cref="RouteState" /> at <paramref name="url" />. The query string, if present, is parsed
    ///     and decoded, so <c>"/search?q=hello%20world&amp;page=2"</c> arrives as the page will read it.
    /// </summary>
    /// <param name="url">A path, optionally with a query string. A leading <c>/</c> is added if missing.</param>
    public static RouteState At(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        var split = url.IndexOf('?');
        var path = split < 0 ? url : url[..split];
        var query = split < 0 ? string.Empty : url[(split + 1)..];

        if (path.Length == 0 || path[0] != '/')
        {
            path = "/" + path;
        }

        var state = new RouteState { Path = path };
        if (query.Length > 0)
        {
            state.Query = ParseQuery(query);
        }

        return state;
    }

    // Repeated keys accumulate rather than overwrite, because that is what a real query collection does and
    // what a multi-select or a checkbox group produces: "?tag=a&tag=b" is two values, not the last one.
    private static QueryCollection ParseQuery(string query)
    {
        var store = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = Uri.UnescapeDataString((eq < 0 ? pair : pair[..eq]).Replace('+', ' '));
            var value = eq < 0 ? string.Empty : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));

            if (key.Length == 0)
            {
                continue;
            }

            store[key] = store.TryGetValue(key, out var existing)
                ? StringValues.Concat(existing, value)
                : new StringValues(value);
        }

        return new QueryCollection(store);
    }

    /// <summary>
    ///     A <see cref="Navigator" /> over <paramref name="state" />, wired to <paramref name="downloads" />
    ///     so <c>Navigator.Download</c> works instead of throwing. Pass a <see cref="TestDownloadSink" />
    ///     when the page under test exports anything.
    /// </summary>
    public static Navigator NavigatorFor(RouteState state, IDownloadSink? downloads = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new Navigator(state, downloads);
    }
}
