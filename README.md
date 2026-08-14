<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/rask-logo-dark.svg">
  <img alt="Rask" src="assets/rask-logo.svg" width="300">
</picture>

### The .NET One Person Framework — build, run, and ship a whole product solo, in C#, on one server.

<!-- Hosts & tooling -->
[![Rask.Server](https://img.shields.io/nuget/v/Rask.Server.svg?label=Rask.Server)](https://www.nuget.org/packages/Rask.Server)
[![Rask.Wasm](https://img.shields.io/nuget/v/Rask.Wasm.svg?label=Rask.Wasm)](https://www.nuget.org/packages/Rask.Wasm)
[![Rask.Wasm.Hosting](https://img.shields.io/nuget/v/Rask.Wasm.Hosting.svg?label=Rask.Wasm.Hosting)](https://www.nuget.org/packages/Rask.Wasm.Hosting)
[![Rask.Native](https://img.shields.io/nuget/v/Rask.Native.svg?label=Rask.Native)](https://www.nuget.org/packages/Rask.Native)
[![Rask.Cli](https://img.shields.io/nuget/v/Rask.Cli.svg?label=Rask.Cli)](https://www.nuget.org/packages/Rask.Cli)
[![Rask.Bootstrap](https://img.shields.io/nuget/v/Rask.Bootstrap.svg?label=Rask.Bootstrap)](https://www.nuget.org/packages/Rask.Bootstrap)
[![Rask.Testing](https://img.shields.io/nuget/v/Rask.Testing.svg?label=Rask.Testing)](https://www.nuget.org/packages/Rask.Testing)
<!-- Vertical-slice back end -->
[![Rask.Cqrs](https://img.shields.io/nuget/v/Rask.Cqrs.svg?label=Rask.Cqrs)](https://www.nuget.org/packages/Rask.Cqrs)
[![Rask.Data](https://img.shields.io/nuget/v/Rask.Data.svg?label=Rask.Data)](https://www.nuget.org/packages/Rask.Data)
[![Rask.Outbox](https://img.shields.io/nuget/v/Rask.Outbox.svg?label=Rask.Outbox)](https://www.nuget.org/packages/Rask.Outbox)
[![Rask.Jobs](https://img.shields.io/nuget/v/Rask.Jobs.svg?label=Rask.Jobs)](https://www.nuget.org/packages/Rask.Jobs)
[![Rask.Mail](https://img.shields.io/nuget/v/Rask.Mail.svg?label=Rask.Mail)](https://www.nuget.org/packages/Rask.Mail)
[![Rask.Cache](https://img.shields.io/nuget/v/Rask.Cache.svg?label=Rask.Cache)](https://www.nuget.org/packages/Rask.Cache)
[![Rask.Logging](https://img.shields.io/nuget/v/Rask.Logging.svg?label=Rask.Logging)](https://www.nuget.org/packages/Rask.Logging)
[![Rask.Dashboard](https://img.shields.io/nuget/v/Rask.Dashboard.svg?label=Rask.Dashboard)](https://www.nuget.org/packages/Rask.Dashboard)
<!-- Production SQLite -->
[![Rask.SQLite](https://img.shields.io/nuget/v/Rask.SQLite.svg?label=Rask.SQLite)](https://www.nuget.org/packages/Rask.SQLite)
[![Rask.SQLite.EntityFrameworkCore](https://img.shields.io/nuget/v/Rask.SQLite.EntityFrameworkCore.svg?label=Rask.SQLite.EntityFrameworkCore)](https://www.nuget.org/packages/Rask.SQLite.EntityFrameworkCore)
[![Rask.SQLite.Litestream](https://img.shields.io/nuget/v/Rask.SQLite.Litestream.svg?label=Rask.SQLite.Litestream)](https://www.nuget.org/packages/Rask.SQLite.Litestream)
[![Rask.SQLite.Snapshots](https://img.shields.io/nuget/v/Rask.SQLite.Snapshots.svg?label=Rask.SQLite.Snapshots)](https://www.nuget.org/packages/Rask.SQLite.Snapshots)
[![Rask.SQLite.Browser](https://img.shields.io/nuget/v/Rask.SQLite.Browser.svg?label=Rask.SQLite.Browser)](https://www.nuget.org/packages/Rask.SQLite.Browser)
<!-- Forms & push -->
[![Rask.Validation.DataAnnotations](https://img.shields.io/nuget/v/Rask.Validation.DataAnnotations.svg?label=Rask.Validation.DataAnnotations)](https://www.nuget.org/packages/Rask.Validation.DataAnnotations)
[![Rask.Validation.FluentValidation](https://img.shields.io/nuget/v/Rask.Validation.FluentValidation.svg?label=Rask.Validation.FluentValidation)](https://www.nuget.org/packages/Rask.Validation.FluentValidation)
[![Rask.WebPush](https://img.shields.io/nuget/v/Rask.WebPush.svg?label=Rask.WebPush)](https://www.nuget.org/packages/Rask.WebPush)
<!-- Meta -->
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)

# ▶ **[Try the live demo ↗](https://pal-tamas.github.io/rask/docs/)**

</div>

---

## One person. One codebase. One server. A whole product.

**Rask is the .NET One Person Framework.** A single developer builds, runs, and ships a *complete* product —
the UI, the data, the auth, the background work, and the deployment — from **one C# codebase on one server**,
with **SQLite as the production database**. No PaaS to rent. No stack of services to assemble and glue. No
second language to context-switch into.

That's the whole pitch, and here's the whole workflow — an empty folder to a live, HTTPS, database-backed
product, by one person in one sitting:

```bash
dotnet tool install -g Rask.Cli

rask new Shop --auth --docker --data                       # scaffold: UI + cookie auth + Dockerfile + SQLite
# …write a Products slice — docs/tutorial/02-first-feature.md has the code
rask db add InitialCreate && rask db update                # create + apply the SQLite migration
rask dev                                                   # run it, hot-reloading, at /products
rask deploy --host root@box --domain shop.example.com      # ship it: bare box → Docker + auto-HTTPS, zero-downtime
```

Every one of those steps is a first-party command, and every stateful pillar it touches — auth, jobs, mail,
cache, events — rides the app's **own SQLite database**. The **[zero-to-deploy tutorial](docs/tutorial/00-overview.md)**
walks this exact path, one pillar per chapter.

**[📖 Read the doctrine → The .NET One Person Framework](docs/one-person-framework.md)**

---

## It starts with the UI — plain C#, no `.razor`, no JavaScript

You write components as plain C# classes that return a tree of HTML from `Render()`. State is a field, an
event handler is a delegate, and the component re-renders itself — no `.razor`, no JSX, nothing to write in
another language:

```csharp
[Route("/counter")]
public sealed class Counter : Component
{
    private int _count;

    protected override Component? Render() =>
    [
        H1()["Counter"],
        P()[$"Current count: {_count}"],
        Button(OnClick: () => _count++)["Click me"]
    ];
}
```

<sub>☝️ A complete, live, interactive component — routing, state, and event handling in one C# class.
**[See it running, and dozens more, in the live demo ↗](https://pal-tamas.github.io/rask/docs/)**</sub>

**One codebase, every surface.** That *same* component runs three ways — pick the host per project, write the
UI once:

- **Server** — rendered on the server, live updates streamed over a WebSocket as minimal diffs.
- **WASM** — fully client-side on WebAssembly, installable as an **offline PWA**.
- **Native** *(preview)* — a real **iOS / Android** app for the App Store / Play Store.

---

## The batteries — one person's whole back end

The hard part of shipping solo isn't the UI; it's everything behind it. Rask's back half is a set of thin,
opinionated, trim/AOT-safe packages that each **ride the app's own SQLite database** — no Redis, no broker,
no separate server to run. Add one with a package reference and a line of DI; the CLI wires most of it for you.

| Pillar | What one command / one line gives you | |
|---|---|---|
| **The `rask` CLI** | `new` · `generate` · `db` · `dev` · `deploy` — the whole lifecycle, one tool. | [→](docs/cli.md) |
| **A CRUD slice** | An encapsulated entity, CQRS commands/queries, and list/create/edit pages — written once in the tutorial and repeated per feature. | [→](docs/tutorial/02-first-feature.md) |
| **Data** (`Rask.Data`) | `Entity<TId>` + EF interceptors: audit stamps, soft delete, optimistic concurrency, domain events. | [→](docs/data.md) |
| **CQRS** (`Rask.Cqrs`) | Source-generated, trim-safe queries / commands / notifications via `IDispatcher`. | [→](docs/cqrs.md) |
| **Auth** | Cookie & JWT, Server & WASM, a declarative `Authorize` gate + route guards. | [→](docs/authentication.md) |
| **Background jobs** (`Rask.Jobs`) | Durable enqueued / delayed / recurring work on the app DB, with retries. | [→](docs/jobs.md) |
| **Transactional email** (`Rask.Mail`) | Durable email over SMTP, off the request thread — bodies are Rask components. | [→](docs/mail.md) |
| **Cache** (`Rask.Cache`) | `IDistributedCache` + a typed `ICache.GetOrCreateAsync` on the app DB. | [→](docs/cache.md) |
| **Logging** (`Rask.Logging`) | The application log kept in a database of its own, so it survives a restart — searchable, with retention. | [→](docs/logging.md) |
| **Dashboard** (`Rask.Dashboard`) | An operator dashboard at `/_ops`: queue depth, dead letters and the error behind each, one-click retry, the log. | [→](docs/dashboard.md) |
| **Outbox** (`Rask.Outbox`) | Crash-safe domain events, committed in the same transaction as your data. | [→](docs/outbox.md) |
| **Production SQLite** (`Rask.SQLite`) | WAL, busy-timeout, non-blocking write retries, continuous Litestream backup. | [→](docs/sqlite.md) |
| **Deploy** | `rask deploy` takes a **bare VPS** to a live HTTPS site — Docker, a non-root login, firewall, SSH hardening, zero-downtime swaps. | [→](docs/deployment.md) |

**Everything stateful lives in one file, on one box.** That's what makes "one server" safe rather than
scary: nothing to provision, nothing to network, and a database you can back up by copying a file (or
streaming it off-box continuously). **[Why one server, no PaaS →](docs/sqlite.md#why-one-server-no-paas)**

---

<div align="center">

## 📱 Build mobile apps in C# — no Swift, Kotlin, React Native, or MAUI

**The same component ships as an installable, offline mobile app.** A Rask **WASM** app is a Progressive Web
App: it **installs to the home screen**, **launches full-screen**, **works offline**, sends **push
notifications**, badges its **app icon**, keeps the **screen awake**, and reaches the device —
**vibration, share sheet, geolocation, clipboard, orientation** — through typed C#.

```bash
rask new MyApp --template wasm --pwa     # → an installable, offline PWA, ready to deploy
```

**[📖 Build mobile apps with Rask →](docs/pwa.md)**  ·  **[Try the installable demo ↗](https://pal-tamas.github.io/rask/docs/)**

Going further than a PWA? **`Rask.Native`** *(preview)* ships the *same* component code as a real **native
iOS/Android app** — a WebView hybrid where your C# runs natively on the device. Scaffold with
`rask new MyApp --template native` and run on an emulator with `dotnet build -t:Run -f net10.0-android`.

**[📱 Native mobile with Rask →](docs/native.md)**

</div>

---

## Why the One Person Framework

Building a product used to mean assembling a stack: a frontend framework in another language, a backend, a
managed database, a queue, a cache, a blob store, a deploy pipeline — each rented, glued, and maintained. For
a team that's overhead; for one person it's the whole job. Rask collapses it into **one C# codebase on one
server**: write a feature, store it in SQLite, ship it to a box — no PaaS, no glue, no second language.

- **You write the product, not the plumbing.** A vertical slice is a handful of small files; the pillars
  register in a line; `rask deploy` even prepares the bare server for you.
- **DB-backed by default.** Jobs, mail, cache, outbox — all persist to the app's own SQLite DB. Adding one is
  a package reference, not a new service to operate.
- **Correct, concurrent, backed-up SQLite.** WAL, busy-timeout, non-blocking write retries, and continuous
  streaming replication make one file a real production database.
- **The same UI everywhere.** Server, WASM/PWA, or native — one component model, three hosts.

### And it happens to be the fastest .NET UI, too

The One Person Framework story is the headline; the engine underneath is genuinely fast. Rask treats the
network as the real bottleneck: after first paint, a state change ships a minimal diff — a counter tick on a
24 KB page goes out as ~41 bytes, not 24 KB. It ships fewer bytes on the wire than Blazor on *every* scenario
in the head-to-head suite (typically 2–5×, up to 56×), allocates ~40× less per update, and holds a ~30%
leaner retained tree per mounted page — the one axis Blazor used to lead. **Rask leads on every measured axis.**

| Per-render axis | Rask | Blazor | Rask advantage |
|---|---:|---:|---|
| **Bytes on the wire** — counter tick on a 24 KB page | **41 B** | 186 B | **4.5× fewer** |
| **Allocated / update** | **1,072 B** | 42,972 B | **~40× less** |
| **Retained heap / mounted page** — 200 rows | **158 KB** | 224 KB | **~30% leaner** |
| **Render hot path** — counter | **598 ns** | 1,052 ns | **1.76× faster** |

<sub>CI-enforced [Rask vs Blazor baselines ↗](benchmarks/Rask.Benchmarks.VsBlazor/Baselines/vs-blazor.md) — Apple M4, .NET 10. Every wire scenario is a Rask win; the widest gap is removing 100 rows (**37 B vs 2,080 B = 56×**).</sub>

*Rask* is the Norwegian/Danish/Swedish word for **fast** — but the point isn't a faster Blazor. It's that one
person can build the *whole* thing. **The [docs ↗](docs/) and the [live demo ↗](https://pal-tamas.github.io/rask/docs/)
are the real tour.**

## 📦 Install

> **Prerequisites:** the **.NET 10 SDK** (`dotnet --version` ≥ `10.0`); the `wasm-tools` workload
> (`dotnet workload install wasm-tools`) for the WASM templates, or the `ios android` workloads
> (`dotnet workload install ios android`) for the native template. New to Rask? The
> **[getting started guide](docs/getting-started.md)** teaches the UI, and the
> **[zero-to-deploy tutorial](docs/tutorial/00-overview.md)** builds a whole product end to end.

### Scaffold a new project (recommended)

The [`rask` CLI](docs/cli.md) (`Rask.Cli`, a global .NET tool) owns all scaffolding:

```bash
dotnet tool install -g Rask.Cli          # one-time: install the CLI

rask                                      # a wizard: name, project type, styling, Docker, batteries
rask new MyApp                            # ASP.NET live-server app (the default template)
rask new MyApp --template wasm            # standalone browser-WASM SPA
rask new MyApp --template wasm-hosted     # browser-WASM client + ASP.NET host
rask new MyApp --template native          # native iOS + Android app (WebView hybrid, preview)

cd MyApp && rask dev                       # run with hot reload (--open for a browser; native: dotnet build -t:Run -f net10.0-android)
```

Run `rask` with no arguments on a terminal and it asks its way to a project, skipping any question you
already answered on the command line. Every project comes with a `.gitignore`, an `.editorconfig`, a
`.slnx` solution and a git repository with one commit.

Add `--auth` for a cookie/JWT-wired starter, `--pwa` (WASM) for an installable offline app, or
`--docker` (the three web templates) for a production multi-stage Dockerfile — see
[docs/deployment.md](docs/deployment.md). `rask info` reports the CLI / SDK / OS versions.

### Add packages to an existing project

Pick one host package per project, then add opt-in packages as needed:

<details>
<summary><strong>Package → project type → entry-point API</strong> (click to expand)</summary>

| Package                            | Project type                                                        | Entry-point API                                             |
|------------------------------------|---------------------------------------------------------------------|-------------------------------------------------------------|
| `Rask.Server`                      | `net10.0` ASP.NET                                                   | `services.AddRask()` + `app.UseRask<TApp>()`                |
| `Rask.Wasm`                        | `net10.0-browser`                                                   | `WasmHostBuilder.CreateDefault()` + `host.RunAsync<TApp>()` |
| `Rask.Wasm.Hosting`                | `net10.0` ASP.NET (with a `<ProjectReference>` to the WASM project) | `app.UseRask()`                                             |
| `Rask.Native` *(preview)*          | `net10.0-ios;net10.0-android` app head                             | `NativeAppHost.CreateDefault()` + `host.RunLocalAsync<TApp>(webView)` |
| `Rask.Validation.DataAnnotations`  | any host that hosts your forms                                      | drop `DataAnnotationsValidator()` inside a `Form<T>`        |
| `Rask.Validation.FluentValidation` | any host that hosts your forms                                      | drop `FluentValidationValidator(new MyValidator())` inside  |
| `Rask.Bootstrap`                   | any host with your components                                       | link `BootstrapStyles()` in `Head`, then use `Bs*` factories |
| `Rask.WebPush`                     | any backend (Server app or a WASM PWA's ASP.NET host)              | `services.AddRaskWebPush(...)` + inject `IWebPushSender`     |
| `Rask.Cqrs`                        | any .NET app (standalone; Server, WASM, or non-Rask)               | `services.AddRaskCqrs()` + inject `IDispatcher`             |
| `Rask.Data`                        | an EF Core app wanting a DDD base entity + interceptors           | `class X : Entity<Guid>` + `services.AddRaskData()` + `modelBuilder.ApplyRaskConventions()` |
| `Rask.Outbox`                      | an EF Core app wanting durable domain-event delivery             | `record E(...) : IOutboxEvent` + `services.AddRaskOutbox<Ctx>()` + `modelBuilder.AddRaskOutbox()` |
| `Rask.Jobs`                        | an EF Core app wanting durable background jobs                    | `record J(...) : IJob` + `ICommandHandler<J>` + `services.AddRaskJobs<Ctx>()` + `modelBuilder.AddRaskJobs()` |
| `Rask.Mail`                        | an EF Core app wanting durable transactional email                | `services.AddRaskMail<Ctx>(o => o.From = ...)` + `modelBuilder.AddRaskMail()` + inject `IMailQueue` |
| `Rask.Cache`                       | an EF Core app wanting a database-backed cache                    | `services.AddRaskCache<Ctx>()` + `modelBuilder.AddRaskCache()` + inject `ICache` / `IDistributedCache` |
| `Rask.Logging`                     | any app that wants its log to survive a restart                   | `services.AddRaskLogging("Data Source=logs.db")` — no `TContext`, no migration; inject `ILogStore` to read it back |
| `Rask.Dashboard`                   | operating an app that uses the DB-backed pillars                  | `services.AddRaskDashboard<Ctx>()` + an `AddAuthorization` policy named `RaskDashboardPolicies.Access`, then browse `/_ops` |
| `Rask.SQLite`                      | any .NET app using SQLite (server, mobile, trimmed/AOT)            | `services.AddRaskSqlite(cs)` + inject `IRaskSqliteConnectionFactory` (incl. non-blocking `ExecuteInImmediateTransactionAsync`) |
| `Rask.SQLite.EntityFrameworkCore`  | an EF Core app that wants the pragmas (+ opt-in busy retry)        | `o.UseRaskSqlite(cs)` on the `DbContextOptionsBuilder`       |
| `Rask.SQLite.Litestream`           | server-side SQLite app wanting managed backup                      | `services.AddRaskSqliteLitestream(...)` + `RestoreSqliteFromLitestreamAsync()` |
| `Rask.SQLite.Snapshots`            | server-side SQLite app wanting scheduled backups                   | `services.AddRaskSqliteSnapshots(...)` (or inject `ISqliteSnapshotter`)       |
| `Rask.SQLite.Browser`              | a WASM app wanting a real SQLite database that survives a reload   | `services.AddRaskBrowserSqlite("app")` + `o.UseSqlite(BrowserSqlite.ConnectionString("app"))` |
| `Rask.Testing`                     | your `*.Tests` project (references your app)                       | `RaskTest.Render(new MyComponent())` → assert on `.Html`    |

</details>

`Rask.Server`, `Rask.Wasm`, and `Rask.Native` pull in `Rask.Core` and the source generators transitively. Full setup,
host trade-offs, and sub-path hosting are covered in **[getting started](docs/getting-started.md)** and the **[docs ↗](docs/)**.

## 🧪 Examples

**The fastest way to understand Rask is to click through a real app and read its source.**

- **[Live demo ↗](https://pal-tamas.github.io/rask/docs/)** — `Rask.Example.Wasm` is published to GitHub Pages on every push
  to `main`; click through a full multi-page Rask app in the browser before cloning anything.
- **[Playground ↗](https://pal-tamas.github.io/rask/playground/)** — write Rask component C# in the browser with a real
  IDE: Roslyn-powered IntelliSense, as-you-type diagnostics, and a gallery of ready-to-run examples — then see it
  compile & render live (Roslyn runs in WebAssembly, no server). It also hosts an **eight-chapter guided tutorial**
  whose last four chapters run **real EF Core + SQLite inside the tab** — write an entity, save a row, query it back,
  with nothing installed. See [docs/playground.md](docs/playground.md).
- **[`samples/`](samples/)** — runnable showcase apps that exercise every feature end-to-end: the shared feature pages
  (`samples/Rask.Example.Shared/Features/`), EF Core + SQLite data access, and one auth sample per cell of the
  `{Cookie, JWT} × {Server, WASM}` matrix. Run one with, e.g.,
  `dotnet run --project samples/Rask.Example.Server` and open the printed URL.

## 📚 Documentation

Everything lives in **[`docs/`](docs/)** — start here, then dive into the topic you need:

<details open>
<summary><strong>The full guide map</strong></summary>

| Guide | What it covers |
|-------|----------------|
| **[The .NET One Person Framework](docs/one-person-framework.md)** | The doctrine: one developer, a whole product, one C# codebase, one server, SQLite-first. |
| **[Getting started](docs/getting-started.md)** | Scaffold, first component, interactivity, routing — the UI, end to end. |
| **[Tutorial: zero to deploy](docs/tutorial/00-overview.md)** | Build a whole product end to end — one OPF pillar per chapter, from `rask new` to `rask deploy`. |
| **[The `rask` CLI](docs/cli.md)** · **[Deployment](docs/deployment.md)** | `new` / `generate` / `db` / `dev` / `deploy`; Docker over SSH, auto-HTTPS, bare-VPS setup. |
| **[Best practices](docs/best-practices.md)** | The patterns and pitfalls that keep an app correct, secure, and fast. |
| **[Elements & the DSL](docs/elements.md)** | Primitives, tag factories, universal props, and typed SVG — the render surface. |
| **[Composition](docs/composition.md)** · **[Lifecycle](docs/lifecycle.md)** | Component tiers (static/stateless/stateful), context, callbacks, children; mount/update/dispose. |
| **[Routing](docs/routing.md)** · **[Forms & validation](docs/forms.md)** · **[Building form controls](docs/building-form-controls.md)** | URLs, route params, the form pipeline, custom `IFormControl<T>` inputs. |
| **The back half** — **[Data](docs/data.md)** · **[CQRS](docs/cqrs.md)** · **[Auth](docs/authentication.md)** · **[Jobs](docs/jobs.md)** · **[Email](docs/mail.md)** · **[Cache](docs/cache.md)** · **[Outbox](docs/outbox.md)** · **[Logging](docs/logging.md)** · **[SQLite](docs/sqlite.md)** | The DB-backed pillars: a DDD base entity, source-generated CQRS, cookie/JWT auth, durable jobs, transactional email, a database-backed cache, a transactional outbox, and production SQLite + backup. |
| **[Bootstrap](docs/bootstrap.md)** | Typed Bootstrap 5.3 components (incl. the zero-JS, fully keyboard-accessible [`BsSelect`/`BsMultiSelect`](docs/bootstrap-select.md) comboboxes), zero-JS interactivity, typed utility classes. |
| **[Browser APIs](docs/browser-apis.md)** · **[Mobile & PWA](docs/pwa.md)** · **[Native mobile](docs/native.md)** | The mobile & devices track: 50 typed Web-API wrappers, installable offline PWAs, and native iOS/Android apps. |
| **[JS interop](docs/js-interop.md)** · **[Accessibility](docs/accessibility.md)** · **[AOT](docs/aot.md)** · **[Testing](docs/testing.md)** | Scoped JS + element refs; a11y; opt-in full WASM AOT; unit + E2E. |
| **[Migrating from Blazor](docs/migration-from-blazor.md)** · **[Diagnostics](docs/diagnostics.md)** | How the day-to-day differs, side by side; every RASK build error/warning and its fix. |

</details>

## 📋 Status

Rask is pre-1.0. APIs may change between minor versions. It targets **.NET 10** (`net10.0` for ASP.NET hosts,
`net10.0-browser` for WASM, `net10.0-ios;net10.0-android` for native app heads). Unit suites cover the core,
generators, hosts (Server, WASM, Native), the back-half packages, and validation, plus a Playwright E2E smoke
suite; `Rask.Example.Wasm` publishes with zero IL trimming warnings. The native host is preview-stage.
Production use at your own discretion — issues and PRs welcome.

## 📄 License

Rask is released under the [MIT License](LICENSE).

---

<div align="center">

⚡ **Rask** — *the .NET One Person Framework.*

**[Live demo ↗](https://pal-tamas.github.io/rask/docs/)** · **[Docs ↗](docs/)** · **[Examples ↗](samples/)** · **[NuGet ↗](https://www.nuget.org/packages/Rask.Server)**

Build, run, and ship a whole product solo, in C#, on one server. Issues and PRs welcome.

</div>
