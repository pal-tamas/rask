# Rask.Example.Wasm.Host

Serves the showcase as a **browser-WASM** app. This ASP.NET host publishes and statically
serves the [`Rask.Example.Wasm`](../Rask.Example.Wasm) client (which compiles the shared
components to `net10.0-browser`); after first load the app runs entirely in the browser,
using the `JSImport`/`JSExport` transport instead of a WebSocket.

```bash
dotnet run --project samples/Rask.Example.Wasm.Host
```

## Key files

- `Program.cs` — `UseRask<TApp>()` from `Rask.Wasm.Hosting`, generic so the client
  assembly's `[ModuleInitializer]` (route registration) loads.
- Client: [`Rask.Example.Wasm`](../Rask.Example.Wasm); components:
  [`Rask.Example.Shared`](../Rask.Example.Shared).

Building this project bakes scoped CSS/JS assets into the published bundle via
`BakeScopedAssetsTask`. For the server-side variant, see
[`Rask.Example.Server`](../Rask.Example.Server).
