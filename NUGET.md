<div align="center">

<img alt="Rask" src="https://raw.githubusercontent.com/pal-tamas/rask/main/assets/rask-logo.svg" width="280">

### The full-stack .NET web framework — for a team of one or fifty.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/pal-tamas/rask/blob/main/LICENSE)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)

</div>

UI, data, auth, background work, realtime and deploy — all in C#, in one codebase, on standard ASP.NET
Core and EF Core: Rask.Data and source-generated CQRS, accounts, background jobs, email, an outbox,
cache and file storage on your own database, realtime subscriptions, multi-tenancy, full-text search,
the `/_rask` operator console, and `rask new` / `rask db` / `rask deploy` from the CLI. SQLite is the
production default; PostgreSQL and SQL Server are a package away. Small teams ship like big ones, and
one developer can still ship the whole thing alone. Guides: [rask.sh/docs](https://rask.sh/docs/).

The UI is C# too. Write components as plain C# classes that return a tree of HTML from `Render()`:
state is a field, and an event handler is a delegate. The *same* component code runs server-rendered with live WebSocket
updates or fully client-side on WebAssembly.

Rask is a superset, not a rival: React, Vue, Svelte, Angular and Lit components, real Blazor components
(`Rask.Blazor`), TypeScript SPAs and Nuxt or Next.js apps all run on it — and it builds on ASP.NET Core
and EF Core rather than replacing them.

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

> **Prerequisites: none.** The installer below adds whatever is missing — the **.NET SDK**, the
> `wasm-tools` workload the WASM templates need, Node for the SPA templates — all under `$HOME`, no
> `sudo`. Already have the .NET 10 or 11 SDK? `dotnet tool install -g Rask.Cli` is the whole story.

```bash
curl -sSL https://rask.sh/rask.sh | sh   # the rask CLI — scaffold, migrate, run, deploy
rask new MyApp                            # batteries included; or: -t wasm, or -t wasm-hosted
rask dev                                  # run with hot reload — the first migration is already applied
rask deploy --host you@box --domain app.example.com       # build + run on one box, over SSH
```

Or add to an existing project. **Pick a host** — each brings `Rask`, the shared core, and its batteries:

```bash
dotnet add package Rask.Server            # ASP.NET: live pages over WebSockets plus every battery, all on
dotnet add package Rask.Wasm              # client-side WebAssembly, with the client halves of the batteries
dotnet add package Rask.Spa.Hosting       # host a built SPA on ASP.NET: a Rask WASM app or a TypeScript bundle
dotnet add package Rask.Meta.Hosting      # host Nuxt/Next/SvelteKit/Start/SolidStart/Analog beside your C# (needs Node)
dotnet add package Rask.External           # a .tsx/.vue/.svelte/Lit component as a Rask component (needs Node)
dotnet add package Rask.Blazor             # a real Blazor component (MudBlazor, an RCL) as a Rask component
```

A library of components used by either host references the core alone: `dotnet add package Rask`.

Tailwind is not on that list because it is not a package: the compiler ships inside `Rask.Server` and
`Rask.Wasm`, so either puts it in your build. Add a `Styles/app.css` holding
`@import "tailwindcss";` and `dotnet build` compiles it; `RaskApp` and the WASM host link it, after the UI
kit's sheet — no npm, no config file, nothing to switch on.

With `Rask.Server`, this is the whole of `Program.cs` — every battery is on, and the file says only what
this app does *without*:

```csharp
var app = RaskApp.Create(args);

app.Configure(c => c.Jobs.Off());   // this app has no background work

app.Run<App>();
```

`rask new` writes it for you: `RaskApp.Create(args).Run<App>();` with every battery on, and the
`Configure` line above when you scaffold with `--no-jobs`. There is no `DbContext` to write either —
`RaskAppDbContext` maps every aggregate you declare and every battery's tables.

**The batteries, one by one** — each is also a package of its own, for a host assembled by hand: an
`AddRaskX<AppDbContext>()` call plus a `modelBuilder.AddRaskX()` schema line:

```bash
dotnet add package Rask.Data              # declare a model and that is the data layer (no DbContext to write)
dotnet add package Rask.Api               # host API controllers and minimal APIs, with a real 404 under /api
dotnet add package Rask.Api.Client        # typed clients generated from those endpoints — no URL at the call site
dotnet add package Rask.Wire              # reflection-free JSON primitives the generated codecs call
dotnet add package Rask.Cqrs              # source-generated CQRS/mediator (queries, commands, notifications)
dotnet add package Rask.Cqrs.Client       # dispatch a message to the server from a WASM client
dotnet add package Rask.Query             # cache, dedup and invalidate dispatched queries per session
dotnet add package Rask.Cqrs.Server       # host the endpoint those clients dispatch to
dotnet add package Rask.Auth             # accounts: register, sign in, sign out, confirm, reset
dotnet add package Rask.Auth.Client      # the same flows from a WebAssembly client
dotnet add package Rask.Auth.Api         # the same accounts as JSON only, for a host that renders nothing
dotnet add package Rask.Jobs              # durable background jobs
dotnet add package Rask.Mail              # transactional email queue
dotnet add package Rask.Cache             # read-through cache
dotnet add package Rask.Outbox            # transactional outbox for domain events
dotnet add package Rask.Storage           # keep uploaded files, with a row per file on your database
dotnet add package Rask.Logging           # durable log store (its own SQLite file)
dotnet add package Rask.Dashboard         # the /_rask operator dashboard over every pillar
dotnet add package Rask.Ui                # the component kit those surfaces are drawn with
dotnet add package Rask.DevTools          # the in-page devtools (wire, component tree), Debug-only, absent from every Release publish
dotnet add package Rask.WebPush           # Web Push: subscribers on your database, Push.Send(message) to reach them
dotnet add package Rask.Signaling         # host the WebRTC signaling relay ISignaling connects to
```

**Database** — SQLite, treated as a real production database:

```bash
dotnet add package Rask.SQLite                        # production pragmas (WAL, busy_timeout) via UseRaskSqlite
dotnet add package Rask.SQLite.EntityFrameworkCore    # the EF Core provider glue
dotnet add package Rask.SQLite.Litestream             # managed continuous replication
dotnet add package Rask.SQLite.Snapshots              # scheduled Online-Backup-API copies
dotnet add package Rask.SQLite.Browser                # a persistent SQLite database inside a WASM app
```

SQLite stays the default. When one box is no longer enough:

```bash
dotnet add package Rask.Postgres                      # PostgreSQL via UseRaskPostgres: session timeouts + retry
dotnet add package Rask.SqlServer                     # SQL Server via UseRaskSqlServer: XACT_ABORT, lock timeout + retry
```

**UI and testing:**

```bash
dotnet add package Rask.Validation.FluentValidation   # AbstractValidator<T>; DataAnnotations is built in
dotnet add package Rask.Testing                       # render + drive components in unit tests
```

## Why Rask

After 15+ years building full-stack .NET apps — WebForms, MVC, Angular and React over a C# API — I wanted the front end
back in C# without `.razor` mixing markup and code. So Rask makes a component a plain C# class that returns a tree, runs
the *same* code on Server or WASM, and treats the network as the bottleneck (a state change ships a minimal diff, not
the page). It's a craft project built in the open, deep on Roslyn source generators and tree diffing.

- **One component model, two hosts** — the same C# component runs Server (live diff over WS) or WASM.
- **Markup is a chain** — a Roslyn generator emits `Div.Class("panel")`, `Counter.Start(3)` and type-safe routes, so the IDE lists every step and a missing one is a compile error.
- **Scoped CSS & TypeScript** — sibling `Component.css`/`Component.ts`, compiled with no npm, content-addressed and cached.
- **Routing, lifecycle, forms, validation, auth** — batteries included, no JavaScript required.
- **Toast messages** — inject `IToaster` for transient messages that survive a client-side navigation.
- **Tiny live updates** — a minimal edit-op diff ships instead of the whole page.
- **Slow-link aware** — WASM boot shows download progress; a slow Server round-trip surfaces a pending bar.

## Links

- 📖 **[Documentation](https://github.com/pal-tamas/rask/tree/main/docs)** ·
  [Getting started](https://github.com/pal-tamas/rask/blob/main/docs/getting-started.md) ·
  [Configuration](https://github.com/pal-tamas/rask/blob/main/docs/configuration.md) ·
  [Observability](https://github.com/pal-tamas/rask/blob/main/docs/observability.md) ·
  [Accessibility](https://github.com/pal-tamas/rask/blob/main/docs/accessibility.md)
- 🚀 **[Live demo](https://rask.sh/docs/)**
- 💻 **[Source & README](https://github.com/pal-tamas/rask)**
- 🤖 **[AI assistant guide](https://github.com/pal-tamas/rask/blob/main/llms.txt)**

Licensed under MIT.
