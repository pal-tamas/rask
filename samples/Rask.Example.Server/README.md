# Rask.Example.Server

The showcase served **server-side**. The browser holds a thin client; renders and events
flow over a WebSocket, and Rask ships a minimal edit-op diff per update.

```bash
dotnet run --project samples/Rask.Example.Server
```

Then open the printed URL. Every page in the left nav is a runnable demo with its source.

## Key files

- `Program.cs` — `AddRask()` + `UseRask<ShowcaseLayout>()`; the whole host wiring.
- The pages and components come from [`Rask.Example.Shared`](../Rask.Example.Shared).

For the same app in the browser instead, see
[`Rask.Example.Wasm.Host`](../Rask.Example.Wasm.Host).
