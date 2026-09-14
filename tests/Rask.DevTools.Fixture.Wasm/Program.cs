using Rask.DevTools.Fixture.Wasm;
using Rask.Wasm;

// No logging provider registered: the host writes framework diagnostics to the browser console itself (#1096).
var host = WasmHostBuilder.CreateDefault();
await host.RunAsync<App>();
