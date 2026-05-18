using Company.RaskWasmHosted.Host;
using Rask.Wasm.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Opt into brotli + gzip response compression for the AppBundle (.wasm / .dll / .js / .json).
// UseRask wires UseResponseCompression ahead of UseStaticFiles when this is registered.
builder.Services.AddRask();

var app = builder.Build();

var weatherService = new LocalWeatherForecastService();
app.MapGet("/api/weatherforecast", async () => await weatherService.GetForecastsAsync());

app.UseRask();

app.Run();
