# Rask documentation

Guides and references for building with Rask, **the full-stack .NET web framework** — UI, data, auth,
background work, realtime and deploy, all in C#, for a team of one or fifty. **New to Rask?** Read
[Getting started](getting-started.md) start to finish — it goes from zero to a running, routed,
interactive app. **Ready to build something real?** The [**Tutorial**](tutorial/00-overview.md) takes you
from an empty folder to a deployed, database-backed product that uses every pillar. Want the philosophy
first? Read **[The One Person Framework](one-person-framework.md)**. Want the pitch and a quick demo?
See the project [README](../README.md). Already building? Keep the [**Cheat sheet**](cheatsheet.md) open
and reach for the [**Recipes**](recipes.md) when you need "how do I do X?".

## Start here

| Guide | What it covers |
|-------|----------------|
| [**The One Person Framework**](one-person-framework.md) | The philosophy: a whole product from one C# codebase on one server, SQLite-first — why it lets one developer ship alone and a team move as fast. |
| [**Tutorial: zero to deploy**](tutorial/00-overview.md) | Build the "Shop" app end to end — scaffold → first DB-backed feature → auth → jobs → email → cache → events → production SQLite → push → ops → deploy to one box. One chapter per pillar; you build the app as you go, starting from `rask new Shop`. |
| [**Cheat sheet**](cheatsheet.md) | The one page to keep open — every CLI command and flag, wiring one-liner (`AddRask…`), and code idiom, dense and scannable. |
| [**Recipes**](recipes.md) | Task-first "how do I do X?" — add a feature to an existing database, gate a page, run a job, cache a query, deploy an update — the command, the wiring line, and where to go deeper. |
| [Roadmap](roadmap.md) | The pillars — what's shipped (DB-backed jobs, outbox, mail, cache, file storage, subscriptions, multi-tenancy, full-text search), what's partial, and what's next. |

## Guides

