using Rask.Example.Shared;
using Rask.Wasm;

var host = WasmHostBuilder.CreateDefault();
host.Services.AddExampleServices();
await host.RunAsync<App>();
