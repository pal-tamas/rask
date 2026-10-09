namespace Rask.Site.Features;

public static class GuideCatalog
{
    public static readonly GuideEntry[] All =
    [
        // ---- Start here ----
        new("installation", "Installing Rask", "One line to the CLI and everything it needs — options, upgrade, uninstall.", "Start here")
        {
            SearchTitle = "Install the CLI and .NET 10 SDK in one command",
            Description = "Install the rask CLI with one curl or PowerShell command. It adds the .NET SDK, dotnet-ef, wasm-tools and Node.js LTS under your home directory, no sudo.",
        },
        new("getting-started", "Getting started", "Scaffold a project and build your first component.", "Start here")
        {
            SearchTitle = "Getting started: a full-stack C# web app in .NET",
            Description = "Scaffold and run your first app, then write a C# component, handle events and add a route. Components render on the server over WebSocket or in WebAssembly.",
        },
        new("cheatsheet", "Cheat sheet", "Every CLI command, flag, and wiring one-liner on one page.", "Start here")
        {
            SearchTitle = "Cheat sheet: CLI commands and .NET wiring lines",
            Description = "One dense page of every rask CLI command and flag, a CRUD slice in one place, and the idioms for data, CQRS, jobs, mail, cache, outbox, auth and routing.",
        },
        new("recipes", "Recipes", "Task-first: how do I add a feature, gate a page, run a job, deploy?", "Start here")
        {
            SearchTitle = "Recipes: common tasks in a .NET web app",
            Description = "Task-first answers for an existing app: add a CRUD feature, relate entities, require login, run background work, send email, cache queries, deploy and test.",
        },
        new("best-practices", "Best practices", "Production patterns for state, forms, security, and perf.", "Start here")
        {
            SearchTitle = "C# web component best practices: security and perf",
            Description = "Patterns that keep a C# component app correct, secure and fast: keys and encoding, state and callbacks, forms, routing, JS interop, accessibility and testing.",
        },
        new("migration-from-blazor", "Migrating from Blazor", "Concept mapping and behavioural differences.", "Start here")
        {
            SearchTitle = "Blazor alternative: migrating from Blazor to C#",
            Description = "Map Blazor concepts to plain C# components: .razor to Render(), [Parameter] to properties, cascading values to Context, lifecycle, routing and forms.",
        },
        new("one-person-framework", "Philosophy: the One Person Framework",
            "Why one codebase and one server carry a whole product — for one developer or a team.", "Start here")
        {
            SearchTitle = "The .NET One Person Framework: one codebase, one app",
            Description = "The philosophy behind Rask: one C# codebase on one server, SQLite-first data, built-in batteries and a one-command deploy, for one developer or a team.",
        },
        new("roadmap", "Roadmap", "The full-stack pillars — shipped and planned.", "Start here")
        {
            SearchTitle = "Roadmap: shipped and planned framework pillars",
            Description = "What is shipped and what is next for the full-stack .NET web framework: UI hosts, CLI, CQRS, data, jobs, mail, cache, outbox and SQLite, plus known gaps.",
        },

        // ---- Tutorial ----
        new("00-overview", "Ch 0 · Overview", "What you'll build: a whole product, one pillar per chapter.", "Tutorial", "tutorial/00-overview.md")
        {
            SearchTitle = "Tutorial: build and deploy a full .NET web app",
            Description = "Build Shop, a database-backed .NET app, chapter by chapter: EF Core and SQLite CRUD, auth, jobs, email, cache, outbox events, Web Push and deploy.",
        },
        new("01-scaffold", "Ch 1 · Scaffold", "Scaffold the app with rask new.", "Tutorial", "tutorial/01-scaffold.md")
        {
            SearchTitle = "Tutorial 1: scaffold an ASP.NET Core app",
            Description = "Create the Shop project with rask new, run it, and tour what the server template generates: an ASP.NET Core app with SQLite, auth and batteries wired.",
        },
        new("02-first-feature", "Ch 2 · First feature", "Declare an entity and build its list, create and edit pages.", "Tutorial", "tutorial/02-first-feature.md")
        {
            SearchTitle = "Tutorial 2: CRUD with EF Core and SQLite in C#",
            Description = "Build a database-backed Products catalog: declare the aggregate, write its list, create and edit pages, bind Rask.Ui forms, then add the migration.",
        },
        new("03-orders-and-auth", "Ch 3 · Orders & auth", "A second feature, and locking it down.", "Tutorial", "tutorial/03-orders-and-auth.md")
        {
            SearchTitle = "Tutorial 3: a second feature and login authorization",
            Description = "Add an Orders feature on the same DbContext and SQLite database, then lock down catalog edits with [Authorize] pages and the Authorize component in C#.",
        },
        new("04-background-jobs", "Ch 4 · Background jobs", "Run work off the request thread.", "Tutorial", "tutorial/04-background-jobs.md")
        {
            SearchTitle = "Tutorial 4: durable background jobs in .NET",
            Description = "Move order processing off the request thread with a durable background job stored in SQLite: write a job and handler, enqueue it, and let it retry on failure.",
        },
        new("05-email", "Ch 5 · Email", "Transactional email off the request thread.", "Tutorial", "tutorial/05-email.md")
        {
            SearchTitle = "Tutorial 5: transactional email in a .NET app",
            Description = "Email customers an order receipt whose body is a C# component. Mail is queued in SQLite and sent over SMTP by a background worker, triggered from a job.",
        },
        new("06-cache", "Ch 6 · Cache", "Cache the catalog on your own database.", "Tutorial", "tutorial/06-cache.md")
        {
            SearchTitle = "Tutorial 6: cache database queries in .NET",
            Description = "Cache the product list in a typed, SQLite-backed cache: write an accessor around Cache.Remember, read through it, and forget it when the catalog changes.",
        },
        new("07-outbox-events", "Ch 7 · Outbox & events", "Domain events with the transactional outbox.", "Tutorial", "tutorial/07-outbox-events.md")
        {
            SearchTitle = "Tutorial 7: domain events and a transactional outbox",
            Description = "Raise a domain event when an order is placed; each handler runs in memory, or durably through a transactional outbox that survives a crash. C# and EF Core.",
        },
        new("08-production-sqlite", "Ch 8 · Production SQLite", "WAL, pragmas, and continuous backup.", "Tutorial", "tutorial/08-production-sqlite.md")
        {
            SearchTitle = "Tutorial 8: production SQLite, WAL and Litestream",
            Description = "Make one SQLite file production-safe for a .NET app: WAL, busy-timeout and foreign-key pragmas, scheduled snapshots and off-box backup with Litestream.",
        },
        new("09-web-push", "Ch 9 · Push", "Send Web Push from your own server, on your own keys.", "Tutorial", "tutorial/09-web-push.md")
        {
            SearchTitle = "Tutorial 9: Web Push notifications from .NET",
            Description = "Send Web Push from your own ASP.NET Core server with VAPID keys: the scaffolded wiring, subscribing a browser, and pushing from an outbox handler.",
        },
        new("10-ops", "Ch 10 · Watching it run", "An ops page over every pillar's own table.", "Tutorial", "tutorial/10-ops.md")
        {
            SearchTitle = "Tutorial 10: monitor background workers in .NET",
            Description = "Build an /ops page that reads each pillar's SQLite table to show outbox, job, mail and cache state, make it live, check pragmas, and meet the dashboard.",
        },
        new("11-deploy", "Ch 11 · Deploy", "Ship to one box with rask deploy.", "Tutorial", "tutorial/11-deploy.md")
        {
            SearchTitle = "Tutorial 11: deploy a .NET app to a VPS with HTTPS",
            Description = "Ship the app to one Linux server with rask deploy: a Docker image built over SSH, automatic Let's Encrypt HTTPS via Caddy, health-checked swaps, CI deploys.",
        },

        // ---- Data ----
        new("data", "Rask.Data", "Declare aggregates, write them off the type, query their generated read faces, bind generated form models.", "Data")
        {
            SearchTitle = "EF Core aggregates without writing a DbContext in C#",
            Description = "Declare DDD aggregates, entities and value objects in C# with EF Core: query read faces, map value collections to JSON, partition tables by tenant.",
        },
        new("data-access", "Data access", "EF Core + SQLite, vertical slices, DDD patterns.", "Data")
        {
            SearchTitle = "Data access with EF Core and SQLite",
            Description = "Wire EF Core and SQLite into a server app: register the DbContext with a factory for long-lived sessions, load data in the lifecycle, seed and test it.",
        },
        new("query", "Rask.Query", "The dispatcher wrapped in a cache: dedup, staleness, invalidation.", "Data")
        {
            SearchTitle = "TanStack Query-style data caching for C#",
            Description = "Wrap the CQRS dispatcher in a TanStack Query-style cache for C# components: request dedup, staleness, background refetch, keys, invalidation and commands.",
        },
        new("cqrs", "CQRS", "Source-generated queries, commands, events, behaviors.", "Data")
        {
            SearchTitle = "CQRS in .NET with source-generated handlers",
            Description = "A source-generated CQRS mediator for .NET: dispatch queries, commands and events through IDispatcher with no reflection, plus pipeline behaviors.",
        },
        new("sqlite", "Production SQLite", "WAL + busy-timeout pragmas, continuous backup, snapshots.", "Data")
        {
            SearchTitle = "SQLite in production for .NET: WAL and pragmas",
            Description = "Run SQLite as a production database with ADO.NET or EF Core: WAL pragmas, BEGIN IMMEDIATE retries, STRICT tables, FTS5 full-text search and Litestream backup.",
        },
        new("multi-tenancy", "Multi-tenancy", "One app, many customers: rows owned by a tenant, filtered on every query.", "Data")
        {
            SearchTitle = "Multi-tenant .NET apps with EF Core tenant filters",
            Description = "Serve many customers from one C# app and one database: a table opts in to a tenant id, every query is filtered to the current tenant, and writes are stamped.",
        },
        new("full-text-search", "Full-text search", "Ranked search over your own tables — SQLite FTS5 or PostgreSQL — with highlights.", "Data")
        {
            SearchTitle = "Full-text search in EF Core on SQLite or PostgreSQL",
            Description = "Add ranked full-text search to a C# app on its own database, SQLite FTS5 or PostgreSQL: declare the searched columns, query from EF Core, highlight the matches.",
        },
        new("outbox", "Outbox", "Crash-safe domain-event delivery on your database.",
            "Data")
        {
            SearchTitle = "Transactional outbox pattern in .NET, no broker",
            Description = "Run event handlers crash-safely with a transactional outbox on your app's database: a durable handler commits with the data and runs at-least-once, no broker.",
        },

        // ---- Auth ----
        new("authentication", "Authentication", "Accounts, passkeys, sessions and route guards on Server and WASM.", "Auth")
        {
            SearchTitle = "Passkeys and your own accounts in C#",
            Description = "Add passkeys and accounts to a C# app: WebAuthn verified on the BCL, sign-in pages you own, a session row per device, PBKDF2 or bcrypt, throttling.",
        },
        new("authentication-cookie", "Auth — cookie", "Cookie login and session on Server and on a WASM SPA with an API host.", "Auth")
        {
            SearchTitle = "Cookie authentication for server and WASM apps",
            Description = "Wire cookie-based login and sessions by hand, for the server-rendered WebSocket host and for a WebAssembly SPA backed by your own ASP.NET Core API.",
        },
        new("authentication-providers", "Auth — providers", "Keycloak, Auth0, and other OIDC providers.", "Auth")
        {
            SearchTitle = "OpenID Connect and external identity providers",
            Description = "Sign in through Keycloak, Auth0, AWS Cognito or Duende IdentityServer over OpenID Connect, with Rask still owning the session cookie.",
        },
        new("authentication-hardening", "Auth — hardening", "Production hardening for cookies, tokens, and sessions.", "Auth")
        {
            SearchTitle = "Production security hardening for .NET web apps",
            Description = "Harden authentication for production: session trust model, CSRF protection, reverse proxy forwarded headers, Content-Security-Policy and a security checklist.",
        },

        // ---- Backend services ----
        new("jobs", "Background jobs", "Durable enqueued / delayed / recurring work on your database.", "Backend services")
        {
            SearchTitle = "Durable background jobs in .NET on your database",
            Description = "Run .NET background jobs off the request thread, stored in your app's database with no Redis or broker: enqueued, delayed and recurring, retried with backoff.",
        },
        new("mail", "Transactional email", "Durable email queued on your database, delivered over SMTP.", "Backend services")
        {
            SearchTitle = "Queued transactional email in .NET over SMTP",
            Description = "Send transactional email from .NET off the request thread: messages queue in your database, bodies are C# components, and a worker delivers over SMTP.",
        },
        new("cache", "Cache", "A database-backed IDistributedCache plus a typed ICache.", "Backend services")
        {
            SearchTitle = "Database-backed IDistributedCache for ASP.NET Core",
            Description = "A cache stored in your app's own database instead of Redis: it implements IDistributedCache and adds Cache.Remember(key, load).For(10.Minutes).",
        },
        new("file-storage", "File storage", "Uploads kept on disk, S3 or Azure, with a row per file.", "Backend services")
        {
            SearchTitle = "File uploads in ASP.NET Core on disk, S3 or Azure",
            Description = "Keep user uploads in .NET on disk, S3, R2 or Azure Blob with a row in your database: sniffed content types, size limits, public and signed temporary URLs.",
        },
        new("api-endpoints", "HTTP APIs", "Controllers and minimal APIs, called through a generated typed client.", "Backend services")
        {
            SearchTitle = "ASP.NET Core APIs with a generated typed client",
            Description = "Host ASP.NET Core controllers and minimal APIs, and get a typed HTTP client generated from them, with validation from [Required] and AbstractValidator rules.",
        },
        new("http-and-files", "HTTP & files", "Fetch JSON with a DI'd HttpClient; upload and download files.", "Backend services")
        {
            SearchTitle = "HttpClient, file uploads and downloads in C#",
            Description = "Fetch JSON with a dependency-injected HttpClient, accept uploads through a typed file picker, and send downloads to the browser, on server and WASM hosts.",
        },

        // ---- Realtime ----
        new("subscriptions", "Subscriptions", "Keep a page current: a live query, or an event published to every page subscribed to it.", "Realtime")
        {
            SearchTitle = "Live queries and real-time subscriptions in C#",
            Description = "Keep C# pages current: Live() refetches a query when anyone writes, and QueryClient.Subscribe pushes real events to every subscribed component.",
        },
        new("webpush", "Web Push (server)", "Subscribe with MDN's PushManager, send Web Push from your backend — VAPID keys, delivery results.", "Realtime")
        {
            SearchTitle = "Send Web Push notifications from .NET",
            Description = "Send Web Push notifications from an ASP.NET Core backend to subscribed browsers with your own VAPID keys, aes128gcm encryption and zero external dependencies.",
        },
        new("webrtc", "IWebRtc", "Typed browser API: IWebRtc.", "Realtime", "apis/webrtc.md")
        {
            SearchTitle = "WebRTC Data Channels in C# and .NET (IWebRtc)",
            Description = "Connect two browsers peer-to-peer from C# with IWebRtc: WebRTC data channels with batched messages plus camera, microphone and screen streams.",
        },
        new("signaling", "ISignaling", "Typed browser API: ISignaling.", "Realtime", "apis/signaling.md")
        {
            SearchTitle = "WebRTC Signaling Server in C# and .NET (ISignaling)",
            Description = "Relay WebRTC offers, answers and ICE candidates between browsers with ISignaling and a WebSocket relay on any ASP.NET host. Authentication is on by default.",
        },

        // ---- Frontend ----
        new("building-components", "Building components",
            "Naming a component and chaining onto it; what a component demands before it exists.", "Frontend")
        {
            SearchTitle = "Building C# components with the markup chain",
            Description = "Learn how C# markup chains work: required steps come first, bound versus controlled form controls, callbacks, your own components, and lists of components.",
        },
        new("elements", "Elements & the DSL", "Primitives, tag entries, universal props, SVG, the element catalog.", "Frontend")
        {
            SearchTitle = "HTML and SVG elements as typed C# components",
            Description = "Reference for HTML in C#: Text, Raw and Doctype primitives, a typed entry for every HTML and SVG element, universal attributes and the children indexer.",
        },
        new("routing", "Routing", "Route attributes, params, nested layouts, type-safe URLs.", "Frontend")
        {
            SearchTitle = "Routing and type-safe URLs in C#",
            Description = "Declare routes with [Route] and navigate with source-generated, type-safe URLs. Covers route and query parameters, nested layouts, Go.To and Go.With.",
        },
        new("composition", "Composition", "Children, fragments, callbacks, context, virtualize.", "Frontend")
        {
            SearchTitle = "Component composition: children and fragments",
            Description = "How C# components compose: children and fragments through the indexer, static vs stateless vs stateful components, and hosting components built at runtime.",
        },
        new("composition-callbacks-context", "Composition — callbacks & context", "Child→parent callbacks and provide/consume context.", "Frontend")
        {
            SearchTitle = "Child-to-parent callbacks and context in C#",
            Description = "Send events from a child component to its parent with Callback properties that re-render the owner, and pass values down with context, not prop drilling.",
        },
        new("composition-lists", "Composition — lists & more", "Virtualize, keyed lists, toasts, drag-and-drop, error boundaries.", "Frontend")
        {
            SearchTitle = "Virtualized lists, toasts and drag and drop",
            Description = "Render windowed lists with Virtualize, keep list identity with keys, and add toast messages, drag-and-drop and error boundaries to C# web components.",
        },
        new("lifecycle", "Lifecycle", "Mount, updated, first render, rendered, unmount, cancellation.", "Frontend")
        {
            SearchTitle = "Component lifecycle hooks in C#",
            Description = "The lifecycle hooks a component can override, their order and sync vs async rules, plus disposal, cancellation tied to component lifetime and hosted services.",
        },
        new("render-modes", "Live pages", "Every page live: WebSocket or HTTP fallback, waiting for async data, status codes, redirects.", "Frontend")
        {
            SearchTitle = "Live server-rendered pages without hydration in C#",
            Description = "Server-rendered HTML with no hydration, a live session per page over a WebSocket or an automatic HTTP fallback, async data, status codes and redirects.",
        },
        new("forms", "Forms & validation", "Two-way binding, Form.Model(m), inline Validate rules.", "Frontend")
        {
            SearchTitle = "Forms and two-way data binding in C#",
            Description = "Bind inputs two-way with typed Bind expressions, build forms on an EditContext, track touched and modified fields, and show accessible validation messages.",
        },
        new("forms-validation", "Forms — validation", "Inline Validate rules, sync or async; attributes and FluentValidation too.", "Frontend")
        {
            SearchTitle = "Form validation with inline rules in C#",
            Description = "Validate a form with inline rules on a field or the form, sync or async, or with DataAnnotations. A database unique-constraint error shows under its field.",
        },
        new("forms-advanced", "Forms — advanced", "Nested/complex models, radio & checkbox groups, custom controls.", "Frontend")
        {
            SearchTitle = "Nested form models and custom form controls",
            Description = "Bind and validate nested models and collections, build radio and checkbox groups, keep form state across a redeploy, and write your own form controls in C#.",
        },
        new("validation", "Validation", "Inline rules in a form; attributes and AbstractValidator<T> on requests too.", "Frontend")
        {
            SearchTitle = "Model validation for forms and HTTP requests",
            Description = "Built-in .NET validation: inline rules in a form, and DataAnnotations or FluentValidation rules that run again on the server before a request is handled.",
        },
        new("js-interop", "JavaScript interop", "Scoped CSS/TypeScript, element refs, IJSRuntime, typed APIs.", "Frontend")
        {
            SearchTitle = "Scoped CSS and TypeScript for C# components",
            Description = "Ship component-scoped CSS and TypeScript with C# components, call the script's exports as typed C# methods, and see how the assets are compiled and cached.",
        },
        new("js-interop-runtime", "JS interop — runtime", "Calling JS, the typed browser-API layer, element refs, third-party libs.", "Frontend")
        {
            SearchTitle = "Calling JavaScript from C# with IJSRuntime",
            Description = "Call JavaScript from C# with an injected IJSRuntime, use typed browser APIs and element refs, and wrap a third-party JavaScript library with TypeScript types.",
        },
        new("spa", "Single-page app front ends", "An npm-built front end built, published and served by its ASP.NET host.", "Frontend")
        {
            SearchTitle = "Host a React or Vue SPA on ASP.NET Core",
            Description = "Serve a React, Vue, Svelte or Angular single-page app from an ASP.NET Core host: dotnet build runs the npm build, publish ships the bundle, one call serves it.",
        },
        new("islands", "Islands",
            "A .tsx or Lit file as an ordinary Rask component, with props owned by C#.", "Frontend")
        {
            SearchTitle = "React, Vue and Svelte components in a C# app",
            Description = "Use React, Vue, Svelte, Lit or Angular components in a C# app, from your own files or straight from npm, with typed props, callbacks and hot reload.",
        },
        new("blazor-components", "Blazor components",
            "A real Blazor component — MudBlazor, an RCL — hosted in a Rask page, server-rendered.", "Frontend")
        {
            SearchTitle = "Host MudBlazor and Radzen Blazor components in C#",
            Description = "Render real Blazor components from Razor Class Libraries, MudBlazor or Radzen inside a C# page: in the first response, with parameters, events and @bind.",
        },
        new("tailwind", "Tailwind CSS", "Tailwind v4 compiled by dotnet build — no npm, no config file.", "Frontend")
        {
            SearchTitle = "Tailwind CSS in .NET without npm or Node.js",
            Description = "Compile Tailwind CSS and daisyUI with dotnet build: no package.json, node_modules or PostCSS. The build scans your C# for classes, with knobs and fixes.",
        },
        new("ui-kit", "UI kit", "The components the framework's own surfaces are drawn with.", "Frontend")
        {
            SearchTitle = "daisyUI components as typed C# components",
            Description = "Every daisyUI 5 component as a typed C# component, no npm or Tailwind config: accessible menus, modals, a sidebar layout, form fields, dark mode.",
        },
        new("data-grid", "Data grid", "Sorting, paging, typed selection, grouping and a card layout on a phone.", "Frontend")
        {
            SearchTitle = "Data grid in C#: sorting, paging and selection",
            Description = "A typed C# data grid with sortable columns, paging, typed row selection, expandable detail rows, grouping, a column chooser and a card layout on phones.",
        },
        new("tree", "Tree", "Expandable hierarchies with a keyboard, typed selection and virtualization.", "Frontend")
        {
            SearchTitle = "Tree view in C#: keyboard and virtualization",
            Description = "A typed C# tree view: expand and collapse, single or multiple selection, a keyboard with type-ahead, and virtualization.",
        },
        new("accessibility", "Accessibility", "ARIA, focus management, the img-alt analyzer.", "Frontend")
        {
            SearchTitle = "Accessible C# components: ARIA, roles and focus",
            Description = "Set ARIA attributes, roles, tab order and language on any element, trap focus in overlays, and catch missing image alt text with a compile-time analyzer.",
        },
        new("localization", "Localization", "Ship in more than one language: negotiated culture, typed catalogs, plurals.", "Frontend")
        {
            SearchTitle = "Localization and translation for .NET web apps",
            Description = "Ship an app in several languages: how the visitor's culture is chosen, translating text with placeholders and plurals, right-to-left layouts and WASM ICU.",
        },

        // ---- Deploy & operate ----
        // No "generate": that command was removed, and a card naming a verb the CLI does not have
        // is the first thing a reader types. The list is the commands `rask --help` prints.
        new("cli", "The rask CLI", "new, dev, db, deploy, info, doctor — the front door.",
            "Deploy & operate")
        {
            SearchTitle = "A .NET CLI to scaffold, run, migrate and deploy apps",
            Description = "Reference for the rask .NET tool: rask new to scaffold projects, rask dev for hot reload, rask db for EF Core migrations and backups, rask deploy over SSH.",
        },
        new("deployment", "Deployment", "rask deploy: a bare VPS to a live HTTPS site, zero downtime.", "Deploy & operate")
        {
            SearchTitle = "Deploy an ASP.NET Core app with Docker and HTTPS",
            Description = "Ship a .NET app to a single VPS with rask deploy: host setup, Docker builds over SSH, automatic HTTPS, GitHub Actions and backups, plus Dockerfiles.",
        },
        new("scaling", "Scaling", "How far one box goes, measured — and where the wall actually is.", "Deploy & operate")
        {
            SearchTitle = "Scaling a single-server .NET app on SQLite",
            Description = "How far one server goes: measured live sessions per GiB, what survives a restart, the single SQLite writer as the real wall, and running more instances.",
        },
        new("secrets", "Secrets", "Where passwords and API keys live, and how they reach the server.", "Deploy & operate")
        {
            SearchTitle = "Managing secrets in a deployed ASP.NET Core app",
            Description = "Keep secrets out of source control: put them in .env.production, deploy with rask deploy --env-file, read them via IConfiguration, and know the limits.",
        },
        new("configuration", "Configuration", "Every setting, under Rask in appsettings.json.", "Deploy & operate")
        {
            SearchTitle = "Configuring a .NET web app from appsettings.json",
            Description = "Configure every Rask package from appsettings.json: the Rask section each one reads, overriding it with environment variables or code, and startup validation.",
        },
        new("dashboard", "Dashboard", "An operator dashboard over every battery's table.", "Deploy & operate")
        {
            SearchTitle = "Operator dashboard for jobs, outbox and logs",
            Description = "The operator dashboard a server app ships with, over your own database: outbox, job and mail queues, dead letters, cache, live logs, backups and SQLite status.",
        },
        new("logging", "Logging", "A durable log store in a database of its own.", "Deploy & operate")
        {
            SearchTitle = "Durable .NET log storage in SQLite",
            Description = "Store application logs in a SQLite file through a standard ILoggerProvider, with batched background writes, retention by age and row count, and scopes.",
        },
        new("observability", "Observability", "Logging, tracing, diagnostics.", "Deploy & operate")
        {
            SearchTitle = ".NET metrics, tracing and health checks",
            Description = "Monitor a .NET web app in production with structured logging, Meter metrics, ActivitySource tracing and health checks, ready to export via OpenTelemetry.",
        },

        // ---- Browser & devices ----
        new("web-apis", "Web APIs from MDN", "Every web API the browser ships, as MDN names it, from C#.", "Browser & devices")
        {
            SearchTitle = "Call any browser Web API from C#, generated from MDN",
            Description = "Every web API the browser ships, generated from MDN into C#: Navigator, Window, Document, localStorage by MDN's names, one round trip per await.",
        },
        new("browser-apis", "Browser APIs", "MDN's browser APIs in C#, and the few typed wrappers beside them.", "Browser & devices")
        {
            SearchTitle = "Browser Web APIs from C#: MDN's surface, typed",
            Description = "Call browser Web APIs from C#: MDN's own surface generated into Rask.Web, plus a few typed wrappers. A map of the whole surface on server and WASM hosts.",
        },
        new("browser-apis-sharing", "Browser APIs — sharing model", "Where wrappers live; declarative vs imperative; subscriptions.", "Browser & devices")
        {
            SearchTitle = "Web Share, gesture triggers and API subscriptions",
            Description = "Which typed browser APIs run on server and WASM hosts, declarative Web Share and gesture triggers on the server, and subscriptions that push updates into C#.",
        },
        new("browser-apis-reference", "Browser APIs — reference & demos", "Every browser API with a runnable live demo.", "Browser & devices")
        {
            SearchTitle = "Browser API reference with live C# demos",
            Description = "Runnable demos for the browser APIs, C# source beside the live result: storage, environment, location, sensors, observers, media, crypto and files.",
        },
        new("pwa", "Mobile & PWA", "Service workers, Web Push, offline, installable apps.", "Browser & devices")
        {
            SearchTitle = "Build a PWA in C#: offline, install and push",
            Description = "Turn a C# WebAssembly app into an installable Progressive Web App: web app manifest, offline service worker, background sync, push notifications, device APIs.",
        },

        // ---- Browser API reference ----
        new("browser-capabilities", "Capability matrix", "Which browser/device API works on which host.", "Browser API reference")
        {
            SearchTitle = "Browser API Support Matrix for C# and .NET",
            Description = "See which browser and device APIs work from C# on the Server host and on WebAssembly, which need a click-gesture component, and which are WASM-only.",
        },
        new("background-sync", "IBackgroundSync", "Typed browser API: IBackgroundSync.", "Browser API reference", "apis/background-sync.md")
        {
            SearchTitle = "Background Sync API in C# and .NET (IBackgroundSync)",
            Description = "Register one-off and periodic Background Sync from C# and get the woken tag in a callback. WASM-only; needs a service worker and runs while a tab is open.",
        },
        new("indexeddb", "IIndexedDb", "Typed browser API: IIndexedDb.", "Browser API reference", "apis/indexeddb.md")
        {
            SearchTitle = "IndexedDB in C# and .NET (IIndexedDb)",
            Description = "Store strings and byte arrays in IndexedDB from C# through an async key/value store. Works on Server and WebAssembly; bytes are kept as a real Uint8Array.",
        },
        new("webauthn", "IWebAuthn", "Typed browser API: IWebAuthn.", "Browser API reference", "apis/webauthn.md")
        {
            SearchTitle = "WebAuthn Passkeys in C# and .NET (IWebAuthn)",
            Description = "Register and sign in users with passkeys from C# through IWebAuthn, a typed Web Authentication API (WebAuthn) wrapper for Server and WebAssembly hosts.",
        },

        // ---- Advanced ----
        new("testing", "Testing", "Unit testing with Rask.Testing, event handlers, E2E.", "Advanced")
        {
            SearchTitle = "Unit testing C# UI components without a browser",
            Description = "Render C# components to HTML in a unit test, with no browser or server. Dispatch click and input handlers, fake IJSRuntime and uploads, and check validation.",
        },
        new("devtools", "DevTools", "A panel in the page: wire, component tree, renders, perf and errors, Debug-only.", "Advanced")
        {
            SearchTitle = "In-page devtools for C# web apps: renders and errors",
            Description = "Inspect a running C# web app from inside the page: wire traffic, the component tree with props, what rendered and why, interaction timings and errors.",
        },
        new("building-form-controls", "Building form controls", "Author your own IFormControl<T>.", "Advanced")
        {
            SearchTitle = "Custom form controls in C# with two-way binding",
            Description = "Build your own C# form control by implementing IFormControl<T>. Get two-way binding, per-field validation and a controlled mode, shown with a full example.",
        },
        new("aot", "AOT compilation", "Ahead-of-time compile for WASM, and trim-safety.", "Advanced")
        {
            SearchTitle = "WebAssembly AOT compilation for .NET apps",
            Description = "Publish a .NET WebAssembly app AOT-compiled with one MSBuild property. Covers mixed mode, custom IParsable registration, JSON source generation and limits.",
        },
        new("prerendering", "Prerendering", "Render a standalone WASM app's pages to HTML at publish.", "Advanced")
        {
            SearchTitle = "Prerender a .NET WebAssembly app to static HTML",
            Description = "Render each route of a .NET WebAssembly app to HTML at publish time, so crawlers see content, not a spinner. Also writes sitemap.xml and robots.txt.",
        },
        new("code-analysis", "Code analysis", "The analyzers, warnings as errors, and the SOLID + Clean Code standard.", "Advanced")
        {
            SearchTitle = ".NET analyzers and warnings-as-errors setup",
            Description = "How Rask builds with .NET analyzers and warnings as errors, the public API gate, and the SOLID and Clean Code standard every change is reviewed against.",
        },
        new("api-style", "Public API style", "How every public name is chosen, and the gate that records the surface.", "Advanced")
        {
            SearchTitle = "Public API naming guidelines for .NET libraries",
            Description = "The naming rules every public API in the framework follows: short nouns, BCL verbs, no Async suffix, an ambient token, no bare bool, and the RS0016/RS0017 gate.",
        },
        new("diagnostics", "Diagnostics", "Every RASK0xx descriptor, its trigger, and the fix.", "Advanced")
        {
            SearchTitle = "Source generator and analyzer diagnostics reference",
            Description = "Every RASK compile-time diagnostic from the source generators and analyzers: what triggers it, its severity, how to fix it, and which ship an IDE quick-fix.",
        },

        // ---- Contributing & internals ----
        new("development-workflow", "Development workflow", "How the repo builds, tests, and ships.", "Contributing & internals")
        {
            SearchTitle = "Contributor workflow: build, test and release gates",
            Description = "How changes to the framework are built, tested and shipped: warnings-as-errors builds, CI gates on every push, MinVer tags and NuGet releases.",
        },
        new("repo-administration", "Repo administration", "Governance, CODEOWNERS, releases, automation.", "Contributing & internals")
        {
            SearchTitle = "GitHub branch protection and repository settings",
            Description = "The GitHub settings behind the repository: branch protection on main, required code owner review, why no status checks are required, and workflow secrets.",
        },
        new("ai-agents", "Building with AI assistants", "llms.txt and the published guides, for an assistant building on Rask.", "Contributing & internals")
        {
            SearchTitle = "Building .NET apps with AI coding assistants",
            Description = "Point an AI coding assistant at llms.txt and the published guides so it scaffolds and extends C# apps following the framework's conventions and diagnostics.",
        },
        new("live-rendering", "Live-rendering internals", "The diff codec and the live-render pipeline.", "Contributing & internals", "architecture/live-rendering.md")
        {
            SearchTitle = "Live rendering over WebSocket and WebAssembly",
            Description = "How one C# component tree stays live after first paint: a shared render and diff pipeline, sent over a WebSocket on ASP.NET Core or JSImport on WebAssembly.",
        },
        new("live-rendering-codec", "Live rendering — walk & codec", "Parallel HTML+frame walk, the edit-op diff codec, keyed reconciliation.", "Contributing & internals", "architecture/live-rendering-codec.md")
        {
            SearchTitle = "DOM diff codec and keyed list reconciliation",
            Description = "How a render emits HTML and a frame stream together, how FrameDiffer turns two renders into minimal edit ops, the JSON wire format, and LIS-based keyed moves.",
        },
        new("live-rendering-runtime", "Live rendering — cache & dispatch", "SessionRenderCache, head/query-nav, handler ordering, slow-connection.", "Contributing & internals", "architecture/live-rendering-runtime.md")
        {
            SearchTitle = "Render cache, event ordering and slow connections",
            Description = "Inside a live session: the two-buffer diff baseline, head and query-only navigations, handler ordering on WebSocket and WASM, and slow-connection indicators.",
        }
    ];

