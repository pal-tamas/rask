# Rask samples

Runnable apps that demonstrate Rask end to end. The **showcase** (Server / WASM) is the
feature tour; the **auth** samples are focused, production-shaped login flows.

## Showcase

| Project | What it is | Run |
|---------|------------|-----|
| [`Rask.Example.Shared`](Rask.Example.Shared) | The component library — every demo page and primitive lives here. Referenced by both hosts; not run directly. | — |
| [`Rask.Example.Server`](Rask.Example.Server) | The showcase served **server-side** over the WebSocket live runtime. | `dotnet run --project samples/Rask.Example.Server` |
| [`Rask.Example.Wasm`](Rask.Example.Wasm) | The same showcase compiled to **browser-WASM** (standalone SPA). Published, then served by the host below. | (published) |
| [`Rask.Example.Wasm.Host`](Rask.Example.Wasm.Host) | ASP.NET static-file host that serves the published WASM showcase. | `dotnet run --project samples/Rask.Example.Wasm.Host` |

The same `Rask.Example.Shared` components run unchanged under both the Server and WASM
transports — that is the point of the pairing.

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
