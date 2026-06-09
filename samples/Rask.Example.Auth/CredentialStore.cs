using System.Security.Claims;

namespace Rask.Example.Auth;

public interface ICredentialStore
{
    // Returns the claims for a valid (username, password), or null. Demo only — swap for a real store.
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

public sealed class LoginModel
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
