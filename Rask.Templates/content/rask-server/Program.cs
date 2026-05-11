using Company.RaskServer;
using Rask.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRask();
builder.Services.AddScoped<IWeatherForecastService, LocalWeatherForecastService>();

var app = builder.Build();

app.UseRask<App>();

app.Run();
