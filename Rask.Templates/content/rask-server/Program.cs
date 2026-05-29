using Company.RaskServer;
using Rask.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRask();
builder.Services.AddScoped<IWeatherForecastService, LocalWeatherForecastService>();

var app = builder.Build();

app.MapStaticAssets();

// To host this app under a sub-path (e.g. behind a reverse proxy mapping
// /myapp/* → this server), pass pathBase. Every framework endpoint and
// emitted URL is scoped under the prefix; user-space routes stay unprefixed.
//   app.UseRask<App>(pathBase: "/myapp");
app.UseRask<App>();

app.Run();
