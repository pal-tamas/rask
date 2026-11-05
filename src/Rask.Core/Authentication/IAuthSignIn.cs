using System.Security.Claims;

namespace Rask.Core.Authentication;

public interface IAuthSignIn
{
    /// <summary>Signs <paramref name="principal" /> in once the current handler finishes.</summary>
    /// <param name="principal">Who is signing in.</param>
    /// <param name="returnUrl">Where to land afterwards. Sanitized to a local URL before it is used.</param>
    /// <param name="scheme">The authentication scheme, when the app has several.</param>
    /// <param name="persistent">Whether the session should outlive the browser session ("remember me").</param>
    Task SignIn(
        ClaimsPrincipal principal, string? returnUrl = null, string? scheme = null, bool persistent = false);

    /// <summary>
    ///     Signs the visitor in with the app's own token once the current handler finishes: the token is written into
    ///     the session cookie <c>AddRaskCookieSession</c> names, and the browser loads <paramref name="returnUrl" />
    ///     under it.
    /// </summary>
    /// <remarks>
    ///     For an app whose authentication scheme reads its own token from a cookie — a JWT bearer scheme, or a handler
    ///     that looks a session row up — and so cannot sign a principal in itself. Check the credentials first: Rask
    ///     carries the token, it does not judge it.
    /// </remarks>
    /// <param name="token">The app's token for the session it has just opened.</param>
    /// <param name="returnUrl">Where to land afterwards. Sanitized to a local URL before it is used.</param>
    /// <exception cref="InvalidOperationException">The host keeps no session cookie: call <c>AddRaskCookieSession()</c>.</exception>
    Task SignInWithToken(string token, string? returnUrl = null);

    /// <summary>Signs the current visitor out once the current handler finishes.</summary>
    /// <param name="returnUrl">Where to land afterwards. Sanitized to a local URL before it is used.</param>
    /// <param name="scheme">The authentication scheme, when the app has several.</param>
    Task SignOut(string? returnUrl = null, string? scheme = null);
}
