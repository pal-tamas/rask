using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.DevTools.Fixture.Wasm;
using Rask.Wasm;

var host = WasmHostBuilder.CreateDefault();
// Framework diagnostics to the browser console for hand runs; see FixtureConsoleLoggerProvider.
host.Services.AddSingleton<ILoggerProvider, FixtureConsoleLoggerProvider>();
await host.RunAsync<App>();
