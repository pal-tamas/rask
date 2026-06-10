using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Company.RaskWasmHosted.Wasm;

public sealed record LoginRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password);

public sealed record MeDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("roles")] string[] Roles);

public sealed class LoginModel
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

// Source-generated JSON keeps the WASM publish trim-clean (zero IL warnings).
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(MeDto))]
public partial class AuthJson : JsonSerializerContext;

// Hydrates the principal from the host's /api/me (the HttpOnly cookie rides the same-origin request).
public sealed class ApiUserProvider(HttpClient http) : IUserProvider
{
    private ClaimsPrincipal _current = new(new ClaimsIdentity());
    public ClaimsPrincipal Current => _current;
    public bool IsLoading { get; private set; }
    public event Action? Changed;

    public Task EnsureLoadedAsync()
    {
        IsLoading = true; // bridge the anonymous→authed flash (LoadAsync's finally clears it)
        return LoadAsync();
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
            // GetAsync (not GetFromJsonAsync): an anonymous /api/me returns 204 No Content, and
            // GetFromJsonAsync would throw a JsonException deserializing the empty body. Treat
            // anything but a 200-with-body as anonymous.
            using var resp = await http.GetAsync("api/me");
            var me = resp.StatusCode == System.Net.HttpStatusCode.OK
                ? await resp.Content.ReadFromJsonAsync(AuthJson.Default.MeDto)
                : null;
            _current = me is { Name: { } name }
                ? new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, name), .. me.Roles.Select(r => new Claim(ClaimTypes.Role, r))], "api"))
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

public sealed class WasmLoginService(HttpClient http, IUserProvider users, Navigator nav)
{
    public async Task<bool> LoginAsync(string username, string password, string? returnUrl)
    {
        var resp = await http.PostAsJsonAsync("api/login", new LoginRequest(username, password), AuthJson.Default.LoginRequest);
        if (!resp.IsSuccessStatusCode) return false;
        await users.RefreshAsync();
        // Open-redirect guard: an attacker-supplied returnUrl must never navigate off-origin.
        nav.Navigate(LocalUrl.Sanitize(returnUrl ?? "/members"));
        return true;
    }

    public async Task LogoutAsync()
    {
        await http.PostAsync("auth/logout", null);
        // Navigate first (still in the handler scope), then clear the principal.
        nav.Navigate("/login");
        await users.RefreshAsync();
    }
}
