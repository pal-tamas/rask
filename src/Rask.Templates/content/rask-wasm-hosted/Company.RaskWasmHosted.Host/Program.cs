using Company.RaskWasmHosted.Host;
using Rask.Wasm.Hosting;
//#if (auth)
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
//#endif

var builder = WebApplication.CreateBuilder(args);

// Opt into brotli + gzip response compression for the AppBundle (.wasm / .dll / .js / .json).
builder.Services.AddRask();
//#if (auth)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o => o.Cookie.Name = "rask.auth");
builder.Services.AddSingleton<ICredentialStore, DemoCredentialStore>();
//#endif

var app = builder.Build();
//#if (auth)
// Populates HttpContext.User from the cookie so /api/me reflects the signed-in user.
app.UseAuthentication();
//#endif

var weatherService = new LocalWeatherForecastService();
app.MapGet("/api/weatherforecast", async () => await weatherService.GetForecastsAsync());
//#if (auth)

// Auth API consumed by the WASM client (same origin, so the HttpOnly cookie rides every request).
app.MapPost("/api/login", async (HttpContext ctx, LoginRequest dto, ICredentialStore creds) =>
{
    var claims = creds.Validate(dto.Username, dto.Password);
    if (claims is null) return Results.Unauthorized();
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(new ClaimsPrincipal(identity));
    return Results.Ok();
});

app.MapGet("/api/me", (HttpContext ctx) =>
    ctx.User.Identity?.IsAuthenticated == true
        ? Results.Ok(new MeDto(ctx.User.Identity!.Name!, ctx.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray()))
        : Results.NoContent());

app.MapPost("/auth/logout", async (HttpContext ctx) => { await ctx.SignOutAsync(); return Results.Ok(); });
//#endif

app.UseRask();

app.Run();
