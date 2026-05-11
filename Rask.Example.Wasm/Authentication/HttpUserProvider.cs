using System.Net.Http.Json;
using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Example.Wasm.Authentication;

public sealed class HttpUserProvider(HttpClient http) : IUserProvider
{
    private Task? _initialLoad;

    public ClaimsPrincipal Current { get; private set; } = new(new ClaimsIdentity());

    public event Action? Changed;

    public Task EnsureLoadedAsync() => _initialLoad ??= RefreshAsync();

    public async Task RefreshAsync()
    {
        try
        {
            var me = await http.GetFromJsonAsync<MeResponse>("api/me").ConfigureAwait(false);
            var next = BuildPrincipal(me);
            var prev = Current;
            if (Equivalent(prev, next))
            {
                return;
            }

            Current = next;
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[Rask.Example.Wasm] /api/me failed: {ex.Message}");
        }
    }

    private static bool Equivalent(ClaimsPrincipal a, ClaimsPrincipal b)
    {
        var ai = a.Identity;
        var bi = b.Identity;
        return ai?.IsAuthenticated == bi?.IsAuthenticated && string.Equals(ai?.Name, bi?.Name, StringComparison.Ordinal);
    }

    private static ClaimsPrincipal BuildPrincipal(MeResponse? me)
    {
        if (me is null || !me.IsAuthenticated)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var claims = me.Claims?.Select(c => new Claim(c.Type, c.Value)).ToArray() ?? Array.Empty<Claim>();
        var identity = new ClaimsIdentity(claims, authenticationType: "Cookies", ClaimTypes.Name, ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }

    private sealed record MeResponse(bool IsAuthenticated, string? Name, ClaimDto[]? Claims);

    private sealed record ClaimDto(string Type, string Value);
}
