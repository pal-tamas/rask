using System.Net.Http.Json;
using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Example.Auth.WasmJwt;

// Hydrates the principal from /api/me, which the host validates from the Bearer token (attached by
// BearerTokenHandler). Only calls /api/me when a token is present, so anonymous never hits a 401.
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

            var me = await http.GetFromJsonAsync("api/me", AuthJson.Default.MeDto);
            _current = me is { Name: { } name }
                ? new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, name), .. me.Roles.Select(r => new Claim(ClaimTypes.Role, r))],
                    "jwt"))
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
