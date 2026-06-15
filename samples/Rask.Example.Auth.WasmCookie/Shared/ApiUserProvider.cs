using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Rask.Core.Authentication;

namespace Rask.Example.Auth.WasmCookie;

// Real WASM IUserProvider: hydrates the principal from the host's /api/me (the HttpOnly cookie rides the
// same-origin request automatically — the token never touches JS). WasmHostBuilder awaits EnsureLoadedAsync
// before the first render, so there's no anonymous flash.
public sealed class ApiUserProvider(HttpClient http) : IUserProvider
{
    public ClaimsPrincipal Current { get; private set; } = new(new ClaimsIdentity());

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
            // GetFromJsonAsync would throw a JsonException trying to deserialize the empty body
            // (its try/catch only caught HttpRequestException). Treat anything but a 200-with-body
            // as anonymous.
            using var resp = await http.GetAsync("api/me");
            var me = resp.StatusCode == HttpStatusCode.OK
                ? await resp.Content.ReadFromJsonAsync(AuthJson.Default.MeDto)
                : null;
            Current = me is { Name: { } name }
                ? new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, name), .. me.Roles.Select(r => new Claim(ClaimTypes.Role, r))],
                    "api"))
                : new ClaimsPrincipal(new ClaimsIdentity());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            Current = new ClaimsPrincipal(new ClaimsIdentity());
        }
        finally
        {
            IsLoading = false;
            Changed?.Invoke();
        }
    }
}
