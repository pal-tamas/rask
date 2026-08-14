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
| [Getting started](getting-started.md) | Prerequisites, scaffold an app, a tour of the generated files, your first component, interactivity, routing, and troubleshooting. |
| [The `rask` CLI](cli.md) | The `Rask.Cli` .NET tool — the whole lifecycle: `rask new` (scaffold), `rask db` (migrations), `rask dev` (hot-reload run), `rask deploy` (bare box → live HTTPS site), `rask info`. |
| [Live playground](playground.md) | Write Rask C# in the browser with IntelliSense, as-you-type diagnostics, and a gallery of examples, then see it compile & render live (Roslyn in WebAssembly) — how it works, the entry-point convention, and its limitations. |
| [Best practices](best-practices.md) | Production patterns and common pitfalls across component design, state, forms, data access, security, accessibility, performance and testing — each linking to the deep dive. |
| [Building components](building-components.md) | How markup is written: naming a component and chaining onto it, the properties a component demands before it exists, bound versus controlled form controls, and what the IDE offers at each step. |
| [Elements & the DSL](elements.md) | The primitives every component is built from: tag factories, universal attributes, the children indexer, `Text`/`Raw`, SVG, and the element catalog. |
| [Routing](routing.md) | `[Route]`, route/query params, nested routes, type-safe `Routes.*` URLs, `Navigator`, `RouteState`. |
| [Composition](composition.md) | Children & fragments, callbacks (child→parent), context (provide/consume), toast messages (`IToaster`/`ToastOutlet`), `VirtualizeModel`, drag-and-drop. |
| [JS interop](js-interop.md) | Scoped CSS & JS conventions, calling JS via `IJSRuntime`, element refs (`Ref:`), typed browser APIs, asset delivery. |
| [Browser APIs](browser-apis.md) | The map of all 49 typed Web-API wrappers — shared vs WASM-only, one-shot vs subscription, the inject-from-ctor and push/`[JSInvokable]` patterns. |
| [Capability matrix](browser-capabilities.md) | Where each of the 49 APIs works (Web / PWA / Native) and which have a native iOS/Android backend — links to a reference page per API under [`apis/`](apis/). |
| [📱 Mobile & PWA](pwa.md) | Build installable, offline mobile apps in C# (WASM): web app manifest, service worker, Web Push (`IWebPush`), `rask new MyApp --template wasm --pwa`. |
| [📱 Native mobile (iOS/Android)](native.md) | Ship the same components as a native iOS/Android app with `Rask.Native` (preview): the WebView-hybrid host, the `native` template + platform heads, `NativeAppHost` Local/Server modes, `INativeWebView`, safe-area insets. |
| [AOT compilation](aot.md) | Opt-in full WASM AOT (`-p:RaskWasmAot=true`): the reflection-free binding registry, registering custom `IParsable` types, `InvokeAsync<T>` under AOT, and the continuous analyzer gate. |
| [Forms & validation](forms.md) | Two-way binding, `Form<T>`/`EditContext`, inline / DataAnnotations / FluentValidation / async validators, radio & checkbox groups. |
| [Lifecycle](lifecycle.md) | `OnMount` / `OnPropsChanged` / `OnRendered` / `OnUnmount`, async-hook rules, cancellation, common gotchas. |
| [Authentication](authentication.md) | Production auth: cookie & JWT, Server & WASM, `Authorize`, route guards, Identity / Keycloak / Auth0 / Cognito / Duende. |
| [Accessibility](accessibility.md) | Setting ARIA attributes, `Role`/`TabIndex`, and focus on any element; the `Img` alt-text analyzer (RASK023). |
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
| [Multi-writer SQLite (CRDT)](sqlite-crdt.md) | Several replicas of one database written independently and merged without conflicts via `UseRaskCrdt(...)` + `ApplyCrdtConventions()` (standalone `Rask.SQLite.Crdt`) — cr-sqlite behind ordinary EF Core, merging per column rather than per row, with the change feed exposed as a transport-free log. |
| [Sharing a CRDT database](sqlite-crdt-sync.md) | `CrdtSyncEngine` over a bucket via `Rask.SQLite.Crdt.Sync` — each device writes only its own prefix so nothing needs locking; forward-only reads from a per-peer watermark, batched uploads, and a status a UI can render. The database is the queue, so a failed sync loses nothing. |
| [Choosing a database](databases.md) | SQLite (the default) vs PostgreSQL via `rask new --database`: what `UseRaskPostgres` configures, what the file-based batteries leave behind, how deploy changes, and why multi-instance isn't safe yet. |
| [CQRS](cqrs.md) | Source-generated, trim-safe queries / commands / notifications and pipeline behaviors via `AddRaskCqrs()` + `IDispatcher` (standalone `Rask.Cqrs`). |
| [Background jobs](jobs.md) | Durable enqueued / delayed / recurring work on the app's own database via `AddRaskJobs<Ctx>()` + `IJobQueue` (standalone `Rask.Jobs`) — at-least-once, with backoff. |
| [Transactional email](mail.md) | Durable email queued on the app's own database via `AddRaskMail<Ctx>()` + `IMailQueue` (standalone `Rask.Mail`) — delivered off the request thread over SMTP with backoff; bodies are Rask components. |
| [Cache](cache.md) | A developer-facing cache on the app's own database via `AddRaskCache<Ctx>()` (standalone `Rask.Cache`) — standard `IDistributedCache` plus a typed `ICache` with `GetOrCreateAsync`, absolute/sliding expiry. |
| [Outbox](outbox.md) | Durable, crash-safe domain-event delivery via `AddRaskOutbox<Ctx>()` (standalone `Rask.Outbox`) — events committed in the same transaction as your data, delivered post-commit with retries. |
| [Web Push](webpush.md) | Server-sent Web Push from your backend via `AddRaskWebPush(...)` + `IWebPushSender` (standalone `Rask.WebPush`) — VAPID + aes128gcm, zero deps; pairs with the client `IWebPush`. |
| [Object storage](object-storage.md) | S3 and Azure Blob via `AddRaskS3ObjectStore(...)`/`AddRaskAzureBlobObjectStore(...)` + `IObjectStore` (standalone `Rask.ObjectStore`) — ranged reads, streamed writes, conditional create; SigV4 signed in-process so it runs server-side and in the browser, with no cloud SDK. |
| [Offline-first merge](sync.md) | A hybrid logical clock, an append-only op log and a deterministic per-field merge via `Rask.Sync` — pure logic, no I/O; conflicts are reported rather than silently resolved, because last-writer-wins loses data by design. |
| [Syncing between devices](sync-client.md) | `SyncEngine` over a bucket via `Rask.Sync.Client` — each device writes only its own prefix so nothing needs locking; forward-only reads from a per-peer watermark, batched uploads, an offline queue, and a status a UI can render. |
| [Secrets](secrets.md) | Where an app's passwords and API keys live, how they reach the server, and what Rask deliberately doesn't do with them. |
| [Dashboard](dashboard.md) | A built-in operator dashboard at `/_ops` via `AddRaskDashboard<Ctx>()` (standalone `Rask.Dashboard`) — queue depth and dead letters for the outbox/jobs/mail, cache contents, a live log tail (plus searchable history with `Rask.Logging`), SQLite pragmas; fail-closed behind an authorization policy. |
| [Logging](logging.md) | A durable log store via `AddRaskLogging(...)` (standalone `Rask.Logging`) — the `ILogger` pipeline kept in a SQLite file of its own, buffered off the request thread, with retention by age and row count and a searchable view in the dashboard. |
| [Observability](observability.md) | Structured logging, the `Rask.Server` meter and activity source, health checks — what to export and what the numbers mean. |
| [Configuration](configuration.md) | The options every host reads, and where to set them. |
| [Deployment](deployment.md) | Ship to a single box with `rask deploy`: Docker over SSH, a shared Caddy proxy for automatic HTTPS, zero-downtime blue-green swaps gated on `/health`, and bare-VPS setup. |
| [Scaling](scaling.md) | How far one box goes — measured, in sessions and in events per second — what survives a restart or a deploy, where the wall actually is, and what it takes to get past it. |

