<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/rask-logo-dark.svg">
  <img alt="Rask" src="assets/rask-logo.svg" width="300">
</picture>

### The .NET One Person Framework — build, run, and ship a whole product solo, in C#, on one server.

**[Site ↗](https://pal-tamas.github.io/rask/)** · **[Docs ↗](https://pal-tamas.github.io/rask/docs/)** · **[Playground ↗](https://pal-tamas.github.io/rask/playground/)**

</div>

One developer builds, runs and ships a *complete* product — the UI, the data, the auth, the background
work and the deployment — from **one C# codebase on one server**, with **SQLite as the production
database**. Components are plain C# classes that return a tree of HTML from `Render()`: state is a
field, and an event handler is a delegate.

```csharp
[Route("/counter")]
public sealed partial class Counter : Component
{
    private int _count;

    protected override Component? Render() =>
        Button.OnClick(() => _count++)[$"Current count: {_count}"];
}
```

## Install

```bash
curl -sSL https://pal-tamas.github.io/rask/rask.sh | sh
```

On Windows, in PowerShell: `irm https://pal-tamas.github.io/rask/rask.ps1 | iex`.

**Prerequisites: none.** The installer adds whatever is missing — the .NET 10 SDK, the `wasm-tools`
workload, Node — all under `$HOME`, no `sudo`. Already have the SDK and want only the tool?
`dotnet tool install -g Rask.Cli`. See [installation](docs/installation.md).

Adding Rask to a project you already have is one package:

```bash
dotnet add package Rask
```

## Three front ends, one back end

Pick one per project — all three sit on the same C# back end. Islands also compose *inside* a Rask
component tree, so those two mix freely.

### Rask components

C# components, server-rendered, with every state change streaming to the browser as a minimal diff
over a WebSocket. Add `--wasm` and the same components also publish as a WebAssembly bundle from that
same project — no second project, no separate build. The bundle is fetched once the page goes idle, and
a page that can run client-side moves there on the next navigation; until then, and for any page that
reaches a database, it stays live over the socket.

```bash
rask new Shop
```

→ [Render modes](docs/render-modes.md) · [Building components](docs/building-components.md)

### SPA

A TypeScript single-page app on an ASP.NET host — React, Preact, Vue, Angular, Solid, Svelte or Lit.
The client's TypeScript is generated from your C# message records on every build, so
`await rask.dispatch(getOrder({ id }))` is typed and renaming a C# property breaks the build rather
than the wire.

```bash
rask new Shop --template react
```

→ [TypeScript front ends](docs/spa.md)

### Islands

A `.tsx` or Lit file as an *ordinary* Rask component. Derive from `ReactComponent`, drop `Chart.tsx`
beside it, and place it anywhere the chain goes — a leaf inside a card, or a whole route. Props are
declared in C#, callbacks re-enter C# over the channel every handler already uses, and the live diff
leaves the subtree alone because its own renderer owns it. This is the one pillar `Rask` does not
bring on its own: add `Rask.External`, and Node, because your React does.

```csharp
public sealed partial class Chart : ReactComponent
{
    public required IReadOnlyList<Point> Series { get; set; }
}
```

→ [Islands](docs/islands.md)

## Ship it

```bash
rask dev                                                  # run it — the first migration is already applied
rask db add AddProducts && rask db update                 # after you change the model
rask deploy --host root@box --domain shop.example.com     # bare box → Docker + auto-HTTPS, zero-downtime
```

Jobs, mail, cache and events are on by default, and every one of them rides the app's own SQLite
database. Auth is the one you ask for: `rask new Shop --auth` scaffolds a cookie login. Run `rask` with
no arguments for a wizard.

## Documentation

| | |
|---|---|
| **[The One Person Framework](docs/one-person-framework.md)** | The doctrine, the batteries, and why one server beats a rented stack |
| **[Getting started](docs/getting-started.md)** · **[Tutorial](docs/tutorial/00-overview.md)** | The UI end to end; then a whole product, one pillar per chapter |
| **[Building components](docs/building-components.md)** · **[Routing](docs/routing.md)** · **[Forms](docs/forms.md)** | How markup is written, the URLs it answers, and the form pipeline |
| **[The `rask` CLI](docs/cli.md)** · **[Deployment](docs/deployment.md)** | `new` / `dev` / `db` / `deploy`; Docker over SSH, auto-HTTPS, bare-VPS setup |
| **[Data](docs/data.md)** · **[CQRS](docs/cqrs.md)** · **[Auth](docs/authentication.md)** · **[Jobs](docs/jobs.md)** · **[SQLite](docs/sqlite.md)** | The database-backed pillars |
| **[Migrating from Blazor](docs/migration-from-blazor.md)** · **[Diagnostics](docs/diagnostics.md)** | Day-to-day differences side by side; every RASK build error and its fix |

The full index is **[`docs/`](docs/)**, and the other packages are listed in
**[NUGET.md](NUGET.md)**. To read a real app, **[`samples/`](samples/)** runs locally
(`dotnet run --project samples/Rask.Example.Server`).

*Rask* is the Norwegian/Danish/Swedish word for **fast**, and the engine earns it: after first paint a
counter tick on a 24 KB page goes out as ~41 bytes. It ships fewer bytes on the wire than Blazor on
every scenario in the head-to-head suite, allocates ~40× less per update and holds a ~30% leaner
retained tree per mounted page — the numbers, enforced by the local pre-push gate, are in the
**[Rask vs Blazor baselines ↗](benchmarks/Rask.Benchmarks.VsBlazor/Baselines/vs-blazor.md)**.

## Status

Rask is pre-1.0; APIs may change between minor versions. It targets **.NET 10** (`net10.0` for ASP.NET
hosts, `net10.0-browser` for WASM). Production use at your own discretion — issues and PRs welcome.

## License

[MIT](LICENSE).
