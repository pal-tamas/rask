# Rask samples

Runnable apps that demonstrate Rask end to end. The **showcase** (Server / WASM) is the
feature tour; the **auth** samples are focused, production-shaped login flows; and
`Rask.Example.Shop` is a whole product with every One Person Framework battery wired in.

## The full stack

| Project | What it is | Run |
|---------|------------|-----|
| [`Rask.Example.Shop`](Rask.Example.Shop) | **Every OPF battery in one app** — data, CQRS, outbox, jobs, mail, cache, production SQLite, snapshots, Litestream, Web Push, auth, Docker. Scaffolded with `rask new --all-batteries`; the feature slices are written the way the tutorial teaches them. Its `/ops` page shows every pillar's state in the one SQLite file. | `dotnet run --project samples/Rask.Example.Shop` |
| [`Rask.Example.EfCore`](Rask.Example.EfCore) | A smaller slice of the same story: SQLite + EF Core with transactional mail and a database-backed cache. | `dotnet run --project samples/Rask.Example.EfCore` |
| [`Rask.Example.Sqlite`](Rask.Example.Sqlite) | `UseRaskSqlite` vs plain `UseSqlite` — what the production pragmas actually change. | `dotnet run --project samples/Rask.Example.Sqlite` |

## Showcase

| Project | What it is | Run |
|---------|------------|-----|
| [`Rask.Example.Shared`](Rask.Example.Shared) | The component library — every demo page and primitive lives here. Referenced by both hosts; not run directly. | — |
| [`Rask.Example.Server`](Rask.Example.Server) | The showcase served **server-side** over the WebSocket live runtime. | `dotnet run --project samples/Rask.Example.Server` |
| [`Rask.Example.Wasm`](Rask.Example.Wasm) | The same showcase compiled to **browser-WASM** (standalone SPA). Published, then served by the host below. | (published) |
| [`Rask.Example.Wasm.Host`](Rask.Example.Wasm.Host) | ASP.NET static-file host that serves the published WASM showcase. | `dotnet run --project samples/Rask.Example.Wasm.Host` |

The same `Rask.Example.Shared` components run unchanged under both the Server and WASM
transports — that is the point of the pairing.

### Native (iOS + Android)

The showcase also runs **natively** on a device, inside a platform WebView. Two examples mirror the
Server/WASM pairing, both mounting the same `Rask.Example.Shared.App`:

| Project | What it is | Run |
|---------|------------|-----|
| [`Rask.Example.Native`](Rask.Example.Native) | **Native + Local** — the showcase runs in-process on the device (the native peer of the WASM sample). | `dotnet build samples/Rask.Example.Native/Rask.Example.Native.csproj "-t:Build;Run" -f net10.0-android` |
| [`Rask.Example.Native.Server`](Rask.Example.Native.Server) | **Native + Server** — a thin native shell over a running `Rask.Example.Server` (the native peer of the Server sample). | `dotnet build samples/Rask.Example.Native.Server/Rask.Example.Native.Server.csproj "-t:Build;Run" -f net10.0-android` |

Both multi-target `net10.0-ios;net10.0-android` (use `-f net10.0-ios` on macOS), so they need the mobile
workloads (`dotnet workload install ios android`) and sit **outside `Rask.slnx`** — the Ubuntu CI can't
build those TFMs. A macOS `native` CI job compiles them, and on-device E2E runs via **Appium**
([`tests/Rask.Native.Appium.Tests`](../tests/Rask.Native.Appium.Tests)) in the macOS `native-appium` job.
The reusable asset-serving infrastructure ships in `Rask.Native` (`NativeOriginAssets` +
`NativeAssetHttpHandler`). See the [native guide](../docs/native.md).

## Authentication

Each pairs a login flow with a protected `/members` page. See the
[authentication guide](../docs/authentication.md) for the full production walkthrough.

| Project | Scheme | Host model | Run |
|---------|--------|-----------|-----|
| [`Rask.Example.Auth`](Rask.Example.Auth) | Cookie | Server-side | `dotnet run --project samples/Rask.Example.Auth` |
| [`Rask.Example.Auth.Jwt`](Rask.Example.Auth.Jwt) | JWT (server) | Server-side | `dotnet run --project samples/Rask.Example.Auth.Jwt` |
| `Rask.Example.Auth.WasmCookie` + [`.Host`](Rask.Example.Auth.WasmCookie.Host) | Cookie | WASM client + ASP.NET host | `dotnet run --project samples/Rask.Example.Auth.WasmCookie.Host` |
| `Rask.Example.Auth.WasmJwt` + [`.Host`](Rask.Example.Auth.WasmJwt.Host) | JWT (bearer) | WASM client + ASP.NET host | `dotnet run --project samples/Rask.Example.Auth.WasmJwt.Host` |

For the WASM auth pairs, run the **`.Host`** project — it serves the published WASM client
and exposes the login/API endpoints. The client project is referenced by its host and is
not launched on its own.

## Notes

- All commands run from the repository root.
- Auth is configured on ASP.NET's own `AddCookie` / `AddJwtBearer` — Rask has no auth
  options object. The samples are wired with demo credentials; do not ship them as-is.
