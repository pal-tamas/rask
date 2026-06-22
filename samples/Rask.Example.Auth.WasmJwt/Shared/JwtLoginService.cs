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
        // Open-redirect guard: never navigate off-origin from an attacker-supplied returnUrl
        // (parity with the server's SanitizeReturnUrl). Unsafe values collapse to "/".
        nav.NavigateTo(LocalUrl.Sanitize(returnUrl ?? "/members"));
        return true;
    }

    public async Task LogoutAsync()
    {
        // Navigate first (still in the handler scope), then clear the token + principal.
        nav.NavigateTo("/login");
        await tokens.ClearAsync();
        await users.RefreshAsync();
    }
}
