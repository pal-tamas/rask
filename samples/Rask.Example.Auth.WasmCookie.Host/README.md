# Rask.Example.Auth.WasmCookie.Host

**Cookie** authentication for a **browser-WASM** client. This ASP.NET host serves the
published `Rask.Example.Auth.WasmCookie` SPA and exposes the login / `me` / logout
endpoints; the client hydrates the current user and gates a protected `/members` page.

```bash
dotnet run --project samples/Rask.Example.Auth.WasmCookie.Host
```

Run **this** host project (not the client) — it serves the WASM bundle and the auth API.

## How it fits together

- `Rask.Example.Auth.WasmCookie` (`net10.0-browser`) — the SPA: login page, user provider,
  member gate. Referenced by this host; not launched on its own.
- This host (`net10.0`) — `Program.cs` wires `AddCookie(...)`, the credential endpoints,
  and `UseRask<TApp>()` from `Rask.Wasm.Hosting`.

JWT variant: `Rask.Example.Auth.WasmJwt.Host`. See the
[authentication guide](../../docs/authentication.md). **Demo credentials only — do not ship.**
