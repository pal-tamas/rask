using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Rask.Wasm.Hosting;

var builder = WebApplication.CreateBuilder(args);

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
    builder.Configuration["Jwt:Key"] ?? JwtIssuer.DevKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "rask-jwt-demo",
            ValidateAudience = true,
            ValidAudience = "rask-jwt-demo",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<ICredentialStore, DemoCredentialStore>();
builder.Services.AddSingleton(new JwtIssuer(signingKey));
builder.Services.AddRask(); // Rask.Wasm.Hosting — response compression for the AppBundle

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/login", (LoginRequest dto, ICredentialStore creds, JwtIssuer jwt) =>
{
    var ok = creds.Validate(dto.Username, dto.Password);
    return ok is null
        ? Results.Unauthorized()
        : Results.Ok(new TokenResponse(jwt.Issue(ok.Value.Name, ok.Value.Roles)));
});

app.MapGet("/api/me", (HttpContext ctx) => new MeDto(
        ctx.User.Identity!.Name!,
        ctx.User.FindAll("role").Select(c => c.Value).ToArray()))
    .RequireAuthorization();

app.UseRask(); // serve the published WASM AppBundle

app.Run();

public sealed record LoginRequest(string Username, string Password);
public sealed record TokenResponse(string Token);
public sealed record MeDto(string Name, string[] Roles);

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

public sealed class JwtIssuer(SymmetricSecurityKey key)
{
    public const string DevKey = "rask-jwt-demo-insecure-dev-signing-key-change-me-please-0123456789";

    public string Issue(string name, string[] roles)
    {
        var claims = new List<Claim> { new("name", name) };
        foreach (var r in roles)
        {
            claims.Add(new Claim("role", r));
        }

        var token = new JwtSecurityToken(
            issuer: "rask-jwt-demo",
            audience: "rask-jwt-demo",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
