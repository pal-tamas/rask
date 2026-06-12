using Company.RaskWasmHosted.Host;
using Rask.Wasm.Hosting;
//#if (auth)
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
//#endif

var builder = WebApplication.CreateBuilder(args);

// Opt into brotli + gzip response compression for the published wwwroot (.wasm / .js / .json).
builder.Services.AddRask();
//#if (auth)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "rask.auth";
        // Secure-by-default: HTTPS-only and SameSite=Lax (so the cookie doesn't ride cross-site
        // POSTs — the primary CSRF mitigation for the /api/login POST below). The dev launch
        // profile runs on HTTPS; relax SecurePolicy only if you must serve over plain HTTP.
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;
    });
builder.Services.AddSingleton<ICredentialStore, DemoCredentialStore>();
//#endif

var app = builder.Build();

// Transport security (applies whether or not auth is enabled): redirect HTTP→HTTPS, and in
// non-Development emit HSTS so browsers refuse plain-HTTP for the configured max-age.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
//#if (auth)
// Populates HttpContext.User from the cookie so /api/me reflects the signed-in user.
app.UseAuthentication();
// Present so a [Authorize]/RequireAuthorization() you add to an endpoint is actually enforced.
app.UseAuthorization();
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