    public static readonly string[] GroupOrder =
        ["Start here", "Tutorial", "Data", "Auth", "Backend services", "Realtime", "Frontend",
         "Deploy & operate", "Browser & devices", "Browser API reference", "Advanced", "Contributing & internals"];

    /// <summary>The catalog entry for a slug, or <c>null</c> when no guide has that name.</summary>
    /// <remarks>
    ///     A guide's head is built from the whole entry — its search title, its description, its group — so
    ///     the page asks for the entry once rather than for each field by slug. An unknown slug is not in the
    ///     sitemap and is not prerendered, so it only ever reaches a visitor who typed it.
    /// </remarks>
    /// <summary>
    ///     Slugs a guide used to answer at, and the slug it answers at now. A crawler and every link elsewhere still
    ///     hold the old URL, so it keeps rendering the guide — with its canonical naming the new one, which is also
    ///     what keeps the old URL out of the sitemap.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Moved { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["broadcast"] = "subscriptions",
    };

    /// <summary>The slug a guide answers at now: <paramref name="slug" /> itself, or where it moved to.</summary>
    public static string Resolve(string slug) => Moved.TryGetValue(slug, out var moved) ? moved : slug;

    public static GuideEntry? Find(string slug) =>
        All.FirstOrDefault(g => string.Equals(g.Slug, slug, StringComparison.Ordinal));

    public static string TitleFor(string slug) => Find(slug)?.Title ?? slug;

    // The doc's path under docs/, for the "edit on GitHub" link. Subfolder guides carry it explicitly
    // (the slug is only the bare leaf); top-level guides default to "{slug}.md".
    public static string SourcePath(string slug)
    {
        return Find(slug)?.Source ?? $"{slug}.md";
    }

    // Reads the verbatim markdown for a guide. Every docs/**/*.md is embedded as raskdoc/{leaf}.md (see
    // the EmbeddedResource glob in Rask.Site.csproj), so the slug — the bare leaf — is the key.
    // Returns null for an unknown slug, so GuidePage can render a not-found state instead of a blank page.
    public static string? ReadMarkdown(string slug)
    {
        var asm = typeof(GuideCatalog).Assembly;
        using var stream = asm.GetManifestResourceStream($"raskdoc/{slug}.md");
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
