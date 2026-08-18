using System.Security.Claims;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Native.Authentication;

/// <summary>
///     Native-side <see cref="IAuthSignIn" />. Sign-out clears any client-held bearer token
///     (<see cref="ITokenStore" />), tells the server to invalidate the session when the app has an
///     <see cref="HttpClient" /> to reach it, refreshes <see cref="IUserProvider" />, and navigates to
///     <c>returnUrl</c> in-app.
/// </summary>
/// <remarks>
///     <para>
///         Both server-facing dependencies are optional because a Native + Local app need not have a backend
///         at all — an offline, on-device app signs out purely by dropping its local token. When the app does
///         register an <see cref="HttpClient" /> (the usual case: a device app against a Rask Server), sign-out
///         also posts to <see cref="LogoutPath" />, because clearing the token on the device alone leaves the
///         session valid on the server.
///     </para>
///     <para>
///         Registered by <c>NativeAppHost</c> with <c>TryAdd</c>, so an app with its own auth flow (OIDC via
///         a system browser, a platform keychain) replaces it by registering first.
///     </para>
/// </remarks>
public sealed class NativeAuthSignIn(
    IUserProvider userProvider,
    Navigator navigator,
    ITokenStore? tokenStore = null,
    HttpClient? http = null) : IAuthSignIn
{
    /// <summary>
    ///     The server endpoint sign-out posts to when an <see cref="HttpClient" /> is registered, so the
    ///     server can invalidate the session. Defaults to <c>auth/logout</c>; relative to the client's base
    ///     address.
    /// </summary>
    public string LogoutPath { get; init; } = "auth/logout";

    /// <summary>
    ///     Not supported on the native host, and throws. The device cannot mint its own principal — anything
    ///     the client decides about who it is, it has decided about itself. POST credentials to a server
    ///     endpoint with <see cref="HttpClient" /> and store the token it issues via <see cref="ITokenStore" />.
    /// </summary>
    /// <param name="principal">Unused.</param>
    /// <param name="returnUrl">Unused.</param>
    /// <param name="scheme">Unused.</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    public Task SignInAsync(ClaimsPrincipal principal, string? returnUrl = null, string? scheme = null) =>
        throw new NotSupportedException(
            "NativeAuthSignIn does not support principal-based sign-in. POST credentials to your server " +
            "endpoint via HttpClient and persist the issued token through ITokenStore instead.");

    /// <summary>
    ///     Signs the user out: clears the local token, posts to <see cref="LogoutPath" /> when a server is
    ///     reachable, refreshes the current user, then navigates to <paramref name="returnUrl" />.
    /// </summary>
    /// <param name="returnUrl">Where to go afterwards.</param>
    /// <param name="scheme">Authentication scheme, when the app distinguishes several. Unused here.</param>
    public async Task SignOutAsync(string? returnUrl = null, string? scheme = null)
    {
        // Local first, and unconditionally: if the network call below fails (a device app is offline far more
        // often than a browser tab), the user must still end up signed out on this device rather than
        // stranded in a half-signed-out state holding a live token.
        if (tokenStore is not null)
        {
            await tokenStore.ClearAsync().ConfigureAwait(false);
        }

        if (http is not null)
        {
            await http.PostAsync(LogoutPath, null).ConfigureAwait(false);
        }

        await userProvider.RefreshAsync().ConfigureAwait(false);
        // Open-redirect guard: returnUrl is whatever the caller passed (often a query value that can be
        // attacker-influenced — a native app reaches these through deep links). Collapse anything non-local
        // to "/" at this boundary, the same LocalUrl rule the Server and WASM sign-in paths apply.
        navigator.NavigateTo(LocalUrl.Sanitize(returnUrl));
    }
}
