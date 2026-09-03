# Rask documentation

Guides and references for building with Rask, **the .NET One Person Framework** — build, run, and ship a
whole product solo, in C#, on one server. **New to Rask?** Read
[Getting started](getting-started.md) start to finish — it goes from zero to a running, routed,
interactive app. **Ready to build something real?** The [**Tutorial**](tutorial/00-overview.md) takes you
from an empty folder to a deployed, database-backed product that uses every pillar. Want the philosophy
first? Read **[The .NET One Person Framework](one-person-framework.md)**. Want the pitch and a quick demo?
See the project [README](../README.md). Already building? Keep the [**Cheat sheet**](cheatsheet.md) open
and reach for the [**Recipes**](recipes.md) when you need "how do I do X?".

## Start here

| Guide | What it covers |
|-------|----------------|
| [**The .NET One Person Framework**](one-person-framework.md) | The doctrine: one developer, a whole product, one C# codebase, one server, SQLite-first — and the batteries that make it real. |
| [**Tutorial: zero to deploy**](tutorial/00-overview.md) | Build the "Shop" app end to end — scaffold → first DB-backed feature → auth → jobs → email → cache → events → production SQLite → push → ops → deploy to one box. One chapter per pillar, and the finished app is committed as [`samples/Rask.Example.Shop`](../samples/Rask.Example.Shop). |
| [**Cheat sheet**](cheatsheet.md) | The one page to keep open — every CLI command, feature field token, wiring one-liner (`AddRask…`), and code idiom, dense and scannable. |
| [**Recipes**](recipes.md) | Task-first "how do I do X?" — add a feature to an existing database, gate a page, run a job, cache a query, deploy an update — the command, the wiring line, and where to go deeper. |
| [Roadmap](roadmap.md) | The One Person Framework pillars — what's shipped (DB-backed jobs, outbox, mail, cache) and what's next (broadcast). |

## Guides

| Guide | What it covers |
|-------|----------------|
| [Installing Rask](installation.md) | The one-line installer — what it puts where, every option, upgrading, uninstalling, and the manual path if you would rather not run a script from the internet. |
| [Getting started](getting-started.md) | Prerequisites, scaffold an app, a tour of the generated files, your first component, interactivity, routing, and troubleshooting. |
| [The `rask` CLI](cli.md) | The `Rask.Cli` .NET tool — the whole lifecycle: `rask new` (scaffold), `rask db` (migrations), `rask dev` (hot-reload run), `rask deploy` (bare box → live HTTPS site), `rask info`. |
| [Live playground](playground.md) | Write Rask C# in the browser with IntelliSense, as-you-type diagnostics, and a gallery of examples, then see it compile & render live (Roslyn in WebAssembly) — how it works, the entry-point convention, and its limitations. |
| [Best practices](best-practices.md) | Production patterns and common pitfalls across component design, state, forms, data access, security, accessibility, performance and testing — each linking to the deep dive. |
| [Building components](building-components.md) | How markup is written: naming a component and chaining onto it, the properties a component demands before it exists, bound versus controlled form controls, and what the IDE offers at each step. |
| [Elements & the DSL](elements.md) | The primitives every component is built from: tag entries, universal attributes, the children indexer, `Text`/`Raw`, SVG, and the element catalog. |
| [Routing](routing.md) | `[Route]`, route/query params, nested routes, type-safe `Routes.*` URLs, `Navigator`, `RouteState`. |
| [Composition](composition.md) | Children & fragments, callbacks (child→parent), context (provide/consume), toast messages (`IToaster`/`ToastOutlet`), `VirtualizeModel`, drag-and-drop. |
| [JS interop](js-interop.md) | Scoped CSS & TypeScript conventions (a `.js` sibling is RASK054), calling JS via `IJSRuntime`, element refs (`Ref:`), typed browser APIs, asset delivery. |
| [Browser APIs](browser-apis.md) | The map of all 50 typed Web-API wrappers — shared vs WASM-only, one-shot vs subscription, the inject-from-ctor and push/`[JSInvokable]` patterns. |
| [Capability matrix](browser-capabilities.md) | Where each of the 50 APIs works (Web / PWA) — links to a reference page per API under [`apis/`](apis/). |
| [📱 Mobile & PWA](pwa.md) | Build installable, offline mobile apps in C# (WASM): web app manifest, service worker, Web Push (`IWebPush`), `rask new MyApp --template wasm`. |
| [AOT compilation](aot.md) | Opt-in full WASM AOT (`-p:RaskWasmAot=true`): the reflection-free binding registry, registering custom `IParsable` types, `InvokeAsync<T>` under AOT, and the continuous analyzer gate. |
| [Prerendering](prerendering.md) | Render a standalone WASM app's pages to real HTML at publish (`<RaskPrerender>true</RaskPrerender>`), so a crawler gets the page instead of the boot spinner: what is written, which routes are skipped and why, and why a route that throws is deliberately left out. |
| [Forms & validation](forms.md) | Two-way binding, `Form<T>`/`EditContext`, inline / DataAnnotations / FluentValidation / async validators, radio & checkbox groups. |
| [Validation](validation.md) | Built in and on: `[Required]` and `AbstractValidator<T>` run in a form and on every dispatched request, with nothing declared. The off switch, validators that need services, and what a rejected request looks like on the wire. |
| [Render modes](render-modes.md) | How a Server page reaches the browser: waiting for async data before the first byte, serving a page that needs nothing live as a cacheable document, setting a status or redirecting on load, and moving an eligible page into WebAssembly — published from the same project. |
| [Lifecycle](lifecycle.md) | `OnMount` / `OnPropsChanged` / `OnRendered` / `OnUnmount`, async-hook rules, cancellation, common gotchas. |
| [Authentication](authentication.md) | Production auth: cookie & JWT, Server & WASM, `Authorize`, route guards, Identity / Keycloak / Auth0 / Cognito / Duende. |
| [Accessibility](accessibility.md) | Setting ARIA attributes, `Role`/`TabIndex`, and focus on any element; the `Img` alt-text analyzer (RASK023). |
| [Localization](localization.md) | Ship in more than one language: the visitor's culture negotiated per request, dates and numbers in their format, text from typed JSON catalogs (a missing key is a compile error), plural grammar per language, `<html lang>`/`dir`, and the WASM ICU opt-in. |
| [Testing](testing.md) | Unit-testing components with `Rask.Testing`, driving event handlers, when to reach for E2E. |
| [Migrating from Blazor](migration-from-blazor.md) | Concept mapping, behavioural gotchas, and what stays the same. |
| [Building with AI assistants](ai-agents.md) | The `AGENTS.md` / `llms.txt` artifacts that let AI tools scaffold and extend Rask apps. |

