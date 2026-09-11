namespace Rask.Server.Prerender;

/// <summary>Why a page request was served live instead of from the page cache.</summary>
internal enum PageCacheBypass
{
    /// <summary>Nothing: the request may be answered from the cache.</summary>
    None,

    /// <summary>Not a <c>GET</c> or <c>HEAD</c>.</summary>
    Method,

    /// <summary>
    ///     The URL carries a query string. A page can read it, nothing can tell whether it did, and keying
    ///     on it would let anyone mint copies without limit.
    /// </summary>
    Query,

    /// <summary>The path belongs to an application mounted under its own prefix, such as <c>/_rask</c>.</summary>
    Mounted,

    /// <summary>The path resolved to no page, or to the not-found page.</summary>
    NotFound,

    /// <summary>The path is not one the app planned copies for.</summary>
    Unplanned,

    /// <summary>
    ///     The language came from the URL. That request persists a cookie, which only a live response can
    ///     set.
    /// </summary>
    CultureFromQuery,

    /// <summary>The page, or a layout above it, requires authorization.</summary>
    Protected,

    /// <summary>A recent render showed the page cannot be stored, so asking again would only repeat it.</summary>
    Refused,

    /// <summary>The visitor is signed in and the stored copy depends on who is.</summary>
    SignedInReadsUser,
}

/// <summary>
///     Decides whether a page request may be answered from the page cache.
/// </summary>
/// <remarks>
///     A pure function of facts the handler already has, so the rules read as a truth table and are tested as
///     one. Ordered cheapest first; the first reason that applies is the one reported, and every reason is a
///     request served live exactly as it was before the cache existed.
/// </remarks>
internal static class PageCacheEligibility
{
    /// <summary>
    ///     The reason a request must be served live, or <see cref="PageCacheBypass.None" />.
    /// </summary>
    /// <param name="getOrHead">Whether the method is <c>GET</c> or <c>HEAD</c>.</param>
    /// <param name="hasQuery">Whether the URL carries a query string.</param>
    /// <param name="mounted">Whether the path belongs to a mounted application.</param>
    /// <param name="resolvedToPage">Whether the path resolved to a page other than the not-found page.</param>
    /// <param name="planned">Whether the path is planned.</param>
    /// <param name="cultureFromQuery">Whether the negotiated language came from the URL.</param>
    /// <param name="isPublic">Whether the page takes no authorization.</param>
    internal static PageCacheBypass Evaluate(
        bool getOrHead,
        bool hasQuery,
        bool mounted,
        bool resolvedToPage,
        bool planned,
        bool cultureFromQuery,
        bool isPublic)
    {
        if (!getOrHead)
        {
            return PageCacheBypass.Method;
        }

        if (hasQuery)
        {
            return PageCacheBypass.Query;
        }

        if (mounted)
        {
            return PageCacheBypass.Mounted;
        }

        if (!resolvedToPage)
        {
            return PageCacheBypass.NotFound;
        }

        if (!planned)
        {
            return PageCacheBypass.Unplanned;
        }

        if (cultureFromQuery)
        {
            return PageCacheBypass.CultureFromQuery;
        }

        return isPublic ? PageCacheBypass.None : PageCacheBypass.Protected;
    }

    /// <summary>The metric tag for <paramref name="bypass" />: a fixed string, so tagging allocates nothing.</summary>
    internal static string Tag(PageCacheBypass bypass) => bypass switch
    {
        PageCacheBypass.None => "none",
        PageCacheBypass.Method => "method",
        PageCacheBypass.Query => "query",
        PageCacheBypass.Mounted => "mounted",
        PageCacheBypass.NotFound => "notfound",
        PageCacheBypass.Unplanned => "unplanned",
        PageCacheBypass.CultureFromQuery => "culturequery",
        PageCacheBypass.Protected => "protected",
        PageCacheBypass.Refused => "refused",
        PageCacheBypass.SignedInReadsUser => "readsuser",
        _ => "unknown",
    };
}
