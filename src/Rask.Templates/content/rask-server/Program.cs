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
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/forbidden";
    });
builder.Services.AddSingleton<ICredentialStore, DemoCredentialStore>();
//#endif

var app = builder.Build();

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