## The One Person Framework batteries (the back half)

The opinionated, DB-backed pillars that make a solo developer productive — each a thin, trim/AOT-safe package
that rides the app's own SQLite database. No Redis, no broker, no second server. Walk through them in order
in the [Tutorial](tutorial/00-overview.md); the reference for each is here.

| Guide | What it covers |
|-------|----------------|
| [Data access (EF Core)](data-access.md) | EF Core + SQLite in a Server app: `IDbContextFactory`, loading in the lifecycle, vertical slices, a DDD aggregate + value objects, and the SQLite decimal gotcha. |
| [Rask.Data](data.md) | The `Entity<TId>` base + EF interceptors: audit stamps, transparent soft delete, optimistic concurrency, and domain events — via `AddRaskData()` + `ApplyRaskConventions()`. |
| [SQLite production pragmas](sqlite.md) | Production SQLite via `UseRaskSqlite` / `AddRaskSqlite` (standalone `Rask.SQLite`): WAL, `foreign_keys`, `busy_timeout` & friends applied on every connection open, plus Litestream backup. |
| [CQRS](cqrs.md) | Source-generated, trim-safe queries / commands / notifications and pipeline behaviors via `AddRaskCqrs()` + `IDispatcher` (standalone `Rask.Cqrs`). |
| [HTTP APIs](api-endpoints.md) | Ordinary API controllers and minimal API endpoints, hosted properly and callable without a URL: `AddRaskApi()` + `MapRaskApi()` map them and answer 404 with a problem document under `/api` — where the catch-all used to render the app with a 200 — and `Rask.Api.Client` generates one typed client per controller straight from the declaration, so a route renamed on the server breaks the call site at compile time instead of at 404 time. For when someone other than your own browser code has to call you; [CQRS](cqrs.md) is the answer when nobody does. |
| [TypeScript front ends](spa.md) | A TypeScript SPA with a typed connection to your C#: `rask new --template react`, TypeScript generated from the message records, TanStack Query, and `UseRaskSpa()` (standalone `Rask.Spa.Hosting`). React, Preact, Vue, Angular, Solid, Svelte or Lit — the framework is yours, the language is not. |
| [Meta framework front ends](meta.md) | Nuxt, TanStack Start, SolidStart, SvelteKit, Analog or Next.js owning the **whole** front end, with Rask as the backend it integrates with — one container, one port. Kestrel keeps the public port and answers `/_rask` itself; the framework's own Node server is a supervised child on loopback, and its built client assets are served by Kestrel rather than forwarded (standalone `Rask.Meta.Hosting`). Distinct from the SPA lane, which serves a static bundle and needs no Node at runtime. |
| [Blazor components](blazor-components.md) | A **real** Blazor component — from a Razor Class Library, MudBlazor, Radzen — as an ordinary Rask component: derive a `partial` class from `BlazorComponent<T>` and place it anywhere the chain goes. The Razor SDK compiles `.razor` untouched; Rask renders the result server-side into the *first* HTTP response, passes parameters as live C# objects rather than JSON, and wires the hosted component's own `@onclick` to Rask's existing channel so it fires with no Blazor circuit. Runs on both hosts, a trimmed WebAssembly publish included (the hosted type is DAM-annotated, or the trimmer removes its `[Parameter]` setters and the island renders empty). A statically rendered island is deliberately not opaque. |
| [Islands](islands.md) | A `.tsx`, `.vue`, `.svelte`, Angular or Lit file as an *ordinary Rask component*: derive from one of seven base classes — `ReactComponent`, `PreactComponent`, `SolidComponent`, `VueComponent`, `SvelteComponent`, `AngularComponent`, `LitComponent` — drop the front-end file beside it, and place it anywhere the chain goes — a leaf, a subtree, or a whole route. Props are declared in C# and serialized without reflection, callbacks re-enter C# over the channel every DOM handler already uses, and the live diff treats the subtree as opaque because its own renderer owns it. |
| [Tailwind CSS](tailwind.md) | Every project, no flag and no package: Tailwind v4 ships inside the host package and is compiled by `dotnet build` with no npm, no config file and no `node_modules` — it scans your C# string literals for class names. The standalone binary where one exists, npm where it doesn't, so no platform is left out. |
| [Rask.Query](query.md) | The dispatcher wrapped in a cache for Rask components (standalone `Rask.Query`): request dedup, staleness, background refetch, and TanStack-shaped keys matched by prefix — the same model the JavaScript side gets from TanStack Query itself. |
| [Background jobs](jobs.md) | Durable enqueued / delayed / recurring work on the app's own database via `AddRaskJobs<Ctx>()` + `IJob` (standalone `Rask.Jobs`) — at-least-once, with backoff. |
| [Transactional email](mail.md) | Durable email queued on the app's own database via `AddRaskMail<Ctx>()` + `IMail` (standalone `Rask.Mail`) — delivered off the request thread over SMTP with backoff; bodies are Rask components. |
| [Cache](cache.md) | A developer-facing cache on the app's own database via `AddRaskCache<Ctx>()` (standalone `Rask.Cache`) — standard `IDistributedCache` plus a typed `ICache` with `GetOrAddAsync`, absolute/sliding expiry. |
| [Outbox](outbox.md) | Durable, crash-safe domain-event delivery via `AddRaskOutbox<Ctx>()` (standalone `Rask.Outbox`) — events committed in the same transaction as your data, delivered post-commit with retries. |
| [Web Push](webpush.md) | Server-sent Web Push from your backend via `AddRaskWebPush(...)` + `IWebPush` (standalone `Rask.WebPush`) — VAPID + aes128gcm, zero deps; pairs with the client `IWebPush`. |
| [Secrets](secrets.md) | Where an app's passwords and API keys live, how they reach the server, and what Rask deliberately doesn't do with them. |
| [Dashboard](dashboard.md) | A built-in operator dashboard at `/_rask` via `AddRaskDashboard<Ctx>()` (standalone `Rask.Dashboard`) — queue depth and dead letters for the outbox/jobs/mail, cache contents, a live log tail (plus searchable history with `Rask.Logging`), SQLite pragmas; fail-closed behind an authorization policy. |
| [Logging](logging.md) | A durable log store via `AddRaskLogging(...)` (standalone `Rask.Logging`) — the `ILogger` pipeline kept in a SQLite file of its own, buffered off the request thread, with retention by age and row count and a searchable view in the dashboard. |
| [Observability](observability.md) | Structured logging, the `Rask.Server` meter and activity source, health checks — what to export and what the numbers mean. |
| [Configuration](configuration.md) | The options every host reads, and where to set them. |
| [Deployment](deployment.md) | Ship to a single box with `rask deploy`: Docker over SSH, a shared Caddy proxy for automatic HTTPS, zero-downtime blue-green swaps gated on `/health`, and bare-VPS setup. |
| [Scaling](scaling.md) | How far one box goes — measured, in sessions and in events per second — what survives a restart or a deploy, where the wall actually is, and what it takes to get past it. |

## Reference

| Reference | What it covers |
|-----------|----------------|
| [Diagnostics (RASK001–042)](diagnostics.md) | Every analyzer/generator diagnostic, what triggers it, and how to fix it. |
| [Code analysis](code-analysis.md) | Analyzers, warnings-as-errors, and the per-PR adoption procedure. |
| [Public API style](api-style.md) | How every public name is chosen, and the gate that records the surface. |

## Contributing

| Doc | What it covers |
|-----|----------------|
| [Development workflow](development-workflow.md) | The format → warnings-as-errors → tests → benchmarks → docs → review → PR gate, CI, nightly, releases. |
| [Repo administration](repo-administration.md) | Branch protection, required checks, secrets, and the settings this repository expects. |

## Architecture

| Doc | What it covers |
|-----|----------------|
| [Live rendering & the diff codec](architecture/live-rendering.md) | How the render walk, frame stream, edit-op diff, keyed reconciliation, and the two transports (Server WS / WASM JSImport) work. |

---

The in-repo map for contributors lives in [CLAUDE.md](../CLAUDE.md). Runnable feature demos are
under [`samples/`](../samples).
