using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Example.Shared.Demos;

// A toggleable IUserProvider for the User-gating showcase. Real apps back IUserProvider with a
// cookie/JWT (Server) or /api/me (WASM); this one just flips an in-memory principal so the demo
// can show authenticated/role-gated rendering without real auth infrastructure. Registered as
// both itself (so the demo can sign in/out) and IUserProvider (so Component.User resolves it).
public sealed class DemoUserProvider : IUserProvider
{
    public ClaimsPrincipal Current { get; private set; } = new(new ClaimsIdentity());

    public event Action? Changed;

    public void SignIn(string name, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        // A ClaimsIdentity with a non-null authenticationType is treated as authenticated.
        Current = new ClaimsPrincipal(new ClaimsIdentity(claims, "demo"));
        Changed?.Invoke();
    }

    public void SignOut()
    {
        Current = new ClaimsPrincipal(new ClaimsIdentity());
        Changed?.Invoke();
    }
}
