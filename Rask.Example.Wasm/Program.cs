using Rask.Example.Shared;
using Rask.Wasm;

// Framework default is LiveDiffMode.Auto — counter increments and similar
// in-place state changes go over the wire as a handful of bytes instead of the
// whole rendered body. Open the network panel in the browser to see it.
var host = WasmHostBuilder.CreateDefault();
host.Services.AddExampleServices();
await host.RunAsync<App>();
