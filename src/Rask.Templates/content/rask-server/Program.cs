using Company.RaskServer;
using Rask.Server;
//#if (auth)
using Microsoft.AspNetCore.Authentication.Cookies;
//#endif

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRask();
builder.Services.AddScoped<IWeatherForecastService, LocalWeatherForecastService>();
//#if (auth)

// Cookie auth — Rask reads HttpContext.User; the sign-in handshake sets this cookie on redeem.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "rask.auth";
        // Secure-by-default: never send the auth cookie over plain HTTP, and use SameSite=Lax so it
        // doesn't ride cross-site POSTs (CSRF). The dev launch profile runs on HTTPS so the cookie
        // is set in development too; if you must serve over plain HTTP, relax SecurePolicy.
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/forbidden";
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

app.MapStaticAssets();
//#if (auth)
// Must precede UseRask so HttpContext.User is populated on the GET and the WS upgrade.
app.UseAuthentication();
app.UseAuthorization();
//#endif

// To host this app under a sub-path (e.g. behind a reverse proxy mapping
// /myapp/* → this server), pass pathBase. Every framework endpoint and
// emitted URL is scoped under the prefix; user-space routes stay unprefixed.
//   app.UseRask<App>(pathBase: "/myapp");
app.UseRask<App>();

app.Run();
