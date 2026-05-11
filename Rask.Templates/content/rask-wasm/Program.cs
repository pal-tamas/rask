using Company.RaskWasm;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wasm;

var host = WasmHostBuilder.CreateDefault();

host.Services.AddSingleton<IWeatherForecastService, LocalWeatherForecastService>();

await host.RunAsync<App>();
