using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Rask.Server.Authentication;

namespace Rask.Example.Auth.Jwt;

// Headless. On a fresh session it reads the stored JWT (ProtectedSessionStorage decrypts it server-side),
// validates it, and seeds the live principal — so a refresh stays signed in without the token ever being in
// the URL or readable in JS. JS interop runs once the WebSocket is up; the read completes then and fires a
// re-render via IUserProvider.Changed.
public sealed partial class JwtBootstrap(ProtectedSessionStorage store, JwtValidator validator, SessionUserProvider users)
    : Component
{
    protected override async Task OnMountAsync()
    {
        try
        {
            var result = await store.GetAsync<string>("rask.jwt");
            if (result.Success && result.Value is { } jwt && validator.Validate(jwt) is { } principal)
            {
                users.Set(principal);
            }
        }
        catch
        {
            // No token yet, or JS interop unavailable on the prerender pass — stay anonymous.
        }
    }

    protected override Component? Render() => default;
}
