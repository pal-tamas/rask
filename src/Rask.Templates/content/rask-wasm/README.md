# Company.RaskWasm

A standalone browser-WASM [Rask](https://github.com/pal-tamas/rask) SPA (`net10.0-browser`).
It runs entirely in the browser using the `JSImport`/`JSExport` transport — there is no
ASP.NET host of its own.

## Run

```bash
dotnet run
```

## Publish

```bash
dotnet publish -c Release
```

Publishing trims the app and bakes scoped CSS/JS assets into the bundle; serve the output
from any static-file host. Keep the publish IL-trim-clean — new reflection needs a
`[DynamicallyAccessedMembers]` annotation or a justified suppression.

## Layout

- `Program.cs` — `WasmHostBuilder.CreateDefault()` + `RunAsync<App>()`.
- `App.cs` — the root component (renders the full shell).
- `HomePage.cs` (+ `HomePage.css`), `Counter.cs`, `Weather.cs` — pages and components.

## Authentication

Scaffolded with `--auth`. Because a standalone SPA has no host, this template adds a **JWT
bearer** login against an *external* API: the token is held in storage and attached as
`Authorization: Bearer`. Point `BaseAddress` at your auth API. Without `--auth` the `Auth/`
files are omitted.

> Want the client and its API in one solution instead? Use the `rask-wasm-hosted` template.

Next steps: the [Rask docs](https://github.com/pal-tamas/rask/tree/main/docs).
