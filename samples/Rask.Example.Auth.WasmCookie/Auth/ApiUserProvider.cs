using System.Net.Http.Json;
using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Example.Auth.WasmCookie;

// Real WASM IUserProvider: hydrates the principal from the host's /api/me (the HttpOnly cookie rides the
// same-origin request automatically — the token never touches JS). WasmHostBuilder awaits EnsureLoadedAsync
// before the first render, so there's no anonymous flash.
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
            var me = await http.GetFromJsonAsync("api/me", AuthJson.Default.MeDto);
            _current = me is { Name: { } name }
                ? new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, name), .. me.Roles.Select(r => new Claim(ClaimTypes.Role, r))],
                    "api"))
                : new ClaimsPrincipal(new ClaimsIdentity());
        }
        catch (HttpRequestException)
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
