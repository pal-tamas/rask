using Company.RaskWasmHosted.Wasm;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wasm;

// PathBase is auto-detected from <base href>. Publish with /p:RaskPathBase=/myapp
// for sub-path deploys, or override via CreateDefault(o => o.PathBase = "/myapp").
var host = WasmHostBuilder.CreateDefault();

host.Services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri(WasmHostBuilder.BaseAddress) });
host.Services.AddSingleton<IWeatherForecastService, HttpWeatherForecastService>();

await host.RunAsync<App>();
