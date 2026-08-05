using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Rask.Wasm.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "rask.auth";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
builder.Services.AddSingleton<ICredentialStore, DemoCredentialStore>();
builder.Services.AddRask(); // Rask.Wasm.Hosting — response compression for the AppBundle

// Match what `rask deploy` allows: it sends SIGTERM and SIGKILLs 20s later, so the app budgets under that.
// ServicesStopConcurrently is the other half — stopped one at a time (the .NET default) each hosted
// service's own shutdown grace sums inside this one budget instead of overlapping. See docs/deployment.md.
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(15);
    options.ServicesStopConcurrently = true;
});

var app = builder.Build();

// Populates HttpContext.User from the cookie so /api/me reflects the signed-in user. (No UseAuthorization
// — there are no [Authorize] endpoints; the WASM client gates content with the Authorize component.)
app.UseAuthentication();

// Auth API consumed by the WASM client (same origin, so the HttpOnly cookie rides every request).
app.MapPost("/api/login", async (HttpContext ctx, LoginRequest dto, ICredentialStore creds) =>
{
    var claims = creds.Validate(dto.Username, dto.Password);
    if (claims is null)
    {
        return Results.Unauthorized();
    }

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(new ClaimsPrincipal(identity));
    return Results.Ok();
});

app.MapGet("/api/me", (HttpContext ctx) =>
    ctx.User.Identity?.IsAuthenticated == true
        ? Results.Ok(new MeDto(
            ctx.User.Identity!.Name!,
            ctx.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray()))
        : Results.NoContent());

app.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync();
    return Results.Ok();
});

app.UseRask(); // serve the published WASM AppBundle

app.Run();

public sealed record LoginRequest(string Username, string Password);

public sealed record MeDto(string Name, string[] Roles);

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
