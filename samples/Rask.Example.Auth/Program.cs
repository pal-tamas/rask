using Microsoft.AspNetCore.Authentication.Cookies;
using Rask;
using Rask.Example.Auth;

var app = RaskApp.Create(args);

// Cookie auth — Rask reads HttpContext.User; the sign-in handshake sets this cookie on redeem. Registering
// a scheme is all it takes: RaskApp puts UseAuthentication/UseAuthorization ahead of UseRask on its own,
// which is the order the principal has to be populated in (RASK024), and leaves them out entirely for an
// app that has no scheme.
app.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "rask.auth";
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/forbidden";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
app.Services.AddSingleton<ICredentialStore, DemoCredentialStore>();

app.Run<App>();
