using Rask.DevTools.Fixture.Wasm;
using Rask.Wasm;

var host = WasmHostBuilder.CreateDefault();
await host.RunAsync<App>();
