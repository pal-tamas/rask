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

## Docker

Scaffolded with `--docker`: a multi-stage `Dockerfile` (+ `.dockerignore` + `nginx.conf`) that
publishes the WASM bundle, then serves the static output from a tiny `nginx:alpine` image.

```bash
docker build -t myapp .
docker run --rm -p 8080:80 myapp
```

The bundled `nginx.conf` sets the SPA fallback, the `application/wasm` MIME the runtime needs, and
`gzip_static` to serve the baked `*.gz` assets. See
[docs/deployment.md](https://github.com/pal-tamas/rask/blob/main/docs/deployment.md).

Next steps: the [Rask docs](https://github.com/pal-tamas/rask/tree/main/docs).
