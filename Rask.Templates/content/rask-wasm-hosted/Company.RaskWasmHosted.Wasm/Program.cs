using Company.RaskWasmHosted.Wasm;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wasm;

var host = WasmHostBuilder.CreateDefault();

host.Services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri(WasmHostBuilder.BaseAddress) });
host.Services.AddSingleton<IWeatherForecastService, HttpWeatherForecastService>();

await host.RunAsync<App>();
