using Company.RaskWasmHosted.Host;
using Rask.Wasm.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Opt into brotli + gzip response compression for the AppBundle (.wasm / .dll / .js / .json).
// UseRask wires UseResponseCompression ahead of UseStaticFiles when this is registered.
builder.Services.AddRask();

var app = builder.Build();

var weatherService = new LocalWeatherForecastService();
app.MapGet("/api/weatherforecast", async () => await weatherService.GetForecastsAsync());

// To host two WASM AppBundles side-by-side, pass a per-app prefix:
//   app.UseRask(pathBase: "/appA");
// or the generic form app.UseRask<App>(pathBase: "/appA"). Pair with
// /p:RaskPathBase=/appA at WASM publish time so the bundled index.html's
// <base href> matches.
app.UseRask();

app.Run();
