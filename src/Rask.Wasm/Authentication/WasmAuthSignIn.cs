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
    public string LogoutPath { get; init; } = "auth/logout";

    public Task SignInAsync(ClaimsPrincipal principal, string? returnUrl = null, string? scheme = null) =>
        throw new NotSupportedException(
            "WasmAuthSignIn does not support principal-based sign-in. " +
            "POST credentials to your server endpoint via HttpClient instead.");

    public async Task SignOutAsync(string? returnUrl = null, string? scheme = null)
    {
        await http.PostAsync(LogoutPath, null).ConfigureAwait(false);
        await userProvider.RefreshAsync().ConfigureAwait(false);
        navigator.Navigate(returnUrl ?? "/");
    }
}
