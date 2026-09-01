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

## Remote CQRS dispatch, over the same cookie

The members page also dispatches two messages to this host through `IDispatcher` — no `HttpClient` at
the call site, no url. `Shared/RemoteMessages.cs` lives in the SPA and is `Compile`-linked into this
project, so both halves compile **one** declaration of each message; the handlers
(`RemoteHandlers.cs`) exist only here. `AddRaskCqrsClient()` in the SPA sends anything it has a contract
for, `MapRaskCqrs()` here answers it, and the request is same-origin so the `HttpOnly` cookie rides it —
which is why `WhoAmI` can answer with the identity the *server* sees.

See the [CQRS guide](../../docs/cqrs.md).

JWT variant: `Rask.Example.Auth.WasmJwt.Host`. See the
[authentication guide](../../docs/authentication.md). **Demo credentials only — do not ship.**