## Bootstrap components

The optional `Rask.Bootstrap` package — typed Bootstrap 5.3 component factories, layered on top of
core. Start at the [hub](bootstrap.md) for setup and the component map; each component group then has
its own page:

| Guide | What it covers |
|-------|----------------|
| [Bootstrap](bootstrap.md) | Setup (`BootstrapStyles()`), color modes, the typed enums, the component map, and versioning. |
| [Layout](bootstrap-layout.md) | `BsContainer`, `BsRow`/`BsCol`, `BsStack` — the page shell, the responsive grid, flex stacks. |
| [Buttons & badges](bootstrap-buttons.md) | `BsButton`, `BsButtonGroup`, `BsBadge`, `BsCloseButton`. |
| [Cards, lists & tables](bootstrap-cards.md) | `BsCard` (+ parts), `BsListGroup`, `BsPlaceholder`, `BsTable`, `BsPagination`, `BsBreadcrumb`. |
| [Data grid](data-grid.md) | `BsDataGrid<T>` (+`BsColumn<T>`) — typed columns, sorting, paging, footer totals, master-detail. |
| [Alerts, spinners & progress](bootstrap-feedback.md) | `BsAlert` (dismissible), `BsSpinner`, `BsProgress`. |
| [Icons](bootstrap-icons.md) | The typed `BsIcon` over every Bootstrap Icons glyph (`BsIconName`). |
| [Navbar & nav](bootstrap-navigation.md) | `BsNavbar`/`BsNavbarBrand`/`BsNav`/`BsNavItem` — SPA-routed, auto-active, zero-JS. |
| [Modals, offcanvas & dropdowns](bootstrap-overlays.md) | Controlled `BsModal`/`BsOffcanvas`/`BsDropdown` + the fixed-position popover helper. |
| [Tabs, accordion & collapse](bootstrap-disclosure.md) | Controlled `BsTabs`/`BsAccordion`/`BsCollapse` — zero-JS. |
| [Toasts](bootstrap-toasts.md) | `BsToast` and the `BsToaster` outlet for `IToaster` messages. |
| [Form controls](bootstrap-forms.md) | The `IFormControl<T>` inputs: `BsInput`/`BsTextarea`/`BsCheck`/`BsRadioGroup`/`BsCheckboxGroup` + layout helpers. |
| [Selects & multiselect](bootstrap-select.md) | The searchable, keyboard-contained `BsSelect`/`BsMultiSelect` comboboxes (opt-in `Filter`). |
| [Date & time pickers](bootstrap-pickers.md) | The hand-editable `BsDatePicker`/`BsTimePicker`/`BsDateTimePicker`. |
| [Utility classes](bootstrap-utilities.md) | The typed utility-class tokens composed with `Bs.Join(...)`. |

## Reference

| Reference | What it covers |
|-----------|----------------|
| [Diagnostics (RASK001–042)](diagnostics.md) | Every analyzer/generator diagnostic, what triggers it, and how to fix it. |
| [Code analysis](code-analysis.md) | Analyzers, warnings-as-errors, and the per-PR adoption procedure. |

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
