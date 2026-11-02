using Rask.Auth.Client;
using Rask.Wasm;
using Rask.Wasm.Fixture.Wasm;

// Who is signed in comes from the host's /api/auth/me, as a Rask.Auth server answers it.
var host = WasmHostBuilder.CreateDefault();
host.Services.AddRaskAuthClient();
await host.Run<App>();
