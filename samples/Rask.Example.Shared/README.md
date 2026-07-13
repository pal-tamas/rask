# Rask.Example.Shared

The heart of the showcase: a component library holding every demo page and primitive. It
targets `net10.0` and is referenced unchanged by both the
[Server](../Rask.Example.Server) and [WASM](../Rask.Example.Wasm) hosts — proving the same
components run under either transport.

Not runnable on its own. Launch it through a host:

```bash
dotnet run --project samples/Rask.Example.Server      # server-side live runtime
dotnet run --project samples/Rask.Example.Wasm.Host   # browser-WASM
```

## Layout

| Folder    | Contents                                                                                             |
|-----------|------------------------------------------------------------------------------------------------------|
| `Pages/`  | One routed page per feature (`[Route]`), each pairing a runnable demo with its source.               |
| `Demos/`  | Reusable demo components used by the pages (e.g. `ContextDemos`, `CallbackDemos`, `ElementRefDemo`). |
| `Layout/` | `ShowcaseLayout` — the shell, sidebar nav, and the route table that drives both.                     |

The site is guides-first: `Features/Guides/GuidesIndexPage.cs` is the root (`/`), rendering the
`GuideCatalog` (the `docs/*.md` guides) as grouped cards. The sidebar in `Shared/ShowcaseLayout.cs`
leads with those guides, then the demoted interactive Examples.
