using System.Net.Http.Json;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Example.Auth.WasmJwt;

public sealed class JwtLoginService(HttpClient http, TokenStore tokens, IUserProvider users, Navigator nav)
{
    public async Task<bool> LoginAsync(string username, string password, string? returnUrl)
    {
        var resp = await http.PostAsJsonAsync("api/login", new LoginRequest(username, password),
            AuthJson.Default.LoginRequest);
        if (!resp.IsSuccessStatusCode)
        {
            return false;
        }

        var dto = await resp.Content.ReadFromJsonAsync(AuthJson.Default.TokenResponse);
        if (dto is null)
        {
            return false;
        }

        await tokens.SetAsync(dto.Token);
        await users.RefreshAsync();
        nav.Navigate(returnUrl ?? "/members");
        return true;
    }

    public async Task LogoutAsync()
    {
        // Navigate first (still in the handler scope), then clear the token + principal.
        nav.Navigate("/login");
        await tokens.ClearAsync();
        await users.RefreshAsync();
    }
}
