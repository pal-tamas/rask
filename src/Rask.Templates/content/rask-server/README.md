# Company.RaskServer

A server-side [Rask](https://github.com/pal-tamas/rask) app. The browser holds a thin
client; renders and events flow over a WebSocket and Rask ships a minimal diff per update.

> Scaffolded from the `rask-server` template — also what the bare `dotnet new rask` selects.
> For a client-side WebAssembly app instead, use `dotnet new rask-wasm` (or `rask-wasm-hosted`).

## Run

```bash
dotnet run
```

Then open the printed URL.

## Layout

- `Program.cs` — host wiring: `AddRask()` + `UseRask<App>()`.
- `App.cs` — the root component; renders the full page shell (`Doctype`/`Html`/`Head`/`Body`).
- `HomePage.cs` (+ `HomePage.css`) — a routed page with co-located scoped styles.
- `Counter.cs` — an interactive component.
- `Weather.cs` / `LocalWeatherForecastService.cs` — data via DI.

## Authentication

Scaffolded with `--auth`. This template adds **cookie** auth: a `/login` form, a credential
store, and a protected `/members` page (under `Auth/`). Without `--auth` those files are
omitted. Auth is configured on ASP.NET's own `AddCookie` — Rask has no auth options object.

## CQRS

Scaffolded with `--cqrs`. This adds the **Rask.Cqrs** mediator: a sample `GreetingQuery` +
handler and a `/greeting` page that injects `IDispatcher` and dispatches it (under `Cqrs/`),
the `Rask.Cqrs` package reference, and the `AddRaskCqrs()` call in `Program.cs`. Handlers are
discovered at build time (source-generated, reflection-free — trim/AOT-safe). Without `--cqrs`
those files and wiring are omitted. See [docs/cqrs.md](https://github.com/pal-tamas/rask/blob/main/docs/cqrs.md).

Next steps: the [Rask docs](https://github.com/pal-tamas/rask/tree/main/docs).
