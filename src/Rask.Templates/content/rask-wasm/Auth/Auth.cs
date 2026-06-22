using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Company.RaskWasm;

public sealed record LoginRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password);

public sealed record TokenResponse([property: JsonPropertyName("token")] string Token);

public sealed record MeDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("roles")] string[] Roles);

public sealed class LoginModel
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(MeDto))]
public partial class AuthJson : JsonSerializerContext;

// Bearer JWT in localStorage (survives refresh) + an in-memory copy the handler reads synchronously.
// SECURITY: a token in localStorage is plaintext and readable by ANY script on the page (XSS), so this
// scaffolded store is a development-grade floor. Before production, prefer an HttpOnly cookie (the token
// never reaches JS) or encrypt at rest with ProtectedTokenStore — see docs/authentication.md. The
// WarnOnce below logs a one-time reminder to the browser console while this plaintext store is in use.
public sealed class TokenStore(IJSRuntime js)
{
    private bool _warned;

    public string? Token { get; private set; }

    public async Task InitAsync()
    {
        Token = await js.InvokeAsync<string?>("localStorage.getItem", "rask.jwt");
        if (Token is not null)
        {
            await WarnOnceAsync();
        }
    }

    public async Task SetAsync(string token)
    {
        Token = token;
        await js.InvokeVoidAsync("localStorage.setItem", "rask.jwt", token);
        await WarnOnceAsync();
    }

    public async Task ClearAsync()
    {
        Token = null;
        await js.InvokeVoidAsync("localStorage.removeItem", "rask.jwt");
    }

    // One-time console warning so a scaffold shipped to production unchanged surfaces the risk.
    // Delete this (and harden the store) once you've moved to an HttpOnly cookie or ProtectedTokenStore.
    private async Task WarnOnceAsync()
    {
        if (_warned)
        {
            return;
        }

        _warned = true;
        await js.InvokeVoidAsync("console.warn",
            "Rask: the bearer token is stored in plaintext localStorage and is readable by any script "
            + "(XSS risk). This is a development floor — for production use an HttpOnly cookie or encrypt "
            + "the token at rest (ProtectedTokenStore). See docs/authentication.md.");
    }
}

public sealed class BearerTokenHandler(TokenStore tokens) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (tokens.Token is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, ct);
    }
}

public sealed class JwtUserProvider(HttpClient http, TokenStore tokens) : IUserProvider
{
    private ClaimsPrincipal _current = new(new ClaimsIdentity());
    public ClaimsPrincipal Current => _current;
    public bool IsLoading { get; private set; }
    public event Action? Changed;

    public async Task EnsureLoadedAsync()
    {
        IsLoading = true; // bridge the anonymous→authed flash (LoadAsync's finally clears it)
        await tokens.InitAsync();
        await LoadAsync();
    }

    public async Task RefreshAsync()
    {
        IsLoading = true;
        Changed?.Invoke();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            if (tokens.Token is null)
            {
                _current = new ClaimsPrincipal(new ClaimsIdentity());
                return;
            }

            // GetAsync (not GetFromJsonAsync): a 204 No Content would make GetFromJsonAsync throw a
            // JsonException on the empty body; treat anything but a 200-with-body as anonymous.
            using var resp = await http.GetAsync("api/me");
            var me = resp.StatusCode == System.Net.HttpStatusCode.OK
                ? await resp.Content.ReadFromJsonAsync(AuthJson.Default.MeDto)
                : null;
            _current = me is { Name: { } name }
                ? new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, name), .. me.Roles.Select(r => new Claim(ClaimTypes.Role, r))], "jwt"))
                : new ClaimsPrincipal(new ClaimsIdentity());
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _current = new ClaimsPrincipal(new ClaimsIdentity());
        }
        finally
        {
            IsLoading = false;
            Changed?.Invoke();
        }
    }
}

public sealed class JwtLoginService(HttpClient http, TokenStore tokens, IUserProvider users, Navigator nav)
{
    public async Task<bool> LoginAsync(string username, string password, string? returnUrl)
    {
        var resp = await http.PostAsJsonAsync("api/login", new LoginRequest(username, password), AuthJson.Default.LoginRequest);
        if (!resp.IsSuccessStatusCode) return false;
        var dto = await resp.Content.ReadFromJsonAsync(AuthJson.Default.TokenResponse);
        if (dto is null) return false;
        await tokens.SetAsync(dto.Token);
        await users.RefreshAsync();
        // Open-redirect guard: an attacker-supplied returnUrl must never navigate off-origin.
        nav.NavigateTo(LocalUrl.Sanitize(returnUrl ?? "/members"));
        return true;
    }

    public async Task LogoutAsync()
    {
        nav.NavigateTo("/login");
        await tokens.ClearAsync();
        await users.RefreshAsync();
    }
}