| Guide | What it covers |
|-------|----------------|
| [Installing Rask](installation.md) | The one-line installer — what it puts where, every option, upgrading, uninstalling, and the manual path if you would rather not run a script from the internet. |
| [Getting started](getting-started.md) | Prerequisites, scaffold an app, a tour of the generated files, your first component, interactivity, routing, and troubleshooting. |
| [The `rask` CLI](cli.md) | The `Rask.Cli` .NET tool — the whole lifecycle: `rask new` (scaffold), `rask db` (migrations), `rask dev` (hot-reload run), `rask deploy` (bare box → live HTTPS site), `rask info`, `rask doctor`, `rask completion`. |
| [Best practices](best-practices.md) | Production patterns and common pitfalls across component design, state, forms, data access, security, accessibility, performance and testing — each linking to the deep dive. |
| [Building components](building-components.md) | How markup is written: naming a component and chaining onto it, the properties a component demands before it exists, bound versus controlled form controls, and what the IDE offers at each step. |
| [Building form controls](building-form-controls.md) | Author your own `IFormControl<T>`: two-way binding, per-field validation and a controlled mode, with a full example. |
| [Elements & the DSL](elements.md) | The primitives every component is built from: tag entries, universal attributes, the children indexer, `Text`/`Raw`, SVG, and the element catalog. |
| [Routing](routing.md) | `[Route]`, route/query params, nested routes, type-safe `Routes.*` URLs, `Go`, `RouteState`. |
| [Subscriptions](subscriptions.md) | Keeping a page current. `[Live(typeof(Order))]` on a query refetches it when anyone writes an order — no event, no record, no policy, and nothing in `Render`. For what is genuinely an event, `QueryClient.Subscribe<T>()` re-renders every subscribed page, narrowed by an `ISubscription<T>` record and its watch policy, over server-sent events from WebAssembly. `Dispatcher.Publish(…)` publishes with nothing injected. |
| [Composition](composition.md) | Children & fragments, callbacks (child→parent), context (provide/consume), built-in toasts (`Toast.Success("Saved")`), `VirtualizeModel`, drag-and-drop. |
| [Composition — callbacks & context](composition-callbacks-context.md) | `Callback` properties that re-render the owner, and values passed down with context instead of prop drilling. |
| [Composition — lists & more](composition-lists.md) | `Virtualize`, keyed lists, toasts, drag-and-drop and error boundaries. |
| [JS interop](js-interop.md) | Scoped CSS & TypeScript conventions (a `.js` sibling is RASK055), calling JS via `IJSRuntime`, element refs (`ElementRef<T>`), typed browser APIs, asset delivery. |
| [JS interop — runtime](js-interop-runtime.md) | Calling JavaScript through `IJSRuntime`, typed element refs, and wrapping a third-party library with TypeScript types. |
| [Web APIs from MDN](web-apis.md) | Every web API the browser ships, generated from MDN into C# (`Rask.Web`): `await Navigator.Clipboard.WriteText("hi")`, one round trip per await, kept objects as handles. |
| [Browser APIs](browser-apis.md) | The map of the typed Web-API wrappers and where `Rask.Web` takes over — shared vs WASM-only, one-shot vs subscription, the inject-from-ctor and push/`[JSInvokable]` patterns. |
| [Browser APIs — sharing model](browser-apis-sharing.md) | Which APIs run on which host, declarative Web Share and gesture triggers on the server, and subscriptions that push updates into C#. |
| [Browser APIs — reference & demos](browser-apis-reference.md) | A runnable demo per API with the C# beside the result: storage, environment, location, sensors, observers, media, crypto and files. |
| [Capability matrix](browser-capabilities.md) | Where each typed wrapper works (Web / PWA) — links to a reference page per API under [`apis/`](apis/). |
| [📱 Mobile & PWA](pwa.md) | Build installable, offline mobile apps in C# (WASM): web app manifest, service worker, Web Push (MDN's `PushManager`), `rask new MyApp --template wasm`. |
| [AOT compilation](aot.md) | Opt-in full WASM AOT (`-p:RaskWasmAot=true`): the reflection-free binding registry, registering custom `IParsable` types, `InvokeAsync<T>` under AOT, and the continuous analyzer gate. |
| [Prerendering](prerendering.md) | Render a standalone WASM app's pages to real HTML at publish (`<RaskPrerender>true</RaskPrerender>`), so a crawler gets the page instead of the boot spinner: what is written, which routes are skipped and why, and why a route that throws is deliberately left out. |
| [Forms & validation](forms.md) | Two-way binding, `Form.Model(m)`/`EditContext`, inline `.Validate(…)` rules, radio & checkbox groups. |
| [Forms — validation](forms-validation.md) | Inline `.Validate(…)` on a field or the form, sync or async, with the rules kept in a value object; DataAnnotations and FluentValidation as alternatives; the validating indicator and first-error-wins. |
| [Forms — advanced](forms-advanced.md) | Nested models and collections, radio and checkbox groups, form state kept across a redeploy. |
| [Validation](validation.md) | Inline `.Validate(…)` rules in a form; `[Required]` and `AbstractValidator<T>` are also supported and run in a form and on every dispatched request, with nothing declared. The off switch, validators that need services, and what a rejected request looks like on the wire. |
| [Live pages](render-modes.md) | How a Server page reaches the browser: every page live with a session of its own, waiting for async data before the first byte, cache headers, and setting a status or redirecting on load. |
| [Lifecycle](lifecycle.md) | `OnMount` / `OnUpdated` / `OnFirstRender` / `OnRendered` / `OnUnmount` — one hook per moment, cancellation, common gotchas. |
| [Authentication](authentication.md) | Accounts on your own `User`: sessions you can list and end, PBKDF2 or bcrypt, `Authorize`, route guards, OIDC (Keycloak / Auth0 / Cognito / Duende). |
| [Authentication — cookie](authentication-cookie.md) | Cookie login and sessions wired by hand, for the Server host and for a WebAssembly app backed by your own API. |
| [Authentication — providers](authentication-providers.md) | Sign in through Keycloak, Auth0, AWS Cognito or Duende IdentityServer over OpenID Connect, with Rask still owning the session cookie. |
| [Authentication — hardening](authentication-hardening.md) | The session trust model, CSRF, forwarded headers behind a proxy, Content-Security-Policy and a production checklist. |
| [Accessibility](accessibility.md) | Setting ARIA attributes, `Role`/`TabIndex`, and focus on any element; the `Img` alt-text analyzer (RASK023). |
| [Localization](localization.md) | Ship in more than one language: the visitor's culture negotiated per request, dates and numbers in their format, text from typed JSON catalogs (a missing key is a compile error), plural grammar per language, `<html lang>`/`dir`, and the WASM ICU opt-in. |
| [Testing](testing.md) | Unit-testing components with `Rask.Testing`, driving event handlers, when to reach for E2E. |
| [DevTools](devtools.md) | The in-page panel of a Debug build: every frame the page and the app exchange, and the component tree nested as on the page with each component's props (sensitive ones redacted at build time). On only in Development on this machine; nothing of it ships. |
| [Migrating from Blazor](migration-from-blazor.md) | Concept mapping, behavioural gotchas, and what stays the same. |
| [Building with AI assistants](ai-agents.md) | `llms.txt` and the published guide set that let AI tools scaffold and extend Rask apps. |

