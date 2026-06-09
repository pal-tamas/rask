using System.Net.Http.Json;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Example.Auth.WasmCookie;

// WASM sign-in: POST credentials to the host's /api/login (it sets the HttpOnly cookie), then refresh the
// provider from /api/me and navigate. (WasmAuthSignIn.SignInAsync intentionally throws — credentials go to
// your own endpoint; only sign-out is framework-provided.)
public sealed class WasmLoginService(HttpClient http, IUserProvider users, Navigator nav)
{
    public async Task<bool> LoginAsync(string username, string password, string? returnUrl)
    {
        var resp = await http.PostAsJsonAsync("api/login", new LoginRequest(username, password),
            AuthJson.Default.LoginRequest);
        if (!resp.IsSuccessStatusCode)
        {
            return false;
        }

        await users.RefreshAsync();
        // Open-redirect guard: never navigate off-origin from an attacker-supplied returnUrl
        // (parity with the server's SanitizeReturnUrl). Unsafe values collapse to "/".
        nav.Navigate(LocalUrl.Sanitize(returnUrl ?? "/members"));
        return true;
    }

    public async Task LogoutAsync()
    {
        await http.PostAsync("auth/logout", null);
        // Navigate first (while still in the click-handler scope), then clear the principal — refreshing
        // first would close the Authorize gate and unmount this component before the navigation runs.
        nav.Navigate("/login");
        await users.RefreshAsync();
    }
}
