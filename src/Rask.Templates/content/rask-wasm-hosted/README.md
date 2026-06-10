# Company.RaskWasmHosted

A browser-WASM [Rask](https://github.com/pal-tamas/rask) app **with an ASP.NET host** — two
projects in one solution:

- `Company.RaskWasmHosted.Wasm` (`net10.0-browser`) — the SPA that runs in the browser.
- `Company.RaskWasmHosted.Host` (`net10.0`) — serves the published WASM client and any
  server APIs. It references the Wasm project across target frameworks
  (`SkipGetTargetFrameworkProperties`) and uses a generic `UseRask<TApp>()` so the client
  assembly's `[ModuleInitializer]` (route registration) loads.

## Run

```bash
dotnet run --project Company.RaskWasmHosted.Host
```

Run the **Host** — it publishes and serves the WASM client. Building the host bakes the
client's scoped CSS/JS assets into the served bundle.

## Publish

```bash
dotnet publish Company.RaskWasmHosted.Host -c Release
```

## Authentication

Scaffolded with `--auth`: **cookie** auth. The host serves the login / `me` / logout
endpoints; the client hydrates the current user and gates a protected `/members` page.
Auth-related files live under each project's `Auth/` folder and are omitted without `--auth`.

Next steps: the [Rask docs](https://github.com/pal-tamas/rask/tree/main/docs).
