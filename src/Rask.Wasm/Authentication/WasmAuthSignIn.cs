using System.Security.Claims;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Wasm.Authentication;

/// <summary>
///     WASM-side <see cref="IAuthSignIn" /> implementation. Sign-out POSTs to <see cref="LogoutPath" />
///     (default <c>/auth/logout</c>) so the server can clear the auth cookie, then refreshes the
///     <see cref="IUserProvider" /> and SPA-navigates to <c>returnUrl</c> — no full page reload.
///     Sign-in is not supported here: WASM apps validate credentials by POSTing them to a server
///     endpoint that varies per app. Use <see cref="HttpClient" /> directly from the LoginPage.
/// </summary>
public sealed class WasmAuthSignIn(HttpClient http, IUserProvider userProvider, Navigator navigator) : IAuthSignIn
{
    /// <summary>
    ///     The server endpoint that sign-out posts to, so the server can clear the auth cookie. Defaults to
    ///     <c>auth/logout</c>. Signing out in the browser alone would leave the session valid on the
    ///     server, so this path has to exist and actually invalidate it.
    /// </summary>
    public string LogoutPath { get; init; } = "auth/logout";

    /// <summary>
    ///     Not supported in the browser, and throws. A WASM app cannot mint its own principal — anything
    ///     the client decides about who it is, it has decided about itself. Post the credentials to a
    ///     server endpoint with <see cref="HttpClient" /> and let the server issue the identity.
    /// </summary>
    /// <param name="principal">Unused.</param>
    /// <param name="returnUrl">Unused.</param>
    /// <param name="scheme">Unused.</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    public Task SignInAsync(ClaimsPrincipal principal, string? returnUrl = null, string? scheme = null) =>
        throw new NotSupportedException(
            "WasmAuthSignIn does not support principal-based sign-in. " +
            "POST credentials to your server endpoint via HttpClient instead.");

    /// <summary>
    ///     Signs the user out: posts to <see cref="LogoutPath" /> so the server clears the auth cookie,
    ///     refreshes the current user, then navigates to <paramref name="returnUrl" /> without a full page
    ///     reload.
    /// </summary>
    /// <param name="returnUrl">Where to go afterwards.</param>
    /// <param name="scheme">Authentication scheme, when the server distinguishes several.</param>
    public async Task SignOutAsync(string? returnUrl = null, string? scheme = null)
    {
        await http.PostAsync(LogoutPath, null).ConfigureAwait(false);
        await userProvider.RefreshAsync().ConfigureAwait(false);
        // Open-redirect guard: returnUrl is whatever the caller passed (often a login/query value
        // that can be attacker-influenced), and NavigateTo can leave the origin. Collapse anything
        // non-local to "/" at this boundary — the same LocalUrl rule the server sign-in path applies.
        navigator.NavigateTo(LocalUrl.Sanitize(returnUrl));
    }
}
