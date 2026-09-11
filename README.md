<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/rask-logo-dark.svg">
  <img alt="Rask" src="assets/rask-logo.svg" width="300">
</picture>

### The .NET One Person Framework — build, run, and ship a whole product solo, in C#, on one server.

**[Site ↗](https://rask.sh/)** · **[Docs ↗](https://rask.sh/docs/)**

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
curl -sSL https://rask.sh/rask.sh | sh
```

On Windows, in PowerShell: `irm https://rask.sh/rask.ps1 | iex`.

**Prerequisites: none.** The installer adds whatever is missing — the .NET 10 SDK, the `wasm-tools`
workload, Node — all under `$HOME`, no `sudo`. Already have the SDK and want only the tool?
`dotnet tool install -g Rask.Cli`. See [installation](docs/installation.md).

Adding Rask to a project you already have is one package:

```bash
dotnet add package Rask
```

## Four front ends, one back end

Rask is a superset, not a rival: your React, Vue, Svelte, Angular or Lit components,
[a real Blazor component](docs/blazor-components.md), a TypeScript SPA or a Nuxt or Next.js app all run
on it — and it builds on ASP.NET Core and EF Core rather than replacing them. Pick one per project — all
four sit on the same C# back end. Islands also compose *inside* a Rask component tree, so those two mix
freely.

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

### Islands

A `.tsx`, `.vue`, `.svelte` or Lit file as an *ordinary* Rask component. Derive from
`ReactComponent`, `PreactComponent`, `SolidComponent`, `VueComponent`, `SvelteComponent`,
`AngularComponent` or `LitComponent`, drop the front-end file beside
it, and place it anywhere the chain goes — a leaf inside a card, or a whole route. Props are declared
in C#, callbacks re-enter C# over the channel every handler already uses, and the live diff leaves the
subtree alone because its own renderer owns it. This is the one pillar `Rask` does not bring on its
own: add `Rask.External`, and Node, because your React does.

```csharp
public sealed partial class Chart : ReactComponent
{
    public required IReadOnlyList<Point> Series { get; set; }
}
```

→ [Islands](docs/islands.md)

### SPA

A TypeScript single-page app on an ASP.NET host — React, Preact, Vue, Angular, Solid, Svelte or Lit.
The client's TypeScript is generated from your C# message records on every build, so
`await rask.dispatch(getOrder({ id }))` is typed and renaming a C# property breaks the build rather
than the wire.

```bash
rask new Shop --template react
```

→ [TypeScript front ends](docs/spa.md)

### Meta framework

Nuxt, Next.js, SvelteKit, TanStack Start, SolidStart or Analog owning the *whole* front end — its own
routing, its own rendering, its own Node server — with Rask as the backend behind it. The two ship as
**one container on one port**: Rask fronts every request, supervises Node as a child process and
forwards to it over loopback, so ASP.NET auth, rate limiting, logging and health stay in front of the
framework and the session has one owner. `rask new` runs the framework's *own* creator — `nuxi`,
`create-next-app`, `sv`, `@tanstack/cli` — so what you get is whatever that creator ships today, plus
a node-server build and a dev proxy. Add `Rask.Meta.Hosting`, and Node, because the framework needs it.

```bash
rask new Shop --template nuxt
```

→ [Meta framework front ends](docs/meta.md)

## Ship it

```bash
rask dev                                                  # run it — the first migration is already applied
rask db add AddProducts && rask db update                 # after you change the model
rask deploy --host root@box --domain shop.example.com     # bare box → Docker + auto-HTTPS, zero-downtime
```

Run `rask` with no arguments for a wizard.

## Batteries included

Auth, jobs, mail, cache and events are on by default, and every one of them rides the app's own
SQLite database — no broker, no Redis, no second service to run. A fresh app can register somebody,
sign them in, confirm their address and reset their password with no auth code written.

- **[Auth](docs/authentication.md)** — accounts out of the box: register, sign in, sign out, route
  guards, and the first account to register is the administrator.
- **[Background jobs](docs/jobs.md)** — enqueued, delayed and recurring work on your database,
  at-least-once with exponential backoff.
- **[Email](docs/mail.md)** — transactional mail queued in the same database and delivered over SMTP
  off the request thread; bodies are Rask components.
- **[Outbox](docs/outbox.md)** — domain events committed in the same transaction as your data and
  relayed at-least-once, with no message broker.
- **[Cache](docs/cache.md)** — the standard `IDistributedCache` plus a typed `ICache` with
  `GetOrAddAsync`.
- **[Data](docs/data.md)** · **[CQRS](docs/cqrs.md)** — audit stamps, soft delete, optimistic
  concurrency and domain events on EF Core, and a source-generated, reflection-free mediator.
- **[Production SQLite](docs/sqlite.md)** — WAL and busy-timeout pragmas, continuous Litestream backup,
  scheduled snapshots.
- **[Logging](docs/logging.md)** — the `ILogger` pipeline kept in a SQLite file of its own, with
  retention by age and row count.
- **[Operator console](docs/dashboard.md)** — `/_rask`: queue depth, dead letters and the errors behind
  them, cache contents and a log tail, behind an authorization policy.
- **[PWA](docs/pwa.md)** · **[Web Push](docs/webpush.md)** — installable, offline apps, and push sent
  from your backend on your own VAPID keys.
- **[Deploy](docs/deployment.md)** — `rask deploy` takes a bare VPS to a live HTTPS site with
  zero-downtime swaps.

They build on the standard .NET pieces — EF Core, hosted services, `ILogger`, `IDistributedCache`,
ASP.NET Core authentication — so adding one is a package reference, not a new stack to learn or a new
box to operate.

## Documentation

| | |
|---|---|
| **[The One Person Framework](docs/one-person-framework.md)** | The doctrine, the batteries, and why one server beats a rented stack |
| **[Getting started](docs/getting-started.md)** · **[Tutorial](docs/tutorial/00-overview.md)** | The UI end to end; then a whole product, one pillar per chapter |
| **[Building components](docs/building-components.md)** · **[Routing](docs/routing.md)** · **[Forms](docs/forms.md)** | How markup is written, the URLs it answers, and the form pipeline |
| **[The `rask` CLI](docs/cli.md)** · **[Deployment](docs/deployment.md)** | `new` / `dev` / `db` / `deploy`; Docker over SSH, auto-HTTPS, bare-VPS setup |
| **[Data](docs/data.md)** · **[CQRS](docs/cqrs.md)** · **[Auth](docs/authentication.md)** · **[Jobs](docs/jobs.md)** · **[SQLite](docs/sqlite.md)** | The database-backed pillars |
| **[HTTP APIs](docs/api-endpoints.md)** | API controllers and minimal APIs, hosted properly and called through a client generated from them |
| **[Migrating from Blazor](docs/migration-from-blazor.md)** · **[Diagnostics](docs/diagnostics.md)** | Day-to-day differences side by side; every RASK build error and its fix |

The full index is **[`docs/`](docs/)**, and the other packages are listed in
**[NUGET.md](NUGET.md)**. To see it running, [rask.sh](https://rask.sh) *is* a Rask app — landing page,
guides and every live demo — built from [`src/Rask.Site`](src/Rask.Site).

*Rask* is the Norwegian/Danish/Swedish word for **fast**.

## Status

Rask is pre-1.0; APIs may change between minor versions. It targets **.NET 10** (`net10.0` for ASP.NET
hosts, `net10.0-browser` for WASM). Production use at your own discretion — issues and PRs welcome.

## License

[MIT](LICENSE).
