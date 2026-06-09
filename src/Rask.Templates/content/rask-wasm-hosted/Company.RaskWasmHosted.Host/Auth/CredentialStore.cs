using System.Security.Claims;

namespace Company.RaskWasmHosted.Host;

public sealed record LoginRequest(string Username, string Password);
public sealed record MeDto(string Name, string[] Roles);

// Demo credential store — replace with your real user store (ASP.NET Identity, a database, etc.).
public interface ICredentialStore
{
    IReadOnlyList<Claim>? Validate(string username, string password);
}

public sealed class DemoCredentialStore : ICredentialStore
{
    public IReadOnlyList<Claim>? Validate(string username, string password) =>
        (username, password) switch
        {
            ("alice", "password") => [new Claim(ClaimTypes.Name, "alice"), new Claim(ClaimTypes.Role, "user")],
            ("root", "password") => [new Claim(ClaimTypes.Name, "root"), new Claim(ClaimTypes.Role, "admin")],
            _ => null
        };
}