## The full-stack batteries (the back half)

The opinionated, DB-backed pillars that let a small team ship like a big one, and one developer ship alone —
each a thin, trim/AOT-safe package that rides the app's own database (SQLite by default). No Redis, no broker,
no second server. Walk through them in order
in the [Tutorial](tutorial/00-overview.md); the reference for each is here.

| Guide | What it covers |
|-------|----------------|
| [Rask.Data](data.md) | Declare an `Aggregate<TId>` and nothing else: writes off the type (`Product.Create(model)`), reads off its generated read face (`Product.Where(…)`) whose navigations are inferred from the ids the aggregates hold, generated form models (`ProductModel`), value objects with no marker, collections of values as one column, audit stamps, opt-in soft delete (`Deletion.Soft`, or `Deletion.None` for a table nothing deletes), optimistic concurrency, domain events, the signed-in user with nothing injected (`Current.UserId`), multi-tenancy and ranked full-text search (`Product.Search(text)`). |
| [Multi-tenancy](multi-tenancy.md) | One `const` partitions a table by tenant — a `TenantId`, a query filter no read composes away, tenant-prefixed indexes — with the tenant taken from the signed-in user; `Tenant.Use`/`Tenant.Across` for explicit work, and what jobs, mail, the outbox, the cache, file storage and accounts do with it. |
| [Data access (EF Core)](data-access.md) | Plain EF Core + SQLite with a `DbContext` of your own: `IDbContextFactory`, loading in the lifecycle, vertical slices, a DDD aggregate + value objects, and the SQLite decimal gotcha. |
| [SQLite production pragmas](sqlite.md) | Production SQLite via `UseRaskSqlite` / `AddRaskSqlite` (standalone `Rask.SQLite`): WAL, `foreign_keys`, `busy_timeout` & friends applied on every connection open, STRICT tables, FTS5 full-text search and JSON path indexes through EF Core, the browser database (`Rask.SQLite.Browser`), plus Litestream backup. |
| [Full-text search](full-text-search.md) | `HasFullTextSearch` + `Search(text)`: ranked, word-aware, diacritic-insensitive search from LINQ, with highlighted matches — on SQLite FTS5, in the browser and on PostgreSQL (`tsvector` + GIN). |
| [CQRS](cqrs.md) | Source-generated, trim-safe queries / commands / events and pipeline behaviors via `AddRaskCqrs()` + `IDispatcher` (standalone `Rask.Cqrs`). |
| [HTTP APIs](api-endpoints.md) | Ordinary API controllers and minimal API endpoints, hosted properly and callable without a URL: `AddRaskApi()` + `MapRaskApi()` map them and answer 404 with a problem document under `/api` — where the catch-all used to render the app with a 200 — and `Rask.Api.Client` generates one typed client per controller straight from the declaration, so a route renamed on the server breaks the call site at compile time instead of at 404 time. For when someone other than your own browser code has to call you; [CQRS](cqrs.md) is the answer when nobody does. |
| [HTTP & files](http-and-files.md) | Fetching JSON with an injected `HttpClient`, uploads through a typed file picker, and downloads sent to the browser, on both hosts. |
| [Blazor components](blazor-components.md) | A **real** Blazor component — from a Razor Class Library, MudBlazor, Radzen — as an ordinary Rask component: derive a `partial` class from `BlazorComponent<T>` and place it anywhere the chain goes. The Razor SDK compiles `.razor` untouched; Rask renders the result server-side into the *first* HTTP response, passes parameters as live C# objects rather than JSON, and wires the hosted component's own `@onclick` to Rask's existing channel so it fires with no Blazor circuit. Runs on both hosts, a trimmed WebAssembly publish included (the hosted type is DAM-annotated, or the trimmer removes its `[Parameter]` setters and the island renders empty). A statically rendered island is deliberately not opaque. |
| [Single-page app front ends](spa.md) | A React, Vue, Svelte or Angular app that builds with npm, inside an ASP.NET host: `dotnet build` runs its `npm ci` and `npm run build`, `dotnet publish` ships the bundle, and `MapRaskSpa()` serves it (standalone `Rask.Spa.Hosting`) with cache headers, precompressed files and a fallback that still 404s a missing asset. |
| [Islands](islands.md) | A `.tsx`, `.vue`, `.svelte`, Angular or Lit file as an *ordinary Rask component*: derive from one of seven base classes — `ReactComponent`, `PreactComponent`, `SolidComponent`, `VueComponent`, `SvelteComponent`, `AngularComponent`, `LitComponent` — drop the front-end file beside it, and place it anywhere the chain goes — a leaf, a subtree, or a whole route. Props are declared in C# and serialized without reflection, callbacks re-enter C# over the channel every DOM handler already uses, and the live diff treats the subtree as opaque because its own renderer owns it. |
| [Tailwind CSS](tailwind.md) | Every project, no flag and no package: Tailwind v4 ships inside the host package and is compiled by `dotnet build` with no npm, no config file and no `node_modules` — it scans your C# string literals for class names. The standalone binary where one exists, npm where it doesn't, so no platform is left out. |
| [Rask.Query](query.md) | The dispatcher wrapped in a cache for Rask components (standalone `Rask.Query`), reached through the static `QueryClient`: request dedup, staleness, background refetch, TanStack-shaped keys matched by prefix, commands (`Command<T>`) that invalidate what they change, and a `Rask.Data` write refreshing the queries about what it wrote. |
| [Background jobs](jobs.md) | Durable enqueued / delayed / recurring work on the app's own database via `AddRaskJobs<Ctx>()` + `IJobs` (standalone `Rask.Jobs`) — at-least-once, with backoff. |
| [Transactional email](mail.md) | Durable email queued on the app's own database via `AddRaskMail<Ctx>()` + `IMail` (standalone `Rask.Mail`) — delivered off the request thread over SMTP with backoff; bodies are Rask components. |
| [Cache](cache.md) | A developer-facing cache on the app's own database via `AddRaskCache<Ctx>()` (standalone `Rask.Cache`) — standard `IDistributedCache` plus `Cache.Remember(key, load).For(10.Minutes)`, sliding and absolute expiry. |
| [File storage](file-storage.md) | Uploaded files kept on disk, in an S3-compatible bucket or in Azure Blob via `AddRaskStorage<Ctx>()` (standalone `Rask.Storage`) — a `StoredFile` row per file on the app's own database, content types sniffed from the bytes, public and temporary URLs, and downloads behind your own authorization check. |
| [Outbox](outbox.md) | Durable, crash-safe domain-event delivery via `AddRaskOutbox<Ctx>()` (standalone `Rask.Outbox`) — events committed in the same transaction as your data, delivered post-commit with retries. |
| [Web Push](webpush.md) | Server-sent Web Push from your backend via `AddRaskWebPush()` + `IWebPush` (standalone `Rask.WebPush`) — VAPID + aes128gcm, zero deps; the browser subscribes with MDN's `PushManager` from `Rask.Web`. |
| [Secrets](secrets.md) | Where an app's passwords and API keys live, how they reach the server, and what Rask deliberately doesn't do with them. |
| [Dashboard](dashboard.md) | A built-in operator dashboard at `/_rask` via `AddRaskDashboard<Ctx>()` (standalone `Rask.Dashboard`) — queue depth and dead letters for the outbox/jobs/mail, cache contents, a live log tail (plus searchable history with `Rask.Logging`), SQLite pragmas; fail-closed behind an authorization policy. |
| [UI kit](ui-kit.md) | Every daisyUI component as a typed Rask component (standalone `Rask.Ui`) — buttons, dialogs, menus, forms, calendars, mockups and the chrome the framework's own surfaces are drawn with. Mobile-first, ships no JavaScript, and brings its own compiled stylesheet with daisyUI inside it, so a consuming app needs no npm install and no Tailwind configuration. |
| [Data grid](data-grid.md) | `Ui.DataGrid` — sortable headers, paging, selection, expandable detail rows, grouping, a column chooser, and a card layout on a phone. Columns arrive through a factory because C# cannot infer a cell lambda any other way; rows come from a list, an `IQueryable` or an awaited `Source`. |
| [Tree](tree.md) | `Ui.Tree` — expandable hierarchies with the WAI-ARIA keyboard and type-ahead, single or multiple selection, hover, and a virtualized mode for thousands of nodes. A node's children arrive through the indexer; expansion and selection are each the page's or the tree's own. |
| [Logging](logging.md) | A durable log store via `AddRaskLogging()` (standalone `Rask.Logging`) — the `ILogger` pipeline kept in a SQLite file of its own, or in a PostgreSQL or SQL Server app's own database, buffered off the request thread, with retention by age and row count and a searchable view in the dashboard. |
| [Observability](observability.md) | Structured logging, the `Rask.Server` meter and activity source, health checks — what to export and what the numbers mean. |
| [Configuration](configuration.md) | Every setting lives in `appsettings.json` under `Rask`: precedence, environment variables, value formats, every section and its options type, what stays in code, and migrating from the old keys. |
| [Deployment](deployment.md) | Ship to a single box with `rask deploy`: Docker over SSH, a shared Caddy proxy for automatic HTTPS, zero-downtime blue-green swaps gated on `/health`, and bare-VPS setup. |
| [Scaling](scaling.md) | How far one box goes — measured, in sessions and in events per second — what survives a restart or a deploy, where the wall actually is, and what it takes to get past it. |

