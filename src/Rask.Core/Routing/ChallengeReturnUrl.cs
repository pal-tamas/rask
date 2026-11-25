using Rask.Core.Authorization;

namespace Rask.Core.Routing;

/// <summary>
///     The way back a sign-in challenge carries: <c>?returnUrl=</c> and the page the reader was sent away from.
/// </summary>
/// <remarks>
///     A challenge of the sign-in page itself — a layout that guards it, a cookie that keeps being refused — would
///     otherwise wrap the address it already carries, escaped once more each time round, until the request is refused
///     (431). The way back the sign-in page already carries is carried instead, so a challenge asked again is the same
///     address; and one past <see cref="LocalUrl.MaxLength" /> is the root (<see cref="LocalUrl.Sanitize" />).
/// </remarks>
internal static class ChallengeReturnUrl
{
    /// <summary>The query parameter the way back travels in.</summary>
    public const string Parameter = "returnUrl";

    // Each unwrap takes a strictly shorter address, so this only bounds what a crafted address can cost.
    private const int MaxUnwraps = 8;

    /// <summary><c>?returnUrl=</c> and <see cref="For" /> of <paramref name="pageUrl" />, escaped once.</summary>
    /// <param name="pageUrl">The page asked for, path and query, as the browser addresses it.</param>
    /// <param name="routePathOf">Takes a language prefix off a path; null when the app has none.</param>
    public static string Query(string pageUrl, Func<string, string>? routePathOf = null) =>
        "?" + Parameter + "=" + Uri.EscapeDataString(For(pageUrl, routePathOf));

    /// <summary>
    ///     The local address a challenge of <paramref name="pageUrl" /> sends the reader back to: the address itself,
    ///     or, when it is the sign-in page, the way back that page already carries (the root when it carries none).
    /// </summary>
    /// <param name="pageUrl">The page asked for, path and query, as the browser addresses it.</param>
    /// <param name="routePathOf">Takes a language prefix off a path; null when the app has none.</param>
    public static string For(string? pageUrl, Func<string, string>? routePathOf = null)
    {
        var url = LocalUrl.Sanitize(pageUrl);
        for (var i = 0; i < MaxUnwraps && IsSignInPage(PathOf(url), routePathOf); i++)
        {
            url = LocalUrl.Sanitize(WayBackOf(url));
        }

        return url;
    }

    private static bool IsSignInPage(string path, Func<string, string>? routePathOf) =>
        string.Equals(routePathOf?.Invoke(path) ?? path, RouteAuthorizationGuard.ChallengePath, StringComparison.OrdinalIgnoreCase);

    // The path alone, without its query or fragment, and without a trailing slash past the root.
    internal static string PathOf(string url)
    {
        var end = url.AsSpan().IndexOfAny('?', '#');
        var path = end < 0 ? url : url[..end];
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    // The returnUrl the address carries, decoded once; null when it carries none.
    private static string? WayBackOf(string url)
    {
        var start = url.IndexOf('?', StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var end = url.IndexOf('#', start);
        var query = QueryString.Parse(end < 0 ? url[start..] : url[start..end]);
        return query.TryGetValue(Parameter, out var values) && values.Count > 0 ? values[0] : null;
    }
}
