using Microsoft.AspNetCore.Authentication.Cookies;
using Rask.Example.Auth;
using Rask.Server;

var builder = WebApplication.CreateBuilder(args);

// Cookie auth — Rask reads HttpContext.User; the sign-in handshake sets this cookie on redeem.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "rask.auth";
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/forbidden";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
builder.Services.AddSingleton<ICredentialStore, DemoCredentialStore>();
builder.Services.AddRask();

// Match what `rask deploy` allows: it sends SIGTERM and SIGKILLs 20s later, so the app budgets under that.
// ServicesStopConcurrently is the other half — stopped one at a time (the .NET default) each hosted
// service's own shutdown grace sums inside this one budget instead of overlapping. See docs/deployment.md.
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(15);
    options.ServicesStopConcurrently = true;
});

var app = builder.Build();

app.MapStaticAssets();
app.UseRouting();
// Must precede UseRask so HttpContext.User is populated on the GET and the WS upgrade.
app.UseAuthentication();
app.UseAuthorization();
app.UseRask<App>();

app.Run();
