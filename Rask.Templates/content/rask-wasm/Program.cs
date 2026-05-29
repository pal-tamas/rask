using Company.RaskWasm;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wasm;

// PathBase is auto-detected at boot from <base href>. For sub-path deploys
// (e.g. GH Pages at https://<user>.github.io/<repo>/), publish with
// /p:RaskPathBase=/<repo> — the framework rewrites the AppBundle's
// index.html so the runtime picks up the prefix on first paint and head-
// emitted asset URLs are scoped under /<repo>/_rask/a/{hash}.{ext}. Override
// explicitly via WasmHostBuilder.CreateDefault(o => o.PathBase = "/myapp")
// if you need to set it from .NET code.
var host = WasmHostBuilder.CreateDefault();

host.Services.AddSingleton<IWeatherForecastService, LocalWeatherForecastService>();

await host.RunAsync<App>();
