using Company.RaskWasmHosted.Host;
using Rask.Wasm.Hosting;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

var weatherService = new LocalWeatherForecastService();
app.MapGet("/api/weatherforecast", async () => await weatherService.GetForecastsAsync());

app.UseRask();

app.Run();
