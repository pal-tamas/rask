using Company.RaskServer;
using Rask.Server;
//#if (auth)
using Microsoft.AspNetCore.Authentication.Cookies;
//#endif
//#if (pwa)
using Rask.Core.Browser;
//#endif

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRask();
builder.Services.AddScoped<IWeatherForecastService, LocalWeatherForecastService>();
//#if (pwa)

// Installable PWA: AddRaskPwa serves the manifest + service worker and emits the manifest link +
// SW registration into the server-rendered <head>. The app is installable and push-capable, but NOT
// an offline app (a Server app renders over a live WebSocket) — offline navigations show wwwroot/
// offline.html. To send Web Push from this app, add Rask.WebPush; see docs/pwa.md.
builder.Services.AddRaskPwa(new WebAppManifest
{
    Name = "Rask App",
    ShortName = "Rask App",
    ThemeColor = "#512BD4",
    BackgroundColor = "#faf9fe",
    Display = DisplayMode.Standalone,
    Icons = [new ManifestIcon("icon.svg", "any", "image/svg+xml", "any maskable")]
});
//#endif
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
