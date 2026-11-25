using Rask.Core.Authentication;
using Rask.Core.Authorization;
using Rask.Core.Routing;

namespace Rask.Server.Authentication;

/// <summary>
///     Where the browser goes once a live handler's sign-in or sign-out ticket is redeemed: the return address the
///     handler named when it is a page to land on, else the root.
/// </summary>
/// <remarks>
///     <para>
///         Off-site addresses already collapse (<see cref="LocalUrl" />). So does an address that names Rask's own
///         machinery under <c>/_rask/</c> — the ticket redeem is a POST, and loading it is an error page — and, after a
///         sign-in, the sign-in page itself: a reader who just signed in was sent back to the form they filled in, and
///         a page that answers a signed-in reader by asking them to sign in again goes round for ever.
///     </para>
///     <para>A sign-out may land on the sign-in page: that is where a reader who just left goes.</para>
/// </remarks>
internal static class AuthReturnUrl
{
    private const string RaskPaths = "/_rask";

    /// <summary>The address the browser loads after <paramref name="action" />.</summary>
    /// <param name="action">What the ticket does.</param>
    /// <param name="returnUrl">The return address the handler named.</param>
    /// <param name="route">The session's router, which takes a language prefix off a path; null when there is none.</param>
    public static string For(AuthAction action, string? returnUrl, RouteState? route)
    {
        var url = LocalUrl.Sanitize(returnUrl);
        var path = PathOf(url);
        if (IsRasks(path) || (action == AuthAction.SignIn && IsSignInPage(route?.RoutePathOf(path) ?? path)))
        {
            return "/";
        }

        return url;
    }

    // The path alone, without its query or fragment, and without a trailing slash past the root.
    private static string PathOf(string url)
    {
        var end = url.AsSpan().IndexOfAny('?', '#');
        var path = end < 0 ? url : url[..end];
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    private static bool IsRasks(string path) =>
        path.StartsWith(RaskPaths, StringComparison.OrdinalIgnoreCase)
        && (path.Length == RaskPaths.Length || path[RaskPaths.Length] == '/');

    private static bool IsSignInPage(string path) =>
        string.Equals(path, RouteAuthorizationGuard.ChallengePath, StringComparison.OrdinalIgnoreCase);
}
