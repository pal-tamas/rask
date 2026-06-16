<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/rask-logo-dark.svg">
  <img alt="Rask" src="assets/rask-logo.svg" width="300">
</picture>

### Live web apps in C#. One codebase — server-rendered over WebSockets, or client-side in the browser via WebAssembly.

[![NuGet Rask.Server](https://img.shields.io/nuget/v/Rask.Server.svg?label=Rask.Server)](https://www.nuget.org/packages/Rask.Server)
[![NuGet Rask.Wasm](https://img.shields.io/nuget/v/Rask.Wasm.svg?label=Rask.Wasm)](https://www.nuget.org/packages/Rask.Wasm)
[![NuGet Rask.Wasm.Hosting](https://img.shields.io/nuget/v/Rask.Wasm.Hosting.svg?label=Rask.Wasm.Hosting)](https://www.nuget.org/packages/Rask.Wasm.Hosting)
[![NuGet Rask.Templates](https://img.shields.io/nuget/v/Rask.Templates.svg?label=Rask.Templates)](https://www.nuget.org/packages/Rask.Templates)
[![NuGet Rask.Validation.DataAnnotations](https://img.shields.io/nuget/v/Rask.Validation.DataAnnotations.svg?label=Rask.Validation.DataAnnotations)](https://www.nuget.org/packages/Rask.Validation.DataAnnotations)
[![NuGet Rask.Validation.FluentValidation](https://img.shields.io/nuget/v/Rask.Validation.FluentValidation.svg?label=Rask.Validation.FluentValidation)](https://www.nuget.org/packages/Rask.Validation.FluentValidation)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)

**[Quick start](#-quick-start--server)** · **[Core concepts](#-core-concepts)** · **[Docs ↗](docs/)** · *
*[Performance](#-performance)** · **[Live demo ↗](https://pal-tamas.github.io/rask/)**

</div>

---

Write components as plain C# classes. Return a tree of HTML from `Render()`. **No `.razor`, no JSX, no JavaScript to
write** — and the *same* component code runs server-rendered with live WebSocket updates or fully client-side on
WebAssembly.

```csharp
[Route("/counter")]
public sealed class Counter : Component
{
    private int _count;

    protected override RenderResult Render() =>
    [
        H1()["Counter"],
        P()[$"Current count: {_count}"],
        Button(OnClick: () => _count++)["Click me"]
    ];
}
```

<sub>☝️ A complete, live, interactive component — routing, state, and event handling in a single C# class.</sub>

<details>
<summary><b>Contents</b></summary>

- [Why Rask](#-why-rask)
- [Compared to Blazor](#-compared-to-blazor)
- [Install](#-install)
- [Quick Start — Server](#-quick-start--server)
- [Quick Start — WASM](#-quick-start--wasm)
- [Troubleshooting](#-troubleshooting)
- [Sub-path hosting & side-by-side apps](#-sub-path-hosting--side-by-side-apps)
- [Examples](#-examples)
- [Core concepts](#-core-concepts) — Components · Interactivity · Context · Async data · Routing · Auth · Head · Error
  boundaries · Forms & validation · Files · Virtualization · Scoped CSS · Scoped JS · Element refs · Keyed lists · Live
  diff codec · Lifecycle
- [Performance](#-performance)
- [Status](#-status)
- [License](#-license)

</details>

## ✨ Why Rask

I've spent 15+ years building full-stack apps on .NET — WebForms, MVC, and a long stretch of SPA work in Angular and
React over a C# API. That split is productive, but it never stopped being two worlds: two languages, two type systems,
two build chains, and a serialization seam in the middle I maintained by hand. I wanted the front end back in C#.

Blazor answers part of that, but `.razor` puts markup and code back in one file with its own templating dialect — the
very thing I'd been trying to get away from. So I built Rask around a single conviction: a component is just a C# class
that returns a tree. No `.razor`, no JSX, no template language — `Div(...)[Span(...), "hi"]` is plain, refactor-safe,
IDE-native C#, and the *same* component runs server-rendered over a WebSocket or fully client-side on WebAssembly. One
codebase; pick the host per project.

The other thing I couldn't unsee from years of SPA work was how much we ship over the wire. So Rask treats the network
as the real bottleneck: after first paint, a state change sends a minimal diff — a counter tick on a 50 KB page goes out
as ~57 bytes, not 50 KB. And, honestly, Rask is also where I went deep on Roslyn source generators, rendering, and tree
diffing — it's a craft project as much as a framework, built in the open.

*Rask* is the Norwegian/Danish/Swedish word for **fast**. Concretely, it's a component framework for .NET: return a
tree of HTML from `Render()` and host the result one of three ways — server-rendered with live updates over a
WebSocket, fully client-side in the browser via WebAssembly, or an ASP.NET app that serves a published WASM bundle.
Only the hosting glue changes.

|     | Feature                            | What it means                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
|:---:|------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 🧩  | **Text-first DSL**                 | No `.razor`, no JSX. Call `Div(...)[Span(...), "hi"]`, `Button(...)["click"]`, `H1()["title"]` from C# — children attach through an indexer on every component, so the tree reads top-down like HTML and stays type-checked, refactor-safe, and IDE-friendly.                                                                                                                                                                                                                     |
| ⚙️  | **Source-generated factories**     | Define `class Counter : Component` and a `Counter()` factory is generated for you. Required vs. optional parameters fall out of property nullability automatically.                                                                                                                                                                                                                                                                                                               |
| 🔗  | **Type-safe URLs**                 | Every `[Route]` becomes a generated URL builder — `NavLink(HomePage(), ...)` instead of `"/"` strings that rot.                                                                                                                                                                                                                                                                                                                                                                   |
| 🎨  | **Scoped CSS, colocated**          | Drop a sibling `{Component}.css` next to `{Component}.cs` and selectors are auto-scoped to that type and hot-reloaded — no class-name discipline, no BEM, no leaks.                                                                                                                                                                                                                                                                                                               |
| 💉  | **Constructor DI**                 | `class Weather(IWeatherForecastService svc) : Component` works directly — no `[Inject]` properties, no boilerplate.                                                                                                                                                                                                                                                                                                                                                               |
| 🛡️ | **Error boundaries**               | `ErrorBoundary(...)` catches render-time, lifecycle, and event-handler faults in its subtree and renders a fallback with a one-shot `recover` callback — no app-wide crashes from a bad descendant.                                                                                                                                                                                                                                                                               |
|  ✅  | **Forms with async validation**    | `Form<TModel>(model, OnValidSubmit: …)` routes submit through validators you opt into by dropping `DataAnnotationsValidator()` or `FluentValidationValidator(...)` inside the form. Implement `IAsyncFieldValidator` for ad-hoc server-side rules — the submit bridge awaits async checks before routing, and rapid keystrokes cancel any prior in-flight validation (latest-wins).                                                                                               |
|  ⚡  | **Live diff codec**                | After first paint, a small state change ships a minimal edit-op payload instead of re-serializing the page — a counter tick on a 50 KB page goes from ~50 KB to ~57 bytes on the wire. On by default.                                                                                                                                                                                                                                                                             |
| 🔑  | **Keyed lists**                    | Add a `Key:` to list items (Blazor `@key` parity) and inserts/removes/reorders reconcile by identity — shipping trusted structural diffs that preserve focus and input state on the survivors. A `RASK022` analyzer flags a list item that's missing a key.                                                                                                                                                                                                                       |
| 🔐  | **Authentication, ASP.NET-native** | Inject `IUserProvider` and read `.Current` (a never-null `ClaimsPrincipal`) plus a headless `Authorize(Roles:, Policy:, Authorized:, NotAuthorized:, Authorizing:)` component for declarative gating, and `[Authorize]` on a page for route gating. No bespoke options — wire cookies/JWT/OIDC on ASP.NET's own `AddCookie`/`AddJwtBearer`/`AddAuthorization`. Runnable samples + `dotnet new --auth` cover cookie & JWT on both Server and WASM. See **[docs/authentication.md](docs/authentication.md)**. |

## ⚖️ Compared to Blazor

If you've worked in Blazor, here's how the day-to-day differs in Rask:

| In Blazor                                         | In Rask                                                                                                                                                                                                                                                                                                                   |
|---------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `.razor` files mixing markup + code               | Plain C# classes with an indexer for children — `Div(...)[Span(...), "hi"]`. The whole tree is C# expressions, so refactors, find-references, and IDE navigation just work.                                                                                                                                               |
| `[Inject]` properties                             | Services come in through the **constructor** (`Counter(IClock clock) : Component`), like anywhere else in .NET. Framework services (`Navigator`, `RouteState`, `HttpClient`) inject the same way.                                                                                                                         |
| `@page "/path"`                                   | `[Route("/path")]` on the class, and every route gets a generated **type-safe URL builder** — `NavLink(UserPage(id: 42))` instead of `"/users/42"` strings that rot when the route changes.                                                                                                                               |
| `RenderFragment` / `EventCallback`                | Children are `IEnumerable<Child>`; event handlers are plain delegates (`OnClick: () => _count++`). Child→parent callbacks are plain delegate props too (`Action<T>?` / `Func<T,Task>?`) — invoking one auto-re-renders the parent that owns it, no `EventCallback` wrapper. No specialised types, no `@bind-Value:event`. |
| `.razor.css` association ceremony                 | Scoped CSS via a sibling `{Component}.css` (Blazor-parity descendant combinators) — auto-globbed at build time, hot-reloaded under `dotnet watch`. Same idea for JS: a sibling `{Component}.js` is bundled and dispatched by the framework.                                                                               |
| Separate render modes to wire up                  | **Same component code on Server or WASM.** Pick the host package per project; you don't rewrite components when switching render mode. Server-only (multipart upload) and WASM-only (chunked file reads, inline downloads) behaviours live in the hosts, not in your tree.                                                |
| `<AuthorizeView>` + `AuthenticationStateProvider` | Inject `IUserProvider` and read `.Current` (a never-null `ClaimsPrincipal`) and a headless `Authorize(...)` component with `Authorized`/`NotAuthorized`/`Authorizing` slots. Auth itself is configured on ASP.NET's **own** `AddCookie`/`AddJwtBearer`/`AddAuthorization` — Rask adds no parallel options surface.                       |

Rask isn't a Blazor replacement so much as a different take on the same problem space. If those trade-offs appeal, the
rest of this README walks through what they look like in practice.

## 📦 Install

### Scaffold a new project with `dotnet new` (recommended)

The fastest way to start. `Rask.Templates` ships three project templates — one per host model — already wired up to the
matching framework package:

```bash
dotnet new install Rask.Templates

dotnet new rask-server       -n MyApp    # ASP.NET live-server app
dotnet new rask-wasm         -n MyApp    # standalone browser-WASM SPA
dotnet new rask-wasm-hosted  -n MyApp    # browser-WASM client + ASP.NET host
```

Each template emits a runnable solution with `App` + `HomePage` + `Counter` + `Weather` (async DI demo). `rask-server`
and `rask-wasm` are single-project; `rask-wasm-hosted` is a two-project solution (`MyApp.Wasm/` + `MyApp.Host/`)
pre-wired with the cross-TFM ProjectReference and a sample `/api/weatherforecast` endpoint.

`cd MyApp && dotnet run` — that's it.

### Add packages to an existing project

Pick one host package per project, then add validation packages as needed:

| Package                            | Project type                                                        | Entry-point API                                             |
|------------------------------------|---------------------------------------------------------------------|-------------------------------------------------------------|
| `Rask.Server`                      | `net10.0` ASP.NET                                                   | `services.AddRask()` + `app.UseRask<TApp>()`                |
| `Rask.Wasm`                        | `net10.0-browser`                                                   | `WasmHostBuilder.CreateDefault()` + `host.RunAsync<TApp>()` |
| `Rask.Wasm.Hosting`                | `net10.0` ASP.NET (with a `<ProjectReference>` to the WASM project) | `app.UseRask()`                                             |
| `Rask.Validation.DataAnnotations`  | any host (referenced from the project that hosts your forms)        | drop `DataAnnotationsValidator()` inside a `Form<T>`        |
| `Rask.Validation.FluentValidation` | any host (referenced from the project that hosts your forms)        | drop `FluentValidationValidator(new MyValidator())` inside  |

```bash
dotnet add package Rask.Server                       # server live host
dotnet add package Rask.Wasm                         # browser WASM client
dotnet add package Rask.Wasm.Hosting                 # ASP.NET host serving a WASM bundle
dotnet add package Rask.Validation.DataAnnotations   # opt-in: System.ComponentModel.DataAnnotations
dotnet add package Rask.Validation.FluentValidation  # opt-in: FluentValidation 12.x
```

`Rask.Server` and `Rask.Wasm` each pull in `Rask.Core` and the source generators transitively; `Rask.Wasm.Hosting`
pulls in `Rask.Wasm`. The validation packages add a global `using static` for their factory namespace, so
`DataAnnotationsValidator()` / `FluentValidationValidator(...)` are in scope without extra `using` lines.

## 🚀 Quick Start — Server

Three files. Live, server-rendered, no JavaScript to write.

**`Program.cs`**

```csharp
using Rask.Server;
using MyApp;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRask();

var app = builder.Build();
app.UseRask<App>();
app.Run();
```

**`App.cs`** — the page root. The `Rask.Server` and `Rask.Wasm` packages auto-import `Rask.Core` and the
generator-emitted factory namespaces (`Rask.Core.Components.Generated`, `Rask.Core.Routing.Generated`),
so `Component`, `Div(...)`, `H1(...)`, `Router()`, `Route<T>(...)` etc. are in scope project-wide with no
`using` lines.

```csharp
namespace MyApp;

public sealed class App : Component
{
    // App-level head content goes through the RenderResult Head override.
    // Pages override their own Head to set per-page Title — singleton dedup
    // means the page's contribution supersedes this fallback for the tab.
    protected override RenderResult Head => [
        Title()["My Rask App"],
        Meta("utf-8")
    ];

    protected override RenderResult Render() =>
        [
            Doctype(),
            Html("en")[
                Head(),                       // framework-managed slot
                Body()[
                    Router()
                ]
            ]
        ];
}
```

Both `<head>` and `<body>` are framework-managed. `<head>` collects every component's `Head`
override during render, dedupes contributions, resolves singleton tags (`<title>`, `<base>` — last
contributor wins), and splices the result plus the scoped-CSS link and scoped-JS bundle script in
automatically — passing children to `Head()` is a `RASK019` compile error. It also prefetches every
registered scoped asset up front (low-priority `<link rel="prefetch">`) so client-side navigation to
a not-yet-mounted component is flash-free; opt out with `AddRask(o => o.PreloadScopedAssets = false)`. `<body>` gets the live
runtime `<script>` injected as its last child automatically, so you no longer write
`RaskRuntimeScript()`. The root must render the full shell (`Doctype`, `Html`, `Head`, `Body`); a
missing element is flagged at compile time by **RASK021** and fails fast at runtime.

**`HomePage.cs`** — your first route. `Rask.Core.Routing` (for `[Route]`, `[RouteParam]`, `Navigator`, …) is the one
namespace you still bring in explicitly.

```csharp
using Rask.Core.Routing;

namespace MyApp;

[Route("/")]
public sealed class HomePage : Component
{
    protected override RenderResult Render() =>
        [
            H1()["Hello, world!"],
            P()["Welcome to your new Rask app."]
        ];
}
```

Run `dotnet run` and open the printed URL.

## 🚀 Quick Start — WASM

Two flavours: a **standalone** SPA that ships as a static bundle, or a **hosted** variant where an ASP.NET project
serves the published WASM bundle alongside your own `/api/...` endpoints. The `App.cs` and `HomePage.cs` from the
server quick start work under both, unchanged.

### Standalone (`rask-wasm`)

One `net10.0-browser` project. `Program.cs`:

```csharp
using Rask.Wasm;
using MyApp;

var host = WasmHostBuilder.CreateDefault();
host.Services.AddSingleton(_ =>
    new HttpClient { BaseAddress = new Uri(WasmHostBuilder.BaseAddress) });

await host.RunAsync<App>();
```

`dotnet publish -c Release` emits a static `wwwroot/` directory you can serve from any static host (GitHub Pages, S3,
nginx). No runtime server process is required for the framework itself — bring your own host for whatever public APIs
the client calls.

### Hosted (`rask-wasm-hosted`)

Two projects: the WASM client itself, and an ASP.NET host that serves the published bundle. The host project takes a
cross-TFM `<ProjectReference>` to the WASM project; the auto-imported targets discover the bundle and bake the path
into an assembly attribute at publish.

**WASM client `Program.cs`** (`net10.0-browser`):

```csharp
using Rask.Wasm;
using MyApp;

var host = WasmHostBuilder.CreateDefault();
host.Services.AddSingleton(_ =>
    new HttpClient { BaseAddress = new Uri(WasmHostBuilder.BaseAddress) });

await host.RunAsync<App>();
```

**Host `Program.cs`** (`net10.0`, with a `<ProjectReference>` to the WASM project):

```csharp
using Rask.Wasm.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRask();              // opt-in: brotli/gzip response compression of the WASM payload

var app = builder.Build();
app.UseRask();
app.Run();
```

`AddRask()` is optional but recommended — it wires `UseResponseCompression` ahead of `UseStaticFiles` so the
precompressed `.br` / `.gz` siblings emitted by the WASM publish step go out with the right `Content-Encoding` and
no runtime compression cost on the framework payload. Skip it and the host still works; you just lose the compression
layer.

`app.UseRask()` mounts the published WASM `wwwroot` as static files with sensible MIME types, immutable caching for
fingerprinted framework assets (no-cache revalidation for the rest), and a SPA fallback so client-side routes resolve.
Add your `/api/...` endpoints alongside it.

## 🧰 Troubleshooting

First-run snags and their fixes:

- **`net10.0` / `net10.0-browser` won't restore, or WASM publish fails.** Rask requires the **.NET 10 SDK**. Check
  with `dotnet --version` (≥ `10.0`). WASM projects (`rask-wasm`, the `.Wasm` half of `rask-wasm-hosted`) also need the
  WebAssembly tooling — install it once with `dotnet workload install wasm-tools`.
- **The IDE flags `HomePage()`, `Counter()`, `NavLink(...)`, or `Route<T>(...)` as undefined.** These are
  **source-generated** — the factory for every `Component`, the URL builder for every `[Route]`. They don't exist until
  the generator runs, which happens on build. Run `dotnet build` once, then reload the solution / restart the language
  server so IntelliSense picks up the generated symbols.
- **A scoped `.css` / `.js` file isn't taking effect.** The sibling file must sit in the same folder as its component
  and share the base name (`Card.cs` ↔ `Card.css`). A `.css`/`.js` with no matching component, or one matching several,
  is a build error: `RASK015`/`RASK016` for CSS, `RASK017`/`RASK018` for JS. Two components with scoped JS that share a
  simple type name warn with `RASK020` (they'd collide at `window.Rask[Name]`). Check the build output.
- **Blank page or 404s on `/_rask/...` assets behind a reverse proxy or sub-path.** The app is almost certainly running
  under a URL prefix the framework doesn't know about — set `PathBase`. See *Sub-path hosting & side-by-side apps*
  below.

## 🌐 Sub-path hosting & side-by-side apps

Every Rask hosting model accepts a per-app URL prefix (`PathBase`). Set it once and every framework-emitted URL —
head asset `<link>`/`<script>` tags, the runtime `<script>` src, WebSocket connect, upload/download/auth endpoints,
history `pushState` — is scoped under that prefix. The opposite direction is symmetric: paths going from the client
back to .NET are stripped of the prefix so user-space route handlers stay unprefixed.

Use cases:

- **Reverse-proxy sub-path** — run two `Rask.Server` apps behind one origin: `app.UseRask<AppA>(pathBase: "/appA")`
  and `app.UseRask<AppB>(pathBase: "/appB")` (typically separate processes).
- **Two WASM apps in one host** — `app.UseRask<AppA>(pathBase: "/appA")` and
  `app.UseRask<AppB>(pathBase: "/appB")` on a single `Rask.Wasm.Hosting` instance.
- **GitHub Pages sub-path** — publish with `/p:RaskPathBase=/<repo>`. The framework rewrites the published
  `index.html`'s `<base href>` to `/<repo>/` at publish time; every asset URL and the SDK import map are
  document-relative, so the WASM runtime auto-detects the prefix from `document.baseURI` on first paint and every
  scoped-asset URL resolves under `/<repo>/_rask/a/{hash}.{ext}`.

```csharp
// Server
app.UseRask<App>(pathBase: "/myapp");

// Wasm hosting (ASP.NET host serving a published wwwroot)
app.UseRask<App>(pathBase: "/myapp");
// or the non-generic form: app.UseRask(pathBase: "/myapp")

// WASM standalone — auto-detected from <base href>; override only if needed:
var host = WasmHostBuilder.CreateDefault(o => o.PathBase = "/myapp");
```

```bash
# WASM publish for GH Pages / sub-path deploy:
dotnet publish MyWasmApp -c Release /p:RaskPathBase=/myapp
```

Normalization: `"myapp"`, `"/myapp"`, and `"/myapp/"` all become `/myapp` internally. `""` and `"/"` mean root (the
default — unchanged behaviour). The CI workflow shipping `Rask.Example.Wasm` to GitHub Pages
(`.github/workflows/pages.yml`) uses this property — copy that pattern for your own sub-path deploys.

## 🧪 Examples

Beyond the quick starts, the repo ships runnable showcase apps that exercise every feature end-to-end:

- **`samples/Rask.Example.Server`** / **`samples/Rask.Example.Wasm`** / **`samples/Rask.Example.Wasm.Host`** — the same
  showcase under each
  host model. Run one with `dotnet run --project samples/Rask.Example.Server` (or `samples/Rask.Example.Wasm.Host`) and
  open the printed
  URL.
- **`samples/Rask.Example.Shared/Features/`** — the feature-by-feature pages those hosts share: forms & validation,
  nested-form
  binding, routing, JS interop, virtualization (a 10K-row table), file upload/download, and **auth gating** (the `/user`
  page shows both imperative `IUserProvider.Current` and the declarative `Authorize` component). These are the canonical
  references cited throughout *Core concepts* below. For production auth flows see *
  *[docs/authentication.md](docs/authentication.md)**.
- **`samples/Rask.Example.EfCore`** — data persistence with **EF Core + SQLite** (Server host): a
  CRUD catalogue organised as vertical slices, with a DDD aggregate + value objects, `IDbContextFactory`,
  and money stored as integer minor units (SQLite has no decimal type). Run it with
  `dotnet run --project samples/Rask.Example.EfCore` and open `/products`. Guide:
  **[docs/data-access.md](docs/data-access.md)**.
- **Runnable auth samples — one per cell of the `{Cookie, JWT} × {Server, WASM}` matrix**, each a minimal app
  (`/login`, a protected `/members`, role-gated admin content, sign-out) backed by a browser E2E:
    - **`Rask.Example.Auth`** — cookie + Server (the redeem handshake).
    - **`Rask.Example.Auth.Jwt`** — JWT + Server; the token rides in **`ProtectedSessionStorage`** (encrypted, never in
      the URL or JS).
    - **`Rask.Example.Auth.WasmCookie(.Host)`** — cookie + WASM (HttpOnly cookie, `/api/me` hydration).
    - **`Rask.Example.Auth.WasmJwt(.Host)`** — JWT + WASM (bearer in localStorage, `Authorization: Bearer`).

  Run a server cell directly — `dotnet run --project samples/Rask.Example.Auth.Jwt` — and a WASM cell via its host —
  `dotnet run --project samples/Rask.Example.Auth.WasmCookie.Host` — then visit `/members`. Sign in with
  `alice` / `password` (user) or `root` / `password` (admin). To scaffold the same in a new project:
  `dotnet new rask-server --auth`, `dotnet new rask-wasm-hosted --auth`, or `dotnet new rask-wasm --auth`. Full
  guide: **[docs/authentication.md](docs/authentication.md)**.
- **Live demo** — every push to `main` publishes `Rask.Example.Wasm` to GitHub Pages via
  [`.github/workflows/pages.yml`](.github/workflows/pages.yml), so you can click through a full multi-page Rask app in
  the browser before cloning anything.

## 📚 Documentation

The collapsible sections below are a feature-by-feature tour. For step-by-step guides and reference,
see **[`docs/`](docs/)**:

- **[Getting started](docs/getting-started.md)** — scaffold, first component, interactivity, routing.
- **[Routing](docs/routing.md)** · **[Forms & validation](docs/forms.md)** · **[Lifecycle](docs/lifecycle.md)** · *
  *[Authentication](docs/authentication.md)**
- **[Testing](docs/testing.md)** · **[Migrating from Blazor](docs/migration-from-blazor.md)**
- **[Diagnostics (RASK001–022)](docs/diagnostics.md)** — every build error/warning and its fix.
- **[Live rendering & the diff codec](docs/architecture/live-rendering.md)** — how the runtime works under the hood.

## 🧩 Core concepts

The framework, feature by feature. Each section is collapsed — click to expand the one you need.

<details>
<summary><b>🔹 Components</b></summary>
<br>

Every component is a `sealed class : Component`. Override `Render()` and return a tree. Children attach via the
`Component this[params IEnumerable<Child>]` indexer — strings, `Component`s, and value types (`int`, `double`,
`bool`, `DateOnly`, `Guid`, …) all convert implicitly to `Child`, so `H1()["Hello"]`, `Div()[Span(...), "text"]`,
and `Td()[f.TemperatureC]` (no `.ToString()`) all work. Value types render with `InvariantCulture`, so the HTML
stays locale-independent.

When you project a list into the children, no per-item cast is needed — `Tbody()[rows.Select(r => Tr(Key: r.Id)[...])]`
binds straight to the indexer (an `IEnumerable<Component>` overload handles the LINQ-pipeline shape).

`Render()` (and the `Head` override) return `RenderResult`, which accepts three shapes:

- **A single component** — `Render() => Div()[...]` (converts implicitly).
- **A collection expression** — `Render() => [Doctype(), Html(...)]` for multiple top-level nodes, with no wrapper
  element. (This is sugar for `Fragment()[...]`; the items are grouped into a `Fragment` internally.)
- **`default`** — render nothing / no contribution. Conditionals target-type each branch, so
  `Render() => ready ? [Doctype(), Html(...)] : default;` works.

```csharp
public sealed class Greeting : Component
{
    public string? Name { get; set; }
    protected override RenderResult Render() => H1()[$"Hello, {Name ?? "world"}!"];
}
```

The source generator emits a `Greeting(...)` factory automatically:

- Non-nullable property with no initialiser → **required** factory parameter.
- Nullable property with no initialiser → optional, defaults to `null`.
- Property with an initialiser → kept out of the factory; your default wins.
- `[SkipFactory]` on a property excludes it explicitly.

Inject framework services through the **constructor**, not as properties:

```csharp
public sealed class Weather(IWeatherForecastService service) : Component { ... }
```

</details>

<details>
<summary><b>⚡ Interactivity</b></summary>
<br>

Local state on fields, event handlers as plain delegates. A click triggers a server round-trip (server host) or a local
re-render (WASM host) — same code.

```csharp
[Route("/counter")]
public sealed class Counter : Component
{
    private int _count;

    protected override RenderResult Render() =>
        [
            H1()["Counter"],
            P()[$"Current count: {_count}"],
            Button(OnClick: () => _count++)["Click me"]
        ];
}
```

**Child → parent callbacks are plain delegate props** — `Action`, `Action<T>`, `Func<Task>`, `Func<T, Task>`. There is
no `Callback`/`EventCallback` type. The generated factory wraps a qualifying delegate so invoking it runs your handler
**and then re-renders the component that owns it** (the lambda's `this`). The child stays oblivious to the parent, and
the parent never wires `StateHasChanged()` by hand:

```csharp
// Reusable child — knows nothing about the parent's state.
public sealed class RatingStars : Component
{
    public int Value { get; set; }
    public Action<int>? OnRate { get; set; }   // a plain delegate prop

    protected override RenderResult Render() =>
        Div()[
            Enumerable.Range(1, 5).Select(i => (Child)Button(
                OnClick: () => OnRate?.Invoke(i),  // child invokes; parent re-renders
                Key: i)[i <= Value ? "★" : "☆"])
        ];
}

// Parent — the lambda captures `this`, so invoking OnRate re-renders this component.
public sealed class RatingDemo : Component
{
    private int _rating;

    protected override RenderResult Render() =>
        [
            RatingStars(Value: _rating, OnRate: n => _rating = n),
            P()[_rating == 0 ? "Click a star." : $"You rated: {_rating}/5"]
        ];
}
```

HTML element handlers (`Button.OnClick`, …) are **not** wrapped — they reach the DOM directly, where a re-render is
already free. Wrapping is confined to your own components, keeping the render hot path allocation-free. A static method,
or a lambda closing over a local instead of `this`, has no component target, so no auto re-render fires — write the
lambda inside the component that should update.

</details>

<details>
<summary><b>🧠 Context (provide / consume)</b></summary>
<br>

Pass a value down the tree without prop-drilling, React-style. Provide it high up; read it deep, through intermediate
components that know nothing about it. `Context.Provide<T>(Value:)` is a transparent node (no DOM); consume with
`Context.Get<T>()` (null if absent), `Context.Required<T>()` (throws), or `Context.Has<T>()`. Nearest provider wins,
matched by type — provide a concrete type, consume by an interface it implements.

```csharp
public sealed record Theme(string Name, bool IsDark);

public sealed class ThemeDemo : Component
{
    private Theme _theme = new("Light", false);

    protected override RenderResult Render() =>
        Context.Provide<Theme>(Value: _theme)[      // provided to the whole subtree
            Button(OnClick: () => _theme = _theme.IsDark ? new("Light", false) : new("Dark", true))[
                $"Toggle — {_theme.Name}"],
            ThemeCard()                             // intermediate: no theme prop passed in
        ];
}

public sealed class ThemeCard : Component       // theme-unaware; render-cached after first paint
{
    protected override RenderResult Render() => Div()["Nested: ", ThemeBadge()];
}

public sealed class ThemeBadge : Component      // the consumer
{
    protected override RenderResult Render()
    {
        var theme = Context.Required<Theme>();  // reading latches it out of the render cache…
        return Span()[theme.IsDark ? "🌙 Dark" : "☀️ Light"];
    }
}
```

Reading a context value opts the consumer out of the render cache, so it re-reads when the provider re-renders — even
straight through a cached intermediate (here `ThemeCard` never re-renders, yet `ThemeBadge` updates on every toggle).

</details>

<details>
<summary><b>⏳ Async data</b></summary>
<br>

Override `OnMountAsync` (runs once per instance) or `OnPropsChangedAsync` (runs every render). Each `await`
triggers an automatic re-render after the continuation, so a loading placeholder turns into real data with no manual
`StateHasChanged()`.

The runtime coalesces these into **one payload per handler dispatch**, and the terminal auto re-render is a
*publish-only* walk that won't re-fire `OnRendered` on components that already rendered. That keeps an
`OnRenderedAsync` hook which awaits a next-frame JS call (e.g. drawing a chart) from looping on itself —
newly-mounted children still get their first `OnRendered(firstRender: true)`.

```csharp
[Route("/weather")]
public sealed class Weather(IWeatherForecastService service) : Component
{
    private WeatherForecast[]? _forecasts;

    protected override async Task OnMountAsync() =>
        _forecasts = await service.GetForecastsAsync();

    protected override RenderResult Render() =>
        _forecasts is null
            ? P()[Em()["Loading..."]]
            : Table()[/* render rows */];
}
```

</details>

<details>
<summary><b>🧭 Routing</b></summary>
<br>

`[Route]` registers a page. `[RouteParam]` and `[QueryParam]` bind URL pieces to properties. The generator emits a
strongly-typed URL builder for each route, so links don't carry stringly-typed paths.

```csharp
[Route("/users/{id}")]
public sealed class UserPage : Component
{
    [RouteParam] public int Id { get; set; }
    [QueryParam] public string? Tab { get; set; }

    protected override RenderResult Render() => Span()[$"User #{Id} — {Tab ?? "overview"}"];
}

// elsewhere:
NavLink(UserPage(id: 42))["View user"];
```

Inside event handlers, navigate via the scoped `Navigator` service: `nav.Navigate(HomePage())`,
`nav.SetQuery("tab", "settings")`, etc. Inject it through the constructor like any other service.

Mark a component `[NotFound]` to register it as the catch-all 404 page; the framework falls back to a minimal
built-in page if no app-defined one exists.

</details>

<details>
<summary><b>🔐 Auth gating (inject <code>IUserProvider</code>)</b></summary>
<br>

Inject `IUserProvider` via the constructor and read `.Current` — a never-null `ClaimsPrincipal` resolved from the
scoped provider (back it with a cookie/JWT on Server, or `/api/me` on WASM). Gate **imperatively** in `Render()` with
plain C#, and subscribe to the provider's `Changed` event so a sign-in originating anywhere re-renders the gate:

```csharp
public sealed class AccountPanel : Component
{
    private readonly IUserProvider _auth;
    public AccountPanel(IUserProvider auth) => _auth = auth;   // inject via the ctor

    protected override void OnMount() => _auth.Changed += StateHasChanged;
    protected override void OnUnmount() => _auth.Changed -= StateHasChanged;

    protected override RenderResult Render() =>
        _auth.Current.Identity?.IsAuthenticated == true
            ? Fragment()[
                P()["Signed in as ", Strong()[_auth.Current.Identity!.Name ?? "?"]],
                _auth.Current.IsInRole("admin")             // role-gated branch
                    ? Div()["🔑 Admin-only panel"]
                    : (Child)Fragment()]
            : P()["You are signed out."];
}
```

Or gate **declaratively** with the headless `Authorize` component — three slots, no markup of its own:

```csharp
Authorize(
    Roles: ["admin"],
    Authorized:    Div()["🔑 Admin tools"],
    NotAuthorized: A(Href: "/login")["Sign in"],
    Authorizing:   Spinner());                 // shown while the principal/policy resolves
```

For whole-page gating, put `[Authorize]` (optionally `[Authorize(Roles = "admin")]`) or `[AllowAnonymous]` on the page
component — the `RouteAuthorizationGuard` enforces it before the page renders.

**Going to production?** See **[docs/authentication.md](docs/authentication.md)** for complete, copy-pasteable flows:
cookie & JWT on both Server and WASM, ASP.NET Identity, Keycloak/OIDC, protected token storage, the
auth configured through ASP.NET's own AddCookie/AddJwtBearer, and a security checklist.

</details>

<details>
<summary><b>📑 Page head contributions</b></summary>
<br>

Any component can override `protected virtual RenderResult Head` to declare what belongs in `<head>` while that
component
is in the tree. The default is `default` — no contribution; a single tag, a collection expression of several tags, or
`default` for "nothing" are all valid (e.g. `Head => loggedIn ? [Meta(...)] : default`):

```csharp
public sealed class UserDetailPage : Component
{
    [RouteParam] public int Id { get; set; }

    // The framework dedupes by rendered HTML; <title> and <base> are singleton
    // tags — last contributor wins. So this page's Title overrides App's
    // fallback when the user lands on /users/42.
    protected override RenderResult Head => [
        Title()[$"User #{Id} — My Rask App"],
        Meta(Name: "description", Content: $"Profile for user {Id}"),
        Link(Rel: "stylesheet", Href: "https://cdn.example.com/profile.css")
    ];

    protected override RenderResult Render() => /* … */;
}
```

When the user navigates away, the page leaves the tree, its contributions drop from the registry, and the next
render's `<head>` reflects whatever components remain. Multiple instances of the same `Link(Href: "...")` dedupe to
a single emission. The `Head()` HTML element itself is **framework-managed** — passing it children is a `RASK019`
compile error; everything goes through the override.

</details>

<details>
<summary><b>🛡️ Error boundaries</b></summary>
<br>

Wrap any subtree in `ErrorBoundary(...)` to catch render-time, sync/async lifecycle, and event-handler exceptions
thrown by descendants. The fallback receives the exception plus a `recover` callback so the boundary can be reset.

```csharp
ErrorBoundary(
    Fallback: (ex, recover) => Div()[
        Strong()["Something went wrong: "], ex.Message,
        Button(OnClick: recover)["Try again"]
    ])[
    // any subtree — render, lifecycle, or handler faults all bubble here
    RiskyChild()
]
```

Without a `Fallback`, the boundary renders a built-in default error page. The `recover` callback passed to the
fallback is the only reset path.

</details>

<details>
<summary><b>✅ Forms &amp; validation</b></summary>
<br>

Bind inputs two-way with `Input(Bind: () => model.Field)` — the input type is inferred from the property's CLR type
(string → text, bool → checkbox, int → number, DateOnly → date, …) and new values flow back into the model on each
event. `Form<TModel>(model, OnValidSubmit: …, OnInvalidSubmit: …)` routes submit through whichever validators are
attached to its `EditContext`. Field errors render via `ValidationMessage` and a top-of-form digest via
`ValidationSummary` — both are headless and take a required `Template:` lambda so you control the markup
(e.g. `Template: errs => Div(Class: "err")[errs[0]]`).

A bound `Select(Bind: () => model.Field)[Option(...), ...]` **pre-selects the option matching the current model value**
— including a non-first option — even when the options are supplied through the `[...]` indexer; the marking is
deferred to serialize time so the rendered `<select>` always reflects the bound state on first paint.

`Input` / `Select` / `Textarea` also accept `AfterBind` / `AfterBindAsync` callbacks that fire **after** the new value
is written to the model (and after validators see the change) — handy for dependent fields that need to rebind in the
same render. Skipped on parse failure or no-op writes.

Validation comes in layers. **The lightest ships in `Rask.Core` — inline `Validate:` lambdas, no extra package:**

- **Per-field inline rule** — pass a `Validate:` lambda directly to an `Input`. Three overloads cover the common
  shapes: omit it, return `IEnumerable<string>` for sync rules, or return `ValueTask<IEnumerable<string>>` for async
  (the `CancellationToken` cancels the in-flight check on the next keystroke). An empty sequence means valid; any
  returned strings become the field's errors.
- **Cross-field rule on the form** — pass `Validate:` to `Form<TModel>` to run a model-level check on submit (great
  for "passwords must match" or "either email or phone is required"). `[FactoryGeneric]` narrows the lambda's
  parameter to `TModel` so it's strongly typed.

```csharp
Form<SignupModel>(_model, OnValidSubmit: m => Console.WriteLine(m.Username))[
    Input(Bind: () => _model.Username,
          Validate: name => name.Length < 3 ? ["Username is too short"] : []),
    ValidationMessage(For: () => _model.Username,
        Template: errs => Div(Class: "field-error")[errs[0]]),
    Button(Type: "submit")["Sign up"]
]
```

**For attribute- or rules-based validation, opt into a package** and drop its validator component inside the form — it
wires into the form's `EditContext` and covers the whole reachable model graph from one place:

- **`Rask.Validation.DataAnnotations`** — `DataAnnotationsValidator()` wires `[Required]` / `[EmailAddress]` / `[Range]`
  / `IValidatableObject` into the form's `EditContext`.
- **`Rask.Validation.FluentValidation`** — `FluentValidationValidator(new MyValidator())` delegates to a
  `FluentValidation.IValidator`, including async rules via `MustAsync`.

```csharp
public sealed class SignupModel
{
    [Required, StringLength(20, MinimumLength = 3)] public string Username { get; set; } = "";
    [Required, EmailAddress]                        public string Email    { get; set; } = "";
}

[Route("/signup")]
public sealed class SignupPage : Component
{
    private readonly SignupModel _model = new();

    protected override RenderResult Render() =>
        Form<SignupModel>(_model, OnValidSubmit: m => Console.WriteLine(m.Username))[
            DataAnnotationsValidator(),                         // opt-in: DA attributes
            Input(Bind: () => _model.Username),
            ValidationMessage(For: () => _model.Username,
                Template: errs => Div(Class: "field-error")[errs[0]]),
            Input(Bind: () => _model.Email),
            ValidationMessage(For: () => _model.Email,
                Template: errs => Div(Class: "field-error")[errs[0]]),
            Button(Type: "submit")["Sign up"]
        ];
}
```

#### Async validation

The inline `Validate:` lambda already covers async per-field rules — return a `ValueTask<IEnumerable<string>>` and the
submit bridge awaits it before routing, with rapid keystrokes cancelling any prior in-flight check (latest-wins). Reach
for a full **`IAsyncFieldValidator`** when the rule needs DI (an `HttpClient`, a repository) or you want to reuse it
across forms: implement it and add it to a manually built `EditContext`. `ValidatingIndicator` is headless too — pass a
`Template:` lambda for whatever should show while the field is being checked (e.g.
`Template: () => Span()["Checking..."]`).

```csharp
public sealed class UniqueUsernameValidator : IAsyncFieldValidator
{
    public async ValueTask ValidateFieldAsync(
        EditContext ctx, FieldIdentifier field, CancellationToken ct)
    {
        if (ctx.Model is SignupModel m && field.FieldName == nameof(SignupModel.Username))
        {
            await Task.Delay(400, ct);                    // pretend it's an API call
            if (await IsTakenAsync(m.Username))
                ctx.AddValidationMessage(field, "Already taken.");
        }
    }
    public ValueTask ValidateAsync(EditContext c, CancellationToken ct) => default;
}

private readonly SignupModel _model = new();
private EditContext? _ctx;

protected override void OnMount()
{
    _ctx = new EditContext(_model);
    _ctx.AddValidator(new UniqueUsernameValidator());
}

protected override RenderResult Render() =>
    Form<SignupModel>(_model, Context: _ctx, OnValidSubmit: m => Console.WriteLine(m.Username))[
        DataAnnotationsValidator(),
        Input(Bind: () => _model.Username),
        ValidatingIndicator(For: () => _model.Username,
            Template: () => Span(Class: "spinner")["Checking..."]),
        ValidationMessage(For: () => _model.Username,
            Template: errs => Div(Class: "field-error")[errs[0]]),
        Button(Type: "submit")["Sign up"]
    ];
```

#### Complex models — sub-objects and lists

`Bind` and validation extend transparently through nested sub-objects and collections. A single
`DataAnnotationsValidator()` or `FluentValidationValidator(...)` at the top of the form covers the whole reachable
graph — there's no per-level opt-in. Validation messages key off the **owner sub-instance**, not a dotted path from
the root, so removing or replacing a row drops its error state with it.

```csharp
public sealed class CheckoutModel
{
    [Required] public string Name { get; set; } = "";
    public AddressModel Address { get; set; } = new();
    public List<LineItem> Items { get; set; } = new();
}

public sealed class AddressModel
{
    [Required] public string Street { get; set; } = "";
    [Required, RegularExpression("^[A-Z]{2}$")] public string Country { get; set; } = "";
}

public sealed class LineItem
{
    [Required] public string Description { get; set; } = "";
    [Range(1, int.MaxValue)] public int Quantity { get; set; } = 1;
}
```

**Sub-object binding** uses the same `Bind: () => ...` shape as flat models:

```csharp
Input(Bind: () => _model.Address.Street),
ValidationMessage(For: () => _model.Address.Street,
    Template: errs => Div(Class: "field-error")[errs[0]]),
```

**Collection binding — foreach + per-item capture** is the canonical pattern. Each iteration captures a different
`item` reference into its own closure, so each row's lambda points at a distinct instance:

```csharp
foreach (var item in _model.Items)
{
    rows.Add(Tr()[
        Td()[Input(Bind: () => item.Description)],
        Td()[Input(Bind: () => item.Quantity)],
        Td()[Button(Type: "button", OnClick: () => _model.Items.Remove(item))["×"]]
    ]);
}
```

**Collection binding — indexer style** is the alternative when you need the row number for UI (reorder buttons,
"Row #3" labels) or when items are records that get replaced rather than mutated — `() => model.Items[i].Name`
re-resolves the indexer every render, so the binding follows the new slot value through replacement. Watch out for
the classic `for (int i = …)` closure trap: copy the index into a per-iteration local before the lambda captures it.

```csharp
for (var idx = 0; idx < _model.Items.Count; idx++)
{
    var i = idx;                                      // <-- per-iteration capture, NOT idx
    rows.Add(Tr()[
        Td()[$"#{i + 1}"],
        Td()[Input(Bind: () => _model.Items[i].Description)],
        Td()[Input(Bind: () => _model.Items[i].Quantity)]
    ]);
}
```

`foreach` doesn't have the closure trap. Records with init-only properties can't be auto-bound via the `Bind` setter
— either declare the record properties as mutable (`{ get; set; }`), or use the indexer pattern with a manual
`OnChange` that replaces the slot with `_model.Items[i] = _model.Items[i] with { Field = newValue }`.

**FluentValidation nesting** uses `SetValidator(...)` and `RuleForEach(...).SetValidator(...)` in the user validator
— Rask routes the dotted `error.PropertyName` (`Address.Street`, `Lines[0].Quantity`) back to the runtime sub-
instance so `ValidationMessage(For: () => _model.Address.Street, ...)` reads it off the right slot.

**Trimming caveat.** Validating a nested graph reflects over every reachable model type. The trimming contract that
already applies to the root model (preserve its public properties via `[DynamicallyAccessedMembers]` or a
`<TrimmerRootDescriptor>`) extends to every nested type. The full Forms/Complex-models showcase under `/nested-forms`
demonstrates all four patterns side-by-side.

#### Radio & checkbox groups

`RadioGroup<TValue>` binds one value from a set of options; `CheckboxGroup<TItem>` binds an `ICollection<TItem>`,
toggling each item in place. Both are transparent `Fragment`s built on the same `Input.Bound` machinery as the rest of
the form, so changes flow through the `EditContext` (validation, touched-tracking) like any bound field:

```csharp
Form(_prefs)[
    RadioGroup(
        () => _prefs.Plan,                                  // single value
        Options: new[] { Plan.Free, Plan.Pro, Plan.Team },
        OptionLabel: p => Span()[p.ToString()]),

    CheckboxGroup<string>(                                  // a collection — toggles in place
        () => _prefs.Interests,
        Options: new[] { "Web", "Mobile", "AI", "Games" },
        OptionLabel: t => Span()[t])
]
```

`CheckboxGroup` usually needs the explicit type argument (`CheckboxGroup<string>`) when the bound collection is a
concrete `List<T>`. Membership is compared with `EqualityComparer<TItem>.Default`. Changing any option re-renders the
component that declared the group, so a live summary updates immediately.

</details>

<details>
<summary><b>📎 Files: upload and download</b></summary>
<br>

`Input(Type: "file", OnFiles: …)` accepts files; `Navigator.Download(...)` sends them. The same component code runs
unchanged on Server and WASM — only the transport differs (multipart over the WebSocket on the server, JS-Map +
chunked reads on WASM; downloads go through `/_rask/download/{token}` on the server, base64 + Blob URL on WASM).

```csharp
Input(Type: "file", OnFiles: async files => {
    var file = files[0];                                         // RaskFile
    using var s = file.OpenReadStream(maxAllowedSize: 5_000_000); // valid only inside this handler
    await s.CopyToAsync(destination);
})
```

```csharp
public sealed class ReportPage(Navigator nav) : Component
{
    private void Download() =>
        nav.Download("report.txt",
                     Encoding.UTF8.GetBytes("hello"),
                     "text/plain");

    protected override RenderResult Render() =>
        Button(OnClick: Download)["Download report"];
}
```

`RaskFile` exposes `Name`, `Size`, `ContentType`, `LastModified`, plus `OpenReadStream(maxAllowedSize, ct)`. The
stream is only valid while the handler is on the stack — read whatever you need before returning. Inside a `Form`,
files also surface through `FormData.Files(name)` and participate in submit. `Navigator.Download` must be called from
an event handler. See `samples/Rask.Example.Shared/Features/Upload/UploadPage.cs` and `DownloadPage.cs` for the canonical demos.

</details>

<details>
<summary><b>📜 Virtualization</b></summary>
<br>

`Virtualize<T>` is a headless windowed-list primitive — it emits no DOM of its own and instead invokes the `Render`
delegate with the visible window of items plus the spacer offsets you wire into your own scroll container.

```csharp
Virtualize<Row>(
    Items: _rows,                                  // or ItemsProvider for async paging
    ItemSize: 32,                                  // pixel height of one row
    OverscanCount: 4,
    InitialClientHeight: 400,
    Render: ctx => Div(
        Style: "height:400px; overflow:auto;",
        OnScroll: ctx.OnScroll)[
        Div(Style: $"height:{ctx.OffsetBefore}px"),  // spacer for off-screen rows above
        Table()[
            Tbody()[
                ctx.VisibleItems.Select(item => Tr(Key: item.Index)[
                    Td()[$"#{item.Index}"],
                    Td()[item.Value?.Name ?? ""]    // null while a placeholder is loading
                ]).ToArray()
            ]
        ],
        Div(Style: $"height:{ctx.OffsetAfter}px")    // spacer for off-screen rows below
    ])
```

Provide exactly one of `Items` (in-memory) or `ItemsProvider` (async paging:
`Func<ItemsProviderRequest, ValueTask<ItemsProviderResult<T>>>`). With a provider, `Virtualize` caches loaded items by
global index, requests missing windows in the background, and emits placeholder rows with `IsPlaceholder = true` until
a fetch completes. See `samples/Rask.Example.Shared/Features/Virtualize/VirtualizePage.cs` for a 10K-row table demo.

</details>

<details>
<summary><b>🔀 Drag & drop</b></summary>
<br>

`DragDrop` is a headless drag-and-drop primitive — it owns no DOM and tracks only the in-flight drag, handing your
`Body` delegate a `DragDropContext`. You draw the draggable items and drop zones, wire the context's
`DragStart` / `DragOver` / `Drop` / `DragEnd` onto them, and move your own data when `OnDrop` reports where the drag
landed. **Zones are arbitrary string keys** — one zone is a sortable list, several are a Kanban board.

```csharp
DragDrop(
    Body: ctx => Ul()[
        _fruits.Select((fruit, i) => Li(
            Key: fruit,                                       // stable Key → trusted keyed reconciliation
            Draggable: true,
            Class: ctx.IsDropTarget("list", i) ? "drop-target" : null,
            OnDragStart: ctx.DragStart("list", i),
            OnDragOver: ctx.DragOver("list", i),              // optional: live drop-target highlight
            OnDrop: ctx.Drop("list", i),
            OnDragEnd: ctx.DragEnd)[fruit])
    ],
    OnDrop: m => Reorder(m.FromIndex, m.ToIndex))             // m: (FromZone,FromIndex) -> (ToZone,ToIndex)
```

Drag handlers are **parameterless** (like `OnClick`): the dragged item's identity rides the handler closure, not the
event payload, so no custom wire type is needed. `Draggable` / `OnDragStart` / `OnDragOver` / `OnDrop` / `OnDragEnd` are
universal attributes on every element. A multi-column Kanban board is the same primitive with one zone per column — a
single `OnDrop` handler moves the card across lists. See `samples/Rask.Example.Shared/Features/DragDrop/DragDropPage.cs` for both a
sortable list and a Kanban board.

</details>

<details>
<summary><b>🎨 Scoped CSS</b></summary>
<br>

Drop a sibling `{Component}.css` file next to `{Component}.cs` and the source generator pairs them at compile time —
selectors are auto-scoped to that component type, delivered per-component over a content-addressed HTTP endpoint, and
hot-reloaded under `dotnet watch`.

```csharp
// Card.cs
public sealed class Card : Component
{
    protected override RenderResult Render() =>
        Div(Class: "card")["..."];
}
```

```css
/* Card.css — sibling file, no extra wiring */
.card { padding: 1rem; border-radius: 8px; border: 1px solid #ddd; }
.card:hover { background: #f7f7f7; }
```

The showcase uses this throughout: `App.css`, `HomePage.css`, `ScopedRed.css` / `ScopedBlue.css`,
and the `Layout/ShowcaseLayout.css` for the sidebar. Two components can use the same `.box` selector — the framework
rewrites each to `.box[data-{scopeId}]` so they never collide. An orphan `.css` file with no matching component raises
`RASK015`; two `.css` files claiming the same component raise `RASK016`; opt the whole project out with
`<RaskScopedCssAutoInclude>false</RaskScopedCssAutoInclude>` in the `.csproj`.

Delivery is **per-component and content-addressed**, identical on Server and WASM. The framework auto-emits one
`<link rel="stylesheet" href="/_rask/a/{hash}.css" data-rask-key="rsk-css-{hash}">` per mounted component type that has
a registered stylesheet, spliced into the framework-managed `<head>` (see *Page head contributions* above) — no call
site or placement required. Each URL is a 12-hex SHA-256 of the rewritten CSS, served with
`Cache-Control: public, max-age=31536000, immutable` + an `ETag`, so two components whose rewritten CSS is byte-equal
share one cached file. Standalone/static-file WASM hosts (no in-process endpoint) get the same files baked into the published
`wwwroot/_rask/a/{hash}.css` (as static web assets) at publish, so a plain static server serves exactly what the
endpoint would.

**Global styles** — a scoped `{Component}.css` can only style that component's own elements; there is no opt-out
selector. Brand palettes, `:root` variables, shell tags (`body`/`html`), and framework classes (Bootstrap) belong in a
plain stylesheet under `wwwroot`, linked from your App component's `<Head>` like any other static stylesheet:

```csharp
// wwwroot/global.css — a normal, unscoped stylesheet.
Link(Rel: "stylesheet", Href: LiveOptions.PathBase + "/global.css")
```

`LiveOptions.PathBase` keeps the URL correct under a reverse-proxy prefix or a sub-path deploy. User `<Head>`
contributions splice in before the auto-injected scoped links, so `global.css` sits earlier in the cascade than scoped
component CSS.

</details>

<details>
<summary><b>🟨 Scoped JS</b></summary>
<br>

Drop a sibling `{Component}.js` file next to `{Component}.cs` to colocate behavior with markup. The file exports any
number of named functions; the framework wraps each file as
`window.Rask["{TypeName}"] = (function () { /* exports */ return { … }; })();` — so each
`export function rendered(...) { ... }`
becomes `window.Rask.{TypeName}.rendered`. Delivery mirrors scoped CSS: per-component and content-addressed, identical
on Server and WASM. The framework auto-emits one `<script src="/_rask/a/{hash}.js" defer data-rask-key="rsk-js-{hash}">`
per mounted component type with a registered script. `defer` means scoped JS runs after parse, so any CDN scripts you
load earlier in `<head>` (without `defer`) initialise first.

Dispatch goes through the standard `Microsoft.JSInterop.IJSRuntime`. Inject it via constructor and call
`InvokeVoidAsync("Rask.{TypeName}.{method}", args...)` from a lifecycle hook (typically `OnRenderedAsync`). The
framework does not pass an `el` argument — user JS queries the DOM itself, via a marker class or attribute the
component renders.

```js
// CodeSample.js — sibling of CodeSample.cs
export function rendered(firstRender) {
    const codes = document.querySelectorAll('.sample-card code[class*="language-"]');
    codes.forEach(code => {
        delete code.dataset.highlighted;
        window.hljs.highlightElement(code);
    });
}
```

```csharp
public sealed class CodeSample(IJSRuntime js) : Component
{
    // The framework does NOT auto-fire scoped-JS hooks. OnRenderedAsync is the
    // typical hook — it gets a firstRender bool that flows straight through to
    // your `rendered(firstRender)` function.
    protected override async Task OnRenderedAsync(bool firstRender) =>
        await js.InvokeVoidAsync("Rask.CodeSample.rendered", firstRender);

    protected override RenderResult Render() =>
        Div(Class: "sample-card")[ /* the marker class user JS will query */ ];
}
```

You can call from any lifecycle hook (`OnMount*`, `OnRendered*`, event handlers, …) and from anywhere else where
`IJSRuntime` is in scope. Orphan `.js` files (no matching `.cs` in the same folder) raise `RASK017`; ambiguous matches
raise `RASK018`. Opt out per-project with `<RaskScopedJsAutoInclude>false</RaskScopedJsAutoInclude>`.

#### Async JS interop

For round-trips where C# needs a value back from JS, use `IJSRuntime.InvokeAsync<T>`:

```csharp
public sealed class Measure(IJSRuntime js) : Component
{
    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender) return;
        int height = await js.InvokeAsync<int>("Rask.Measure.getScrollHeight");
        // … do something with height
    }
}
```

```js
// Measure.js
export function getScrollHeight() {
    return document.querySelector('.measure-target').scrollHeight;   // sync return is fine
}
// async functions also work — the dispatcher awaits the returned Promise
export async function loadFromCdn(url) {
    const r = await fetch(url);
    return await r.text();
}
```

On **WASM**, the standard Blazor-WASM trimming constraint applies: `T` in `InvokeAsync<T>` must be kept rooted on the
call site (via `[DynamicallyAccessedMembers]` or a `JsonSerializerContext`). JSON primitives (`bool`, `int`, `long`,
`double`, `string`) are always safe. Server has no such constraint.

</details>

<details>
<summary><b>🎯 Element refs (JS interop)</b></summary>
<br>

Every element takes a `Ref:` parameter so you can reach the live DOM node from C#. Mint one with `ElementRef.New()`,
store it in a **field** (so its id stays stable across renders), and pass it to `IJSRuntime` — it serializes to a marker
the client revives into the real element before your JS runs:

```csharp
public sealed class MeasureDemo : Component
{
    private readonly IJSRuntime _js;
    private readonly ElementRef _input = ElementRef.New();
    private readonly ElementRef _box = ElementRef.New();

    public MeasureDemo(IJSRuntime js) => _js = js;

    protected override RenderResult Render() =>
        Div()[
            Input(Ref: _input, Type: "text"),
            Div(Ref: _box)["A box whose width JS will read."],
            Button(OnClickAsync: () => _input.FocusAsync(_js))["Focus the input"],
            Button(OnClickAsync: MeasureBox)["Measure the box"]
        ];

    private async Task MeasureBox()
    {
        // The ref resolves to the real element before width() is called with it.
        var width = await _js.InvokeAsync<double>("Rask.MeasureDemo.width", _box);
        // … use width …
    }
}
```

Built-in helpers cover the common cases without any JS of your own: `ElementRefInterop.FocusAsync`, `BlurAsync`,
`ScrollIntoViewAsync` (and `ElementRef.FocusAsync(js)` as shown). For anything else, hand the ref to your scoped JS.

</details>

<details>
<summary><b>🔑 Keyed lists &amp; reconciliation</b></summary>
<br>

When you render a dynamic list, give each item a stable `Key:` so the framework can reconcile by **identity** instead of
by position. `Key` is the last optional parameter on **every** generated factory (Blazor `@key` parity) — it takes any
`object?`:

```csharp
Ul()[
    _todos.Select(item => Li(Key: item.Id, Class: "list-group-item")[
        Input(Bind: () => item.Done),
        Span()[item.Title]
    ])
]
```

With keys, an insert / remove / reorder ships as a **trusted structural diff** (Insert/Remove/Move) instead of a
positional full-HTML morph — so the surviving rows keep their focus, selection, scroll position, and uncommitted input
state across the change. Without keys, the same edit reconciles by position and can blow away that DOM/IDL state on
every row after the edit point.

A few things worth knowing:

- **It's an identity, not a reactive prop.** A `Key` change doesn't fire `OnPropsChanged` — a different key is a
  different logical item, so it mounts fresh. Keys are excluded from the props diff, so a propertyless component keeps
  its fast path.
- **On elements** `Key` emits `data-rask-key`; on a **transparent component or `Fragment`** it auto-forwards onto that
  item's first rendered element (so a keyed list item should render a single root element).
- **`Data["rask-key"]` still works** for back-compat; when both are set, `Key` wins.

**RASK022** (warning) nudges you when a list item is missing a key — it fires on a `.Select(...)` / `.SelectMany(...)`
projection whose body becomes a `Child`, or an element `.Add(...)`-ed to a `List<Child>` inside a loop. Add a `Key:`
(or a `Data` `rask-key`) to clear it. Suppress per-site with `#pragma warning disable RASK022`, or promote it to an
error with `<WarningsAsErrors>RASK022</WarningsAsErrors>` in the `.csproj`.

See `samples/Rask.Example.Shared/Features/KeyedLists/KeyedListsPage.cs` for an interactive demo — type into a row, then reorder with
keys
on vs off to watch DOM state follow (or not follow) its row.

</details>

<details>
<summary><b>🔁 Live rendering &amp; the diff codec</b></summary>
<a id="live-rendering--the-diff-codec"></a>
<br>

A Rask app stays live after the first paint: the server pushes re-renders over a WebSocket, and WASM re-renders in the
browser — the **same component code drives both**, only the transport differs. When state changes (an event handler
mutates a field, an `await` resolves), the runtime renders the tree again and reconciles the DOM in place.

What it sends on the wire is the interesting part. The first render ships the full HTML. After that, a small state
change ships a **minimal edit-op payload** — a handful of text / attribute / subtree operations the client walks
against the live DOM — instead of re-serializing the whole body. A counter tick on a large page goes from the entire
rendered document to a few dozen bytes: an order-of-magnitude saving on every interaction, with no change to how you
write components.

This is on by default. `LiveDiffMode.Auto` (the framework default) uses a choose-smaller heuristic: ship the diff when
it beats the full HTML on bytes, otherwise fall back to full HTML transparently. You don't opt in — but you can tune it:

```csharp
// Server
builder.Services.AddRask(o => o.DiffMode = LiveDiffMode.Auto);   // default; choose-smaller

// WASM
var host = WasmHostBuilder.CreateDefault(o => o.DiffMode = LiveDiffMode.Auto);
```

The other modes are `LiveDiffMode.DisabledFull` (always full HTML — bit-for-bit pre-codec behaviour) and
`LiveDiffMode.Forced` (always diff when one is computable; for tests/benchmarks). The codec is transparent to your
components and is exercised end-to-end on both hosts by the Playwright E2E suite.

**Slow links get honest feedback, automatically.** On WASM the boot splash shows a determinate download-progress bar
instead of an indefinite spinner. On Server, a handler whose round-trip outlives ~300ms surfaces a subtle
top-of-viewport pending bar that clears when the reply lands — so a high-latency click still feels responsive. Both stay
invisible on a fast link; neither needs any code from you.

</details>

<details>
<summary><b>♻️ Lifecycle reference</b></summary>
<br>

| Hook                                     | When                                                                                        |
|------------------------------------------|---------------------------------------------------------------------------------------------|
| `OnMount` / `OnMountAsync`               | Once, on first instance creation                                                            |
| `OnPropsChanged` / `OnPropsChangedAsync` | Every render after props are applied                                                        |
| `OnRendered` / `OnRenderedAsync`         | After every render, with a `firstRender` flag; skipped on publish-only walks (loop-safe)    |
| `OnUnmount` / `OnUnmountAsync`           | Once, on disposal (children before parents); the lifetime `CancellationToken` is still live |
| `StateHasChanged()`                      | Call to force a re-render outside an event handler                                          |

The post-`await` auto re-render is a *publish-only* walk that does not re-fire `OnRendered`/`OnRenderedAsync` on
already-rendered components, so an async hook that awaits a next-frame side effect won't loop (see **Async data**).

</details>

## 📊 Performance

> **Rask beats Blazor on bytes-on-the-wire in every like-for-like live-update scenario**
> (2–15× fewer bytes than Blazor's `RenderBatch`), and renders small trees faster and
> lighter than Blazor's `HtmlRenderer`. The [diff codec](#live-rendering--the-diff-codec)
> ships ~1,800× smaller payloads on a counter tick (~57 bytes where pre-codec shipped
> ~50 KB). The trade-off, reported honestly below: Rask uses more *server* memory per
> update to buy those tiny payloads.

Headline numbers from `Rask.Benchmarks.VsBlazor` — Rask vs Blazor's
`HtmlRenderer` (Scope 1, render hot path), `RenderTreeBuilder` parameter
churn (Scope 2, live-diff payload). Measured 2026-05-27 on **Apple M4 Pro
(14 logical cores, .NET 10.0.5)** with BenchmarkDotNet ShortRun (3 warmup

+ 3 iteration + 1 launch, `[MemoryDiagnoser]`). Lower-is-better for both
  columns; bold marks the winner.

| Scenario                                        | Rask time          | Blazor time  | Rask alloc  | Blazor alloc |
|-------------------------------------------------|--------------------|--------------|-------------|--------------|
| Counter render (1 button, 1 span)               | **246 ns**         | 1 030 ns     | **1.59 KB** | 3.37 KB      |
| AttributeHeavy 100 elements × 20 attrs          | **88 µs**          | 147 µs       | **273 KB**  | 436 KB       |
| AttributeHeavy 100 elements × 50 attrs          | **256 µs**         | 314 µs       | **842 KB**  | 1 028 KB     |
| LiveDiff: counter on 200-row page               | **49 µs**          | 136 µs       | **87 KB**   | 229 KB       |
| LiveDiff: attribute update on 100 × 20 page     | **131 µs**         | 242 µs       | **343 KB**  | 589 KB       |
| LiveDiff: input-typing burst (3-field form)     | **1.2 µs**         | 2.9 µs       | **5.81 KB** | 5.84 KB      |
| LiveDiff: multi-attribute (5 attrs on root)     | **1.1 µs**         | 3.1 µs       | **4.47 KB** | 6.63 KB      |
| Realistic: dashboard counter tick               | **8.9 µs**         | 23.7 µs      | **27 KB**   | 49 KB        |
| Realistic: navigation tab switch                | **15.9 µs**        | 34.1 µs      | **25.7 KB** | 56.7 KB      |
| Virtualize 1000 items vs render-all (Rask wins) | **1.9 µs**         | 163 µs       | **11.2 KB** | 609 KB       |
| Scale: keyed-list reorder (1 000 rows)          | **269 µs** (0.93×) | 290 µs       | 658 KB      | **464 KB**   |
| Scale: keyed-list reorder (5 000 rows)          | 1 625 µs (1.08×)   | **1 505 µs** | 3 391 KB    | **3 266 KB** |
| Scale: random permutation 1 000 keyed rows      | 477 µs (1.39×)     | **343 µs**   | 632 KB      | **457 KB**   |

**Where Rask wins (wire bytes):** the [diff codec](#live-rendering--the-diff-codec)
is the main lever — a counter tick on a 200-row page ships ~41 bytes over the wire
vs ~24 KB pre-codec, and **every like-for-like scenario in the head-to-head suite now
ships fewer bytes than Blazor's `RenderBatch`** (2–15×). A keyed-list reorder is the
latest to cross over: the move run collapses into one `PermutationBatch` op carrying
the shared parent path once, so a 200-row reverse-sort drops from 3.9 KB to **1.1 KB
(2.99× vs Blazor)** — the last like-for-like byte loss, now a win. See the full
per-scenario table and methodology in
[
`benchmarks/Rask.Benchmarks.VsBlazor/Baselines/vs-blazor.md`](benchmarks/Rask.Benchmarks.VsBlazor/Baselines/vs-blazor.md).

**Where Rask wins (CPU/alloc):** small-tree renders, attribute-heavy markup, live diffs
that touch only a few nodes on a large page, virtualised lists, and the "realistic"
patterns (dashboard tick, nav switch).

**Where Rask trails (server memory):** Rask optimises *bytes on the wire*, and it pays
for that with server-side memory. It re-serialises the whole page to HTML on every
render and diffs the frame stream, so a steady-state update allocates more than Blazor
(which diffs its retained render tree directly) — measured deterministically at ~70 KB
vs ~31 KB per update on the 200-row counter page. It also retains a heavier tree (a
`Component` object graph vs Blazor's packed struct render-tree frames). The trade is the
whole point: **far smaller wire payloads in exchange for more server CPU/RAM** — the
right call when the bottleneck is the network, the wrong one for server RAM under many
idle-but-mounted sessions. (The `LiveDiff` alloc rows in the table above show Rask
*lower* — those are BenchmarkDotNet per-op figures that fold in Blazor's one-time
full-tree attach batch; amortised over a long-lived session, the steady-state per-update
number here is the representative one. Same measurement, different baseline — not a
contradiction.) Reproduce with `-- mem-footprint` (below); the full allocation +
retained-heap tables and methodology live in
[`vs-blazor.md`](benchmarks/Rask.Benchmarks.VsBlazor/Baselines/vs-blazor.md). Sustained
10 000-iteration churn workloads trail for the same root cause and are documented as
accepted trade-offs in
[`Justifications.md`](benchmarks/Rask.Benchmarks.VsBlazor/Reports/Justifications.md).

<details>
<summary><b>Reproduce</b></summary>
<br>

```bash
# Scope 1 — render hot path (24 benchmarks, ~12 min):
dotnet run -c Release --project benchmarks/Rask.Benchmarks.VsBlazor -- --filter '*RenderHotPath_*' --job short

# Scope 2 — live-diff payload (32 benchmarks, ~15 min):
dotnet run -c Release --project benchmarks/Rask.Benchmarks.VsBlazor -- --filter '*LiveDiffPayload_*' --job short

# Scope 3 — scale sweeps:
dotnet run -c Release --project benchmarks/Rask.Benchmarks.VsBlazor -- --filter '*Scale_*' --job short

# Scope 6/7 — realistic patterns + sustained-load:
dotnet run -c Release --project benchmarks/Rask.Benchmarks.VsBlazor -- --filter '*Realistic_*' '*MemoryGc_*' --job short

# Deterministic reports (GC counters, no BDN timing noise):
dotnet run -c Release --project benchmarks/Rask.Benchmarks.VsBlazor -- payload-bytes     # wire bytes per update vs Blazor
dotnet run -c Release --project benchmarks/Rask.Benchmarks.VsBlazor -- mem-footprint     # alloc/update + retained heap vs Blazor
```

Results are written to `BenchmarkDotNet.Artifacts/results/`.

</details>

## 📋 Status

Rask is pre-1.0. APIs may change between minor versions. It targets **.NET 10** (`net10.0` for ASP.NET hosts,
`net10.0-browser` for WASM projects). Production use at your own discretion — issues and PRs welcome.

- **Test coverage:** unit suites across `Rask.Core.Tests`, `Rask.Generators.Tests`, host-specific test projects, and
  the validation packages, plus a Playwright E2E smoke suite (`Rask.Examples.E2E.Tests`) that runs against both
  example hosts.
- **Benchmarks:** baselines for the render hot path live in `Rask.Benchmarks` (BenchmarkDotNet); committed reports
  under `BenchmarkDotNet.Artifacts/results/`. `Rask.Benchmarks.VsBlazor` adds a head-to-head suite measuring Rask
  against Blazor on shared scenarios — render throughput and the wire-efficiency
  the [diff codec](#live-rendering--the-diff-codec)
  buys on live updates.
- **Trimming:** `Rask.Example.Wasm` publishes with zero IL warnings. See `CLAUDE.md` for the contract that keeps it
  that way.

## 📄 License

Rask is released under the [MIT License](LICENSE).

---

<div align="center">

⚡ **Rask** — *Norwegian/Danish/Swedish for "fast".*

**[Live demo ↗](https://pal-tamas.github.io/rask/)** · **[NuGet ↗](https://www.nuget.org/packages/Rask.Server)** · *
*[License](LICENSE)**

Built with .NET 10. Issues and PRs welcome.

</div>
