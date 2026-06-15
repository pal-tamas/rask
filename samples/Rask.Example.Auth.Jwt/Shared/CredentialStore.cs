using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Rask.Example.Auth.Jwt;

// Demo credential store — replace with your real user store.
public interface ICredentialStore
{
    (string Name, string[] Roles)? Validate(string username, string password);
}

public sealed class DemoCredentialStore : ICredentialStore
{
    public (string Name, string[] Roles)? Validate(string username, string password) =>
        (username, password) switch
        {
            ("alice", "password") => ("alice", new[] { "user" }),
            ("root", "password") => ("root", new[] { "admin" }),
            _ => null
        };
}

public sealed class LoginModel
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

// Issues short-lived HMAC-signed JWTs (claims use short "name"/"role" names; MapInboundClaims off on read
// so they round-trip unchanged).
public sealed class JwtIssuer(IConfiguration config)
{
    // Dev-only default — set Jwt:Key in configuration for anything real.
    public const string DevKey = "rask-jwt-demo-insecure-dev-signing-key-change-me-please-0123456789";

    public static SymmetricSecurityKey KeyFrom(IConfiguration config) =>
        new(Encoding.UTF8.GetBytes(config["Jwt:Key"] ?? DevKey));

    public string Issue(string name, string[] roles)
    {
        var claims = new List<Claim> { new("name", name) };
        foreach (var r in roles)
        {
            claims.Add(new Claim("role", r));
        }

        var token = new JwtSecurityToken(
            "rask-jwt-demo",
            "rask-jwt-demo",
            claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(KeyFrom(config), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

// Validates a raw JWT and builds the ClaimsPrincipal. Used by the login handler (issue → validate → set
// principal, so the JWT is the source of truth) and by the boot read on a fresh session.
public sealed class JwtValidator(IConfiguration config)
{
    public ClaimsPrincipal? Validate(string jwt)
    {
        try
        {
            var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(
                jwt,
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = "rask-jwt-demo",
                    ValidateAudience = true,
                    ValidAudience = "rask-jwt-demo",
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtIssuer.KeyFrom(config),
                    ValidateLifetime = true,
                    NameClaimType = "name",
                    RoleClaimType = "role"
                },
                out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }
}
