# Rask.Example.Auth.WasmJwt.Host

**JWT** (bearer) authentication for a **browser-WASM** client. This ASP.NET host serves the
published `Rask.Example.Auth.WasmJwt` SPA and issues JWTs from a login endpoint; the client
stores the token, attaches it as `Authorization: Bearer`, and gates a protected `/members`
page.

```bash
dotnet run --project samples/Rask.Example.Auth.WasmJwt.Host
```

Run **this** host project (not the client) — it serves the WASM bundle and issues tokens.

## How it fits together

- `Rask.Example.Auth.WasmJwt` (`net10.0-browser`) — the SPA: login page, token store, user
  provider, member gate. Referenced by this host; not launched on its own.
- This host (`net10.0`) — `Program.cs` signs and issues JWTs and serves the client via
  `UseRask<TApp>()` from `Rask.Wasm.Hosting`. The token rides the live-runtime WebSocket as
  `?access_token=`.

Cookie variant: `Rask.Example.Auth.WasmCookie.Host`. See the
[authentication guide](../../docs/authentication.md). **Demo credentials only — do not ship.**
