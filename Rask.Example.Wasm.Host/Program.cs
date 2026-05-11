using Rask.Example.Components;
using Rask.Example.Wasm.Host;
using Rask.Wasm.Hosting;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/forbidden";
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/auth/login", AuthEndpoints.LoginAsync);
app.MapPost("/auth/logout", AuthEndpoints.LogoutAsync);
app.MapGet("/api/me", AuthEndpoints.MeAsync);

var weatherService = new LocalWeatherForecastService();
app.MapGet("/api/weatherforecast", async () => await weatherService.GetForecastsAsync());

app.UseRask();

app.Run();