## Reference

| Reference | What it covers |
|-----------|----------------|
| [Diagnostics (RASK001–101, RASKISLAND001–018)](diagnostics.md) | Every analyzer/generator diagnostic and every islands build error, what triggers it, and how to fix it. |
| [Code analysis](code-analysis.md) | Analyzers, warnings-as-errors, and the per-PR adoption procedure. |
| [Public API style](api-style.md) | How every public name is chosen, and the gate that records the surface. |

## Contributing

| Doc | What it covers |
|-----|----------------|
| [Development workflow](development-workflow.md) | The format → warnings-as-errors → tests → benchmarks → docs → review gate, landing on `main`, CI, nightly, releases. |
| [Repo administration](repo-administration.md) | Branch protection, required checks, secrets, and the settings this repository expects. |

## Architecture

| Doc | What it covers |
|-----|----------------|
| [Live rendering & the diff codec](architecture/live-rendering.md) | How the render walk, frame stream, edit-op diff, keyed reconciliation, and the two transports (Server WS / WASM JSImport) work. |

---

The in-repo map for contributors lives in [CLAUDE.md](../CLAUDE.md). Every feature demo runs live at
[rask.sh/docs](https://rask.sh/docs), out of [`src/Rask.Site`](../src/Rask.Site) — one app, published
to the domain, so a demo you read here is the one you can click.
