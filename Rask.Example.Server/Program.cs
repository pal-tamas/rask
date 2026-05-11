using Rask.Example.Components;
using Rask.Server;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRask();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/forbidden";
    });
builder.Services.AddAuthorization();

builder.Services.AddScoped<IWeatherForecastService, LocalWeatherForecastService>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseRask<App>();

app.Run();
