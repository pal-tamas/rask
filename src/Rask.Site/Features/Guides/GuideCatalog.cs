using System.Reflection;
using Rask.Ui;

namespace Rask.Site.Features;

// The complete set of repo guides surfaced on-site, in display order and grouped. Each entry's Slug is a
// docs file's bare leaf name (docs/routing.md and docs/apis/geolocation.md -> "routing"/"geolocation"),
// which is exactly what Markdown.RewriteLinks routes an in-doc "*.md" link to (/guides/{leaf}) and what
// the /guides/{slug} route binds. Leaf names are unique across docs/ (guarded by the build). For a guide
// that lives in a subfolder, Source carries the real docs-relative path for the "edit on GitHub" link;
// top-level guides leave it null (defaulting to "{Slug}.md").
//
// Shared by GuidesIndexPage (the cards) and the sidebar's Guides section so the two never drift.
// GuidesTests guards both directions: every slug resolves to an embedded doc, and every embedded
// user-facing doc appears here — so a doc can never be added to the repo yet hidden from the site.
public sealed record GuideEntry(string Slug, string Title, string Blurb, string Group, string? Source = null)
{
    /// <summary>The guide's <c>&lt;title&gt;</c> in a search result, without the site suffix.</summary>
    /// <remarks>
    ///     Separate from <see cref="Title" />, which the sidebar and the prev/next links show and so has to be
    ///     short. A search result is read by someone who has never seen the sidebar: "CQRS" says nothing
    ///     there, "CQRS in .NET with source-generated handlers" says what the page is and matches what they
    ///     typed. <c>required</c>, so a guide added without one does not compile; the length and uniqueness
    ///     rules are asserted by <c>GuideSearchCopyTests</c>.
    /// </remarks>
    public required string SearchTitle { get; init; }

    /// <summary>The guide's <c>&lt;meta name="description"&gt;</c>: 110–160 characters about THIS page.</summary>
    /// <remarks>
    ///     Longer than <see cref="Blurb" />, which is the one-line card text. A description is what a search
    ///     result shows under the title, and the blurb it used to be — "Typed browser API: IBattery." on
    ///     fifty-two pages — told a reader nothing and matched nothing anyone would type.
    /// </remarks>
    public required string Description { get; init; }

    /// <summary>The group's icon.</summary>
    /// <remarks>
    /// Derived rather than stored. Every guide used to name its own, which meant 67 distinct glyphs
    /// across ~80 guides — and the sidebar already groups them, so the icon only ever repeated what the
    /// heading said. One per group is the information that was actually there, and a guide added later
    /// cannot forget to pick one.
    /// </remarks>
    public UiIconName Icon => Group switch
    {
        "Start here" => UiIconName.Rocket,
        "Tutorial" => UiIconName.Book,
        "One Person Framework" => UiIconName.Bolt,
        "Core" => UiIconName.Cube,
        "Integration" => UiIconName.ArrowsRightLeft,
        "Advanced" => UiIconName.Sparkles,
        "Mobile & devices" => UiIconName.Phone,
        "Contributing & internals" => UiIconName.Terminal,
        _ => UiIconName.Document,
    };
}

public static class GuideCatalog
{
    public static readonly GuideEntry[] All =
    [
        // ---- Start here ----
        new("one-person-framework", "The One Person Framework",
            "The doctrine: one dev, one codebase, one server, a whole product.", "Start here")
        {
            SearchTitle = "The .NET One Person Framework: one app, one server",
            Description = "Why one developer can build, run and ship a whole product from one C# codebase on one server: SQLite-first data, built-in batteries and a one-command deploy.",
        },
        new("installation", "Installing Rask", "One line to the CLI and everything it needs — options, upgrade, uninstall.", "Start here")
        {
            SearchTitle = "Install the CLI and .NET 10 SDK in one command",
            Description = "Install the rask CLI with one curl or PowerShell command. It adds the .NET 10 SDK, dotnet-ef, wasm-tools and Node.js LTS under your home directory, no sudo.",
        },
        new("getting-started", "Getting started", "Scaffold a project and build your first component.", "Start here")
        {
            SearchTitle = "Getting started: C# web UI components in .NET",
            Description = "Scaffold and run your first app, then write a C# component, handle events and add a route. Components render on the server over WebSocket or in WebAssembly.",
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
        new("cheatsheet", "Cheat sheet", "Every CLI command, feature flag, and wiring one-liner on one page.", "Start here")
        {
            SearchTitle = "Cheat sheet: CLI commands and .NET wiring lines",
            Description = "One dense page of every rask CLI command, a CRUD slice in one place, and the Program.cs wiring for CQRS, EF Core data, jobs, mail, cache, outbox and SQLite.",
        },
        new("recipes", "Recipes", "Task-first: how do I add a feature, gate a page, run a job, deploy?", "Start here")
        {
            SearchTitle = "Recipes: common tasks in a .NET web app",
            Description = "Task-first answers for an existing app: add a CRUD feature, relate entities, require login, run background work, send email, cache queries, deploy and test.",
        },
        new("roadmap", "Roadmap", "The One Person Framework pillars — shipped and planned.", "Start here")
        {
            SearchTitle = "Roadmap: shipped and planned framework pillars",
            Description = "What is shipped and what is next for the .NET One Person Framework: UI hosts, CLI, CQRS, data, jobs, mail, cache, outbox and SQLite, plus known gaps.",
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
        new("02-first-feature", "Ch 2 · First feature", "Generate a CRUD feature and wire the database.", "Tutorial", "tutorial/02-first-feature.md")
        {
            SearchTitle = "Tutorial 2: CRUD with EF Core and SQLite in C#",
            Description = "Build a database-backed Products catalog as a vertical slice: entity, form model, EF Core mapping, CQRS commands and pages, then migrate with rask db.",
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
            Description = "Cache the product list in a typed, SQLite-backed cache: write an accessor around GetOrAddAsync, read through it, and invalidate it when the catalog changes.",
        },
        new("07-outbox-events", "Ch 7 · Outbox & events", "Domain events with the transactional outbox.", "Tutorial", "tutorial/07-outbox-events.md")
        {
            SearchTitle = "Tutorial 7: domain events and a transactional outbox",
            Description = "Raise domain events when an order is placed and deliver them through a transactional outbox, so handlers still run after a crash. Written in C# with EF Core.",
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

        // ---- One Person Framework (the batteries) ----
        // No "generate": that command was removed, and a card naming a verb the CLI does not have
        // is the first thing a reader types. The list is the commands `rask --help` prints.
        new("cli", "The rask CLI", "Scaffold, run, dev, db, deploy — the front door.",
            "One Person Framework")
        {
            SearchTitle = "A .NET CLI to scaffold, run, migrate and deploy apps",
            Description = "Reference for the rask .NET tool: rask new to scaffold projects, rask dev for hot reload, rask db for EF Core migrations and backups, rask deploy over SSH.",
        },
        new("data", "Rask.Data", "Base entity + EF Core interceptors: audit, soft-delete, domain events.", "One Person Framework")
        {
            SearchTitle = "EF Core models without writing a DbContext in C#",
            Description = "Declare EF Core models and query them directly, with no DbContext or DbSet to write: units of work, value objects, strongly-typed ids and bulk insert.",
        },
        new("cqrs", "CQRS", "Source-generated queries, commands, notifications, behaviors.", "One Person Framework")
        {
            SearchTitle = "CQRS in .NET with source-generated handlers",
            Description = "A source-generated CQRS mediator for .NET: dispatch queries, commands and notifications via IDispatcher with no reflection, plus pipeline behaviors.",
        },
        new("api-endpoints", "HTTP APIs", "Controllers and minimal APIs, called through a generated typed client.", "One Person Framework")
        {
            SearchTitle = "ASP.NET Core APIs with a generated typed client",
            Description = "Host ASP.NET Core controllers and minimal APIs, and get a typed HTTP client generated from them, with validation from [Required] and AbstractValidator rules.",
        },
        new("spa", "TypeScript front ends", "React, Vue, Angular and four more, typed from your C# contracts.", "One Person Framework")
        {
            SearchTitle = "TypeScript SPA with an ASP.NET Core backend",
            Description = "Host a React, Vue, Svelte, Solid, Preact, Lit or Angular TypeScript app on ASP.NET Core, with types generated from your C# message records on each build.",
        },
        new("meta", "Meta framework front ends",
            "Nuxt, Next, SvelteKit and three more owning the whole front end — one container.", "One Person Framework")
        {
            SearchTitle = "Nuxt, Next.js or SvelteKit with a .NET backend",
            Description = "Run Nuxt, Next.js, SvelteKit, SolidStart, TanStack Start or Analog on a C# backend, shipped as one container on one port, calling into C# with shared sign-in.",
        },
        new("islands", "Islands",
            "A .tsx or Lit file as an ordinary Rask component, with props owned by C#.", "One Person Framework")
        {
            SearchTitle = "React, Vue and Svelte components in a C# app",
            Description = "Use a React, Preact, Solid, Vue, Svelte, Angular or Lit file as an ordinary C# component: props declared in C#, callbacks into C#, hydration and hot reload.",
        },
        new("blazor-components", "Blazor components",
            "A real Blazor component — MudBlazor, an RCL — hosted in a Rask page, server-rendered.", "One Person Framework")
        {
            SearchTitle = "Host MudBlazor and Radzen Blazor components in C#",
            Description = "Render real Blazor components from Razor Class Libraries, MudBlazor or Radzen inside a C# page: in the first response, with parameters, events and @bind.",
        },
        new("tailwind", "Tailwind CSS", "Tailwind v4 compiled by dotnet build — no npm, no config file.", "One Person Framework")
        {
            SearchTitle = "Tailwind CSS in .NET without npm or Node.js",
            Description = "Compile Tailwind CSS and daisyUI with dotnet build: no package.json, node_modules or PostCSS. The build scans your C# for classes, with knobs and fixes.",
        },
        new("query", "Rask.Query", "The dispatcher wrapped in a cache: dedup, staleness, invalidation.", "One Person Framework")
        {
            SearchTitle = "TanStack Query-style data caching for C#",
            Description = "Wrap the CQRS dispatcher in a TanStack Query-style cache for C# components: request dedup, staleness, background refetch, keys, invalidation and mutations.",
        },
        new("jobs", "Background jobs", "Durable enqueued / delayed / recurring work on your database.", "One Person Framework")
        {
            SearchTitle = "Durable background jobs in .NET on your database",
            Description = "Run .NET background jobs off the request thread, stored in your app's database with no Redis or broker: enqueued, delayed and recurring, retried with backoff.",
        },
        new("mail", "Transactional email", "Durable email queued on your database, delivered over SMTP.", "One Person Framework")
        {
            SearchTitle = "Queued transactional email in .NET over SMTP",
            Description = "Send transactional email from .NET off the request thread: messages queue in your database, bodies are C# components, and a worker delivers over SMTP.",
        },
        new("cache", "Cache", "A database-backed IDistributedCache plus a typed ICache.", "One Person Framework")
        {
            SearchTitle = "Database-backed IDistributedCache for ASP.NET Core",
            Description = "A cache stored in your app's own database instead of Redis: it implements IDistributedCache and adds a typed ICache with GetOrAddAsync and sliding expiry.",
        },
        new("outbox", "Outbox", "Crash-safe domain-event delivery on your database.",
            "One Person Framework")
        {
            SearchTitle = "Transactional outbox pattern in .NET, no broker",
            Description = "Deliver domain events crash-safely with a transactional outbox on your app's database: events commit with the data and relay at-least-once, with no broker.",
        },
        new("sqlite", "Production SQLite", "WAL + busy-timeout pragmas, continuous backup, snapshots.", "One Person Framework")
        {
            SearchTitle = "SQLite in production for .NET: WAL and pragmas",
            Description = "Run SQLite as a production database with ADO.NET or EF Core: WAL and busy_timeout pragmas, BEGIN IMMEDIATE retries, STRICT tables and Litestream backup.",
        },
        new("deployment", "Deployment", "rask deploy: a bare VPS to a live HTTPS site, zero downtime.", "One Person Framework")
        {
            SearchTitle = "Deploy an ASP.NET Core app with Docker and HTTPS",
            Description = "Ship a .NET app to a single VPS with rask deploy: host setup, Docker builds over SSH, automatic HTTPS, GitHub Actions and backups, plus Dockerfiles.",
        },
        new("scaling", "Scaling", "How far one box goes, measured — and where the wall actually is.", "One Person Framework")
        {
            SearchTitle = "Scaling a single-server .NET app on SQLite",
            Description = "How far one server goes: measured live sessions per GiB, what survives a restart, the single SQLite writer as the real wall, and running more instances.",
        },
        new("secrets", "Secrets", "Where passwords and API keys live, and how they reach the server.", "One Person Framework")
        {
            SearchTitle = "Managing secrets in a deployed ASP.NET Core app",
            Description = "Keep secrets out of source control: put them in .env.production, deploy with rask deploy --env-file, read them via IConfiguration, and know the limits.",
        },

        // ---- Core ----
        new("building-components", "Building components",
            "Naming a component and chaining onto it; what a component demands before it exists.", "Core")
        {
            SearchTitle = "Building C# components with the markup chain",
            Description = "Learn how C# markup chains work: required steps come first, bound versus controlled form controls, callbacks, your own components, and lists of components.",
        },
        new("elements", "Elements & the DSL", "Primitives, tag factories, universal props, SVG, the element catalog.", "Core")
        {
            SearchTitle = "HTML and SVG elements as typed C# components",
            Description = "Reference for HTML in C#: Text, Raw and Doctype primitives, a typed entry for every HTML and SVG element, universal attributes and the children indexer.",
        },
        new("routing", "Routing", "Route attributes, params, nested layouts, type-safe URLs.", "Core")
        {
            SearchTitle = "Routing and type-safe URLs in C#",
            Description = "Declare routes with [Route] and navigate with source-generated, type-safe URLs. Covers route and query parameters, nested layouts, Navigator and RouteState.",
        },
        new("composition", "Composition", "Children, fragments, callbacks, context, virtualize.", "Core")
        {
            SearchTitle = "Component composition: children and fragments",
            Description = "How C# components compose: children and fragments through the indexer, static vs stateless vs stateful components, and hosting components built at runtime.",
        },
        new("composition-callbacks-context", "Composition — callbacks & context", "Child→parent callbacks and provide/consume context.", "Core")
        {
            SearchTitle = "Child-to-parent callbacks and context in C#",
            Description = "Send events from a child component to its parent with Callback properties that re-render the owner, and pass values down with context, not prop drilling.",
        },
        new("composition-lists", "Composition — lists & more", "Virtualize, keyed lists, toasts, drag-and-drop, error boundaries.", "Core")
        {
            SearchTitle = "Virtualized lists, toasts and drag and drop",
            Description = "Render windowed lists with Virtualize, keep list identity with keys, and add toast messages, drag-and-drop and error boundaries to C# web components.",
        },
        new("lifecycle", "Lifecycle", "Mount, props-changed, rendered, unmount, cancellation.", "Core")
        {
            SearchTitle = "Component lifecycle hooks in C#",
            Description = "The lifecycle hooks a component can override, their order and sync vs async rules, plus disposal, cancellation tied to component lifetime and hosted services.",
        },
        new("render-modes", "Live pages", "Every page live: waiting for async data before the first byte, status codes, redirects.", "Core")
        {
            SearchTitle = "Live server-rendered pages without hydration in C#",
            Description = "How a server page reaches the browser: server-rendered HTML with no hydration, a live session per page, waiting for async data, status codes and redirects.",
        },
        new("forms", "Forms & validation", "Two-way binding, Form<T>, inline/DataAnnotations/Fluent.", "Core")
        {
            SearchTitle = "Forms and two-way data binding in C#",
            Description = "Bind inputs two-way with typed Bind expressions, build forms on an EditContext, track touched and modified fields, and show accessible validation messages.",
        },
        new("forms-validation", "Forms — validation", "Inline, DataAnnotations, FluentValidation, and async validators.", "Core")
        {
            SearchTitle = "Form validation with DataAnnotations in C#",
            Description = "Validate form input with inline rules, DataAnnotations and FluentValidation, including async validators, a validating indicator and first-error-wins.",
        },
        new("forms-advanced", "Forms — advanced", "Nested/complex models, radio & checkbox groups, custom controls.", "Core")
        {
            SearchTitle = "Nested form models and custom form controls",
            Description = "Bind and validate nested models and collections, build radio and checkbox groups, keep form state across a redeploy, and write your own form controls in C#.",
        },
        new("validation", "Validation", "Built in and on: attributes and AbstractValidator<T>, in forms and on requests.", "Core")
        {
            SearchTitle = "Model validation for forms and HTTP requests",
            Description = "Built-in .NET validation: DataAnnotations or FluentValidation rules run in the form as the user types, and again on the server before a request is handled.",
        },
        new("js-interop", "JavaScript interop", "Scoped CSS/TypeScript, element refs, IJSRuntime, typed APIs.", "Core")
        {
            SearchTitle = "Scoped CSS and TypeScript for C# components",
            Description = "Ship component-scoped CSS and TypeScript with C# components: how scripts are compiled, how assets are delivered and cached, and no style flash on navigation.",
        },
        new("js-interop-runtime", "JS interop — runtime", "Calling JS, the typed browser-API layer, element refs, third-party libs.", "Core")
        {
            SearchTitle = "Calling JavaScript from C# with IJSRuntime",
            Description = "Call JavaScript from C# with an injected IJSRuntime, use typed browser APIs and element refs, and wrap a third-party JavaScript library with TypeScript types.",
        },


        // ---- Integration ----
        new("authentication", "Authentication", "Cookie sessions and OIDC on Server and WASM, route guards.", "Integration")
        {
            SearchTitle = "Authentication with ASP.NET Core Identity",
            Description = "Built-in sign-in, registration and sign-out on ASP.NET Core Identity: first account as admin, email confirmation, password reset, gating and bearer tokens.",
        },
        new("authentication-cookie", "Auth — cookie", "Cookie login and session on Server and on a WASM SPA with an API host.", "Integration")
        {
            SearchTitle = "Cookie authentication for server and WASM apps",
            Description = "Wire cookie-based login and sessions by hand, for the server-rendered WebSocket host and for a WebAssembly SPA backed by your own ASP.NET Core API.",
        },
        new("authentication-providers", "Auth — providers", "Identity, Keycloak, Auth0, and other OIDC providers.", "Integration")
        {
            SearchTitle = "OpenID Connect and external identity providers",
            Description = "Sign in through Keycloak, Auth0, AWS Cognito or Duende IdentityServer over OpenID Connect, or bring your own user store with ASP.NET Identity.",
        },
        new("authentication-hardening", "Auth — hardening", "Production hardening for cookies, tokens, and sessions.", "Integration")
        {
            SearchTitle = "Production security hardening for .NET web apps",
            Description = "Harden authentication for production: session trust model, CSRF protection, reverse proxy forwarded headers, Content-Security-Policy and a security checklist.",
        },
        new("http-and-files", "HTTP & files", "Fetch JSON with a DI'd HttpClient; upload and download files.", "Integration")
        {
            SearchTitle = "HttpClient, file uploads and downloads in C#",
            Description = "Fetch JSON with a dependency-injected HttpClient, accept uploads through a typed file picker, and send downloads to the browser, on server and WASM hosts.",
        },
        new("data-access", "Data access", "EF Core + SQLite, vertical slices, DDD patterns.", "Integration")
        {
            SearchTitle = "Data access with EF Core and SQLite",
            Description = "Wire EF Core and SQLite into a server app: register the DbContext with a factory for long-lived sessions, load data in the lifecycle, seed and test it.",
        },
        new("accessibility", "Accessibility", "ARIA, focus management, the img-alt analyzer.", "Integration")
        {
            SearchTitle = "Accessible C# components: ARIA, roles and focus",
            Description = "Set ARIA attributes, roles, tab order and language on any element, trap focus in overlays, and catch missing image alt text with a compile-time analyzer.",
        },
        new("localization", "Localization", "Ship in more than one language: negotiated culture, typed catalogs, plurals.", "Integration")
        {
            SearchTitle = "Localization and translation for .NET web apps",
            Description = "Ship an app in several languages: how the visitor's culture is chosen, translating text with placeholders and plurals, right-to-left layouts and WASM ICU.",
        },
        new("dashboard", "Dashboard", "An operator dashboard over every battery's table.", "Integration")
        {
            SearchTitle = "Operator dashboard for jobs, outbox and logs",
            Description = "Mount an operator dashboard over your own database: outbox, background job and mail queues, dead letters, cache, live logs, backups and SQLite status.",
        },
        new("ui-kit", "UI kit", "The components the framework's own surfaces are drawn with.", "Integration")
        {
            SearchTitle = "daisyUI components as typed C# components",
            Description = "Use every daisyUI 5 component as a typed C# component with no npm install or Tailwind config: wiring the kit, themes, form controls and who owns state.",
        },
        new("data-grid", "Data grid", "Sorting, paging, typed selection, grouping and a card layout on a phone.", "Integration")
        {
            SearchTitle = "Data grid in C#: sorting, paging and selection",
            Description = "A typed C# data grid with sortable columns, paging, typed row selection, expandable detail rows, grouping, a column chooser and a card layout on phones.",
        },
        new("logging", "Logging", "A durable log store in a database of its own.", "Integration")
        {
            SearchTitle = "Durable .NET log storage in SQLite",
            Description = "Store application logs in a SQLite file through a standard ILoggerProvider, with batched background writes, retention by age and row count, and scopes.",
        },
        new("observability", "Observability", "Logging, tracing, diagnostics.", "Integration")
        {
            SearchTitle = ".NET metrics, tracing and health checks",
            Description = "Monitor a .NET web app in production with structured logging, Meter metrics, ActivitySource tracing and health checks, ready to export via OpenTelemetry.",
        },
        new("configuration", "Configuration", "App configuration and settings.", "Integration")
        {
            SearchTitle = "Configuring runtime and WebSocket server options",
            Description = "Configure shared runtime and server-only options in code or appsettings.json: WebSocket limits, session grace periods, reconnect, uploads and MaxSessions.",
        },

        // ---- Mobile & devices ----
        new("browser-apis", "Browser APIs", "The typed wrappers over the platform's browser APIs.", "Mobile & devices")
        {
            SearchTitle = "Typed C# wrappers for browser Web APIs",
            Description = "Call browser Web APIs from C# through typed, injectable wrappers instead of raw IJSRuntime calls. A map of the whole surface on server and WASM hosts.",
        },
        new("browser-apis-sharing", "Browser APIs — sharing model", "Where wrappers live; declarative vs imperative; subscriptions.", "Mobile & devices")
        {
            SearchTitle = "Web Share, gesture triggers and API subscriptions",
            Description = "Which typed browser APIs run on server and WASM hosts, declarative Web Share and gesture triggers on the server, and subscriptions that push updates into C#.",
        },
        new("browser-apis-reference", "Browser APIs — reference & demos", "Every typed browser wrapper with a runnable live demo.", "Mobile & devices")
        {
            SearchTitle = "Browser API reference with live C# demos",
            Description = "Runnable demos for every typed browser API wrapper, C# source beside the result: storage, environment, location, sensors, observers, media, crypto and files.",
        },
        new("pwa", "Mobile & PWA", "Service workers, Web Push, offline, installable apps.", "Mobile & devices")
        {
            SearchTitle = "Build a PWA in C#: offline, install and push",
            Description = "Turn a C# WebAssembly app into an installable Progressive Web App: web app manifest, offline service worker, background sync, push notifications, device APIs.",
        },
        new("webpush", "Web Push (server)", "Send Web Push from your backend — VAPID keys, IWebPush, delivery results.", "Mobile & devices")
        {
            SearchTitle = "Send Web Push notifications from .NET",
            Description = "Send Web Push notifications from an ASP.NET Core backend to subscribed browsers with your own VAPID keys, aes128gcm encryption and zero external dependencies.",
        },

        // ---- Browser API reference ----
        new("browser-capabilities", "Capability matrix", "Which browser/device API works on which host.", "Browser API reference")
        {
            SearchTitle = "Browser API Support Matrix for C# and .NET",
            Description = "See which typed C# browser and device API wrappers work on the Server host and on WebAssembly, which need a click-gesture component, and which are WASM-only.",
        },
        new("background-sync", "IBackgroundSync", "Typed browser API: IBackgroundSync.", "Browser API reference", "apis/background-sync.md")
        {
            SearchTitle = "Background Sync API in C# and .NET (IBackgroundSync)",
            Description = "Register one-off and periodic Background Sync from C# and get the woken tag in a callback. WASM-only; needs a service worker and runs while a tab is open.",
        },
        new("badge", "IBadge", "Typed browser API: IBadge.", "Browser API reference", "apis/badge.md")
        {
            SearchTitle = "Badging API in C# and .NET (IBadge)",
            Description = "Set or clear the count on an installed PWA's app icon from C# with the Badging API wrapper. Works on Server and WebAssembly; rendering varies by platform.",
        },
        new("battery", "IBattery", "Typed browser API: IBattery.", "Browser API reference", "apis/battery.md")
        {
            SearchTitle = "Battery Status API in C# and .NET (IBattery)",
            Description = "Read the Battery Status API from C#: level, charging state and charge times, or watch for changes in a callback. Chromium-only; null where unsupported.",
        },
        new("bluetooth", "IBluetooth", "Typed browser API: IBluetooth.", "Browser API reference", "apis/bluetooth.md")
        {
            SearchTitle = "Web Bluetooth API in C# and .NET (IBluetooth)",
            Description = "Pair a Bluetooth LE device from C# and read, write or watch GATT characteristics with the Web Bluetooth API. WebAssembly host only, behind the device chooser.",
        },
        new("broadcast-channel", "IBroadcastChannel", "Typed browser API: IBroadcastChannel.", "Browser API reference", "apis/broadcast-channel.md")
        {
            SearchTitle = "Broadcast Channel API in C# (IBroadcastChannel)",
            Description = "Send messages between browser tabs from C# with the Broadcast Channel API wrapper, and receive them in a callback. Works on Server and WebAssembly.",
        },
        new("clipboard", "IClipboard", "Typed browser API: IClipboard.", "Browser API reference", "apis/clipboard.md")
        {
            SearchTitle = "Clipboard API in C# and .NET (IClipboard)",
            Description = "Copy text to and read text from the system clipboard in C# with the Async Clipboard API. Works on Server and WebAssembly; reads need a gesture or grant.",
        },
        new("cookies", "ICookies", "Typed browser API: ICookies.", "Browser API reference", "apis/cookies.md")
        {
            SearchTitle = "document.cookie in C# and .NET (ICookies)",
            Description = "Read and write browser cookies from C# through document.cookie, with typed CookieOptions. The ICookies wrapper works on both the Server and WebAssembly hosts.",
        },
        new("crypto", "ICrypto", "Typed browser API: ICrypto.", "Browser API reference", "apis/crypto.md")
        {
            SearchTitle = "Web Crypto API in C# and .NET (ICrypto)",
            Description = "Generate random UUIDs and bytes and compute SHA digests from C# using the browser's Web Crypto API. The ICrypto wrapper works on Server and WebAssembly hosts.",
        },
        new("device-motion", "IDeviceMotion", "Typed browser API: IDeviceMotion.", "Browser API reference", "apis/device-motion.md")
        {
            SearchTitle = "DeviceMotion Events in C# and .NET (IDeviceMotion)",
            Description = "Receive accelerometer and gyroscope readings from device motion events in a C# callback. Request permission first: iOS gates and often blocks the sensor.",
        },
        new("device-orientation", "IDeviceOrientation", "Typed browser API: IDeviceOrientation.", "Browser API reference", "apis/device-orientation.md")
        {
            SearchTitle = "DeviceOrientation Events in C# (IDeviceOrientation)",
            Description = "Receive alpha, beta and gamma tilt from device orientation events in a C# callback. Works on Server and WebAssembly; iOS needs a gesture-triggered grant.",
        },
        new("eye-dropper", "IEyeDropper", "Typed browser API: IEyeDropper.", "Browser API reference", "apis/eye-dropper.md")
        {
            SearchTitle = "EyeDropper API in C# and .NET (IEyeDropper)",
            Description = "Pick a colour from anywhere on screen in C# with the EyeDropper API. IEyeDropper is WASM-only; on Server, EyeDropperTrigger posts the colour to OnColor.",
        },
        new("file-system-access", "IFileSystemAccess", "Typed browser API: IFileSystemAccess.", "Browser API reference", "apis/file-system-access.md")
        {
            SearchTitle = "File System Access API in C# (IFileSystemAccess)",
            Description = "Open a file, save it back to disk and read directories from C# with the File System Access API wrapper. Works on Server and WebAssembly in Chromium browsers.",
        },
        new("fullscreen", "IFullscreen", "Typed browser API: IFullscreen.", "Browser API reference", "apis/fullscreen.md")
        {
            SearchTitle = "Fullscreen API in C# and .NET (IFullscreen)",
            Description = "Present an element or the whole page fullscreen from C# with the Fullscreen API. IFullscreen is WASM-only; on Server, FullscreenTrigger runs it in a click.",
        },
        new("gamepad", "IGamepad", "Typed browser API: IGamepad.", "Browser API reference", "apis/gamepad.md")
        {
            SearchTitle = "Gamepad API in C# and .NET (IGamepad)",
            Description = "Read connected game controllers and their state in C# with the Gamepad API, with readings pushed to a callback. Works on Server; prefer WASM for twitch input.",
        },
        new("geolocation", "IGeolocation", "Typed browser API: IGeolocation.", "Browser API reference", "apis/geolocation.md")
        {
            SearchTitle = "Geolocation API in C# and .NET (IGeolocation)",
            Description = "Get the user's position once or watch a live stream of fixes from C# with the Geolocation API wrapper. Needs a secure context and the location permission.",
        },
        new("hid", "IHid", "Typed browser API: IHid.", "Browser API reference", "apis/hid.md")
        {
            SearchTitle = "WebHID API in C# and .NET (IHid)",
            Description = "Talk to HID devices from C# with the WebHID API, with input reports pushed to a callback. Available on the WebAssembly host only, behind a device chooser.",
        },
        new("idle-detector", "IIdleDetector", "Typed browser API: IIdleDetector.", "Browser API reference", "apis/idle-detector.md")
        {
            SearchTitle = "Idle Detection API in C# and .NET (IIdleDetector)",
            Description = "Detect when the user goes idle or locks the screen in C# with the Idle Detection API, with changes pushed to a callback. WASM-only, behind a permission.",
        },
        new("indexeddb", "IIndexedDb", "Typed browser API: IIndexedDb.", "Browser API reference", "apis/indexeddb.md")
        {
            SearchTitle = "IndexedDB in C# and .NET (IIndexedDb)",
            Description = "Store strings and byte arrays in IndexedDB from C# through an async key/value store. Works on Server and WebAssembly; bytes are kept as a real Uint8Array.",
        },
        new("install-prompt", "IInstallPrompt", "Typed browser API: IInstallPrompt.", "Browser API reference", "apis/install-prompt.md")
        {
            SearchTitle = "PWA Install Prompt (beforeinstallprompt) in C#",
            Description = "Capture the beforeinstallprompt event and replay the PWA install prompt from C#. IInstallPrompt is WASM-only; on Server, InstallTrigger reports the outcome.",
        },
        new("intersection-observer", "IIntersectionObserver", "Typed browser API: IIntersectionObserver.", "Browser API reference", "apis/intersection-observer.md")
        {
            SearchTitle = "Intersection Observer API in C# and .NET",
            Description = "Get notified in C# when an element enters or leaves the viewport with the Intersection Observer API. It observes the live document on Server and WebAssembly.",
        },
        new("media-devices", "IMediaDevices", "Typed browser API: IMediaDevices.", "Browser API reference", "apis/media-devices.md")
        {
            SearchTitle = "getUserMedia Camera Capture in C# (IMediaDevices)",
            Description = "Capture camera, microphone or screen into a video element from C# with getUserMedia. IMediaDevices is WASM-only; on Server, use MediaCaptureTrigger instead.",
        },
        new("media-query", "IMediaQuery", "Typed browser API: IMediaQuery.", "Browser API reference", "apis/media-query.md")
        {
            SearchTitle = "matchMedia Media Queries in C# (IMediaQuery)",
            Description = "Evaluate media queries such as dark mode or reduced motion from C# with window.matchMedia. IMediaQuery works on both the Server and WebAssembly hosts.",
        },
        new("media-session", "IMediaSession", "Typed browser API: IMediaSession.", "Browser API reference", "apis/media-session.md")
        {
            SearchTitle = "Media Session API in C# and .NET (IMediaSession)",
            Description = "Set now-playing metadata and handle hardware media-key actions from C# with the Media Session API. Actions reach a callback on Server and WebAssembly.",
        },
        new("media-streams", "IMediaStreams", "Typed browser API: IMediaStreams.", "Browser API reference", "apis/media-streams.md")
        {
            SearchTitle = "MediaStream in C#: Attach and Stop (IMediaStreams)",
            Description = "Attach a live MediaStream to a video element or stop its tracks from C#, whether from capture or a WebRTC peer. Works on every host without a gesture.",
        },
        new("mutation-observer", "IMutationObserver", "Typed browser API: IMutationObserver.", "Browser API reference", "apis/mutation-observer.md")
        {
            SearchTitle = "MutationObserver in C# and .NET (IMutationObserver)",
            Description = "Get notified in C# when an element's children, attributes or text change with MutationObserver. It observes the live document on Server and WebAssembly.",
        },
        new("navigator-info", "INavigatorInfo", "Typed browser API: INavigatorInfo.", "Browser API reference", "apis/navigator-info.md")
        {
            SearchTitle = "Navigator API in C# and .NET (INavigatorInfo)",
            Description = "Read navigator.onLine, language and userAgent from C# with INavigatorInfo, a one-shot Navigator API wrapper that works on the Server host and on WebAssembly.",
        },
        new("network-info", "INetworkInfo", "Typed browser API: INetworkInfo.", "Browser API reference", "apis/network-info.md")
        {
            SearchTitle = "Network Information API in C# (INetworkInfo)",
            Description = "Read the effective connection type, downlink, RTT and Data-Saver flag from C# with INetworkInfo. The Network Information API is Chromium-only; feature-detect.",
        },
        new("notifications", "INotifications", "Typed browser API: INotifications.", "Browser API reference", "apis/notifications.md")
        {
            SearchTitle = "Notifications API in C# and .NET (INotifications)",
            Description = "Show local browser notifications from C# with INotifications. Request permission, replace a notification by tag, and handle a denied Notifications API prompt.",
        },
        new("origin-private-file-system", "IOriginPrivateFileSystem", "Typed browser API: IOriginPrivateFileSystem.", "Browser API reference", "apis/origin-private-file-system.md")
        {
            SearchTitle = "Origin Private File System (OPFS) in C# and .NET",
            Description = "Read and write files in the origin private file system from C# by path and byte range, with no picker or user gesture. A fit for a local SQLite database file.",
        },
        new("page-visibility", "IPageVisibility", "Typed browser API: IPageVisibility.", "Browser API reference", "apis/page-visibility.md")
        {
            SearchTitle = "Page Visibility API in C# (IPageVisibility)",
            Description = "Check whether the page is visible or hidden from C# with IPageVisibility, a Page Visibility API wrapper with a change subscription for Server and WebAssembly.",
        },
        new("performance", "IPerformance", "Typed browser API: IPerformance.", "Browser API reference", "apis/performance.md")
        {
            SearchTitle = "Performance API in C# and .NET (IPerformance)",
            Description = "Read the browser's high-resolution clock and navigation timing from C# with IPerformance, a typed Performance API wrapper for Server and WebAssembly hosts.",
        },
        new("permissions", "IPermissions", "Typed browser API: IPermissions.", "Browser API reference", "apis/permissions.md")
        {
            SearchTitle = "Permissions API in C# and .NET (IPermissions)",
            Description = "Query a permission's state (granted, denied or prompt) from C# with IPermissions before prompting. Permissions API names vary by engine, Safari included.",
        },
        new("picture-in-picture", "IPictureInPicture", "Typed browser API: IPictureInPicture.", "Browser API reference", "apis/picture-in-picture.md")
        {
            SearchTitle = "Picture-in-Picture API in C# (IPictureInPicture)",
            Description = "Float a video element into a Picture-in-Picture mini-player from C#: IPictureInPicture on WebAssembly, or the PictureInPictureTrigger component on Server.",
        },
        new("resize-observer", "IResizeObserver", "Typed browser API: IResizeObserver.", "Browser API reference", "apis/resize-observer.md")
        {
            SearchTitle = "ResizeObserver in C# and .NET (IResizeObserver)",
            Description = "Get notified in C# when an element's size changes with IResizeObserver, a ResizeObserver wrapper that pushes each change to a callback on every host.",
        },
        new("screen-info", "IScreenInfo", "Typed browser API: IScreenInfo.", "Browser API reference", "apis/screen-info.md")
        {
            SearchTitle = "Screen API in C# and .NET (IScreenInfo)",
            Description = "Read display size, color depth and device pixel ratio from C# with IScreenInfo, a typed wrapper over the Screen API and devicePixelRatio for every host.",
        },
        new("screen-orientation", "IScreenOrientation", "Typed browser API: IScreenOrientation.", "Browser API reference", "apis/screen-orientation.md")
        {
            SearchTitle = "Screen Orientation API in C# (IScreenOrientation)",
            Description = "Read and lock screen orientation from C#: IScreenOrientation on WebAssembly, ScreenOrientationTrigger on Server. The Screen Orientation lock needs fullscreen.",
        },
        new("serial", "ISerial", "Typed browser API: ISerial.", "Browser API reference", "apis/serial.md")
        {
            SearchTitle = "Web Serial API in C# and .NET (ISerial)",
            Description = "Talk to a serial device such as an Arduino or GPS from C# with ISerial, a Web Serial API wrapper that pushes incoming data to a callback. WebAssembly only.",
        },
        new("signaling", "ISignaling", "Typed browser API: ISignaling.", "Browser API reference", "apis/signaling.md")
        {
            SearchTitle = "WebRTC Signaling Server in C# and .NET (ISignaling)",
            Description = "Relay WebRTC offers, answers and ICE candidates between browsers with ISignaling and a WebSocket relay on any ASP.NET host. Authentication is on by default.",
        },
        new("share", "IShare", "Typed browser API: IShare.", "Browser API reference", "apis/share.md")
        {
            SearchTitle = "Web Share API in C# and .NET (IShare)",
            Description = "Open the OS share sheet with text or a URL from C# via the Web Share API: IShare on WebAssembly, or the Shareable component inside a click on Server.",
        },
        new("speech-recognition", "ISpeechRecognition", "Typed browser API: ISpeechRecognition.", "Browser API reference", "apis/speech-recognition.md")
        {
            SearchTitle = "Speech Recognition API in C# (ISpeechRecognition)",
            Description = "Turn speech into text from C# with ISpeechRecognition, which pushes final or interim transcripts to a callback. SpeechRecognition is Chromium-only.",
        },
        new("speech-synthesis", "ISpeechSynthesis", "Typed browser API: ISpeechSynthesis.", "Browser API reference", "apis/speech-synthesis.md")
        {
            SearchTitle = "Speech Synthesis API in C# (ISpeechSynthesis)",
            Description = "Speak text aloud and cancel the speech queue from C# with ISpeechSynthesis, a SpeechSynthesis API wrapper that uses the browser's own voices on every host.",
        },
        new("storage-estimator", "IStorageEstimator", "Typed browser API: IStorageEstimator.", "Browser API reference", "apis/storage-estimator.md")
        {
            SearchTitle = "StorageManager.estimate in C# (IStorageEstimator)",
            Description = "Read browser storage quota and usage from C# with IStorageEstimator, a navigator.storage.estimate wrapper, to budget caches and request persistent storage.",
        },
        new("storage", "IBrowserStorage", "Typed browser API: IBrowserStorage.", "Browser API reference", "apis/storage.md")
        {
            SearchTitle = "localStorage in C# and .NET (IBrowserStorage)",
            Description = "Get, set, remove and clear localStorage and sessionStorage strings from C# with IBrowserStorage, a typed Web Storage API wrapper for Server and WebAssembly.",
        },
        new("usb", "IUsb", "Typed browser API: IUsb.", "Browser API reference", "apis/usb.md")
        {
            SearchTitle = "WebUSB API in C# and .NET (IUsb)",
            Description = "Pick and drive a USB device from C# with IUsb, a WebUSB API wrapper for WebAssembly apps. The device chooser needs a user gesture, so Server has no support.",
        },
        new("vibration", "IVibration", "Typed browser API: IVibration.", "Browser API reference", "apis/vibration.md")
        {
            SearchTitle = "Vibration API in C# and .NET (IVibration)",
            Description = "Vibrate the device with a vibrate/pause pattern from C# using IVibration, a Vibration API wrapper. navigator.vibrate works on Android Chromium, not iOS.",
        },
        new("visual-viewport", "IVisualViewport", "Typed browser API: IVisualViewport.", "Browser API reference", "apis/visual-viewport.md")
        {
            SearchTitle = "Visual Viewport API in C# (IVisualViewport)",
            Description = "Read the visible viewport size, offset and zoom from C# with IVisualViewport, useful once a mobile soft keyboard opens. Works on Server and WebAssembly.",
        },
        new("wake-lock", "IWakeLock", "Typed browser API: IWakeLock.", "Browser API reference", "apis/wake-lock.md")
        {
            SearchTitle = "Screen Wake Lock API in C# and .NET (IWakeLock)",
            Description = "Keep the screen awake from C# with IWakeLock and release it by disposing the sentinel. The Screen Wake Lock API drops the lock when the page is hidden.",
        },
        new("web-locks", "IWebLocks", "Typed browser API: IWebLocks.", "Browser API reference", "apis/web-locks.md")
        {
            SearchTitle = "Web Locks API in C# and .NET (IWebLocks)",
            Description = "Coordinate work across tabs and workers from C# with IWebLocks: hold a named Web Locks API lock for a callback, try without waiting, or query the held locks.",
        },
        new("web-push", "IWebPush", "Typed browser API: IWebPush.", "Browser API reference", "apis/web-push.md")
        {
            SearchTitle = "Push API: subscribe to Web Push in C# (IWebPush)",
            Description = "Subscribe a browser to Web Push from C# with IWebPush: request permission, register the service worker, and get or remove the Push API subscription.",
        },
        new("webauthn", "IWebAuthn", "Typed browser API: IWebAuthn.", "Browser API reference", "apis/webauthn.md")
        {
            SearchTitle = "WebAuthn Passkeys in C# and .NET (IWebAuthn)",
            Description = "Register and sign in users with passkeys from C# through IWebAuthn, a typed Web Authentication API (WebAuthn) wrapper for Server and WebAssembly hosts.",
        },
        new("webrtc", "IWebRtc", "Typed browser API: IWebRtc.", "Browser API reference", "apis/webrtc.md")
        {
            SearchTitle = "WebRTC Data Channels in C# and .NET (IWebRtc)",
            Description = "Connect two browsers peer-to-peer from C# with IWebRtc: WebRTC data channels with batched messages plus camera, microphone and screen streams.",
        },

        // ---- Advanced ----
        new("testing", "Testing", "Unit testing with Rask.Testing, event handlers, E2E.", "Advanced")
        {
            SearchTitle = "Unit testing C# UI components without a browser",
            Description = "Render C# components to HTML in a unit test, with no browser or server. Dispatch click and input handlers, fake IJSRuntime and uploads, and check validation.",
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
        new("code-analysis", "Code analysis", "The analyzers and warnings-as-errors adoption.", "Advanced")
        {
            SearchTitle = ".NET analyzers and warnings-as-errors setup",
            Description = "How the repo builds with .NET analyzers and warnings as errors: the public API analyzer gate, recommended Roslyn analyzers, and adopting them one per PR.",
        },
        new("api-style", "Public API style", "How every public name is chosen, and the gate that records the surface.", "Advanced")
        {
            SearchTitle = "Public API naming guidelines for .NET libraries",
            Description = "The naming rules every public API in the framework follows: short nouns, BCL verbs, Async plus a CancellationToken, no bare bool, and the RS0016/RS0017 gate.",
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
            Description = "How changes to the framework are built, tested and shipped: warnings-as-errors builds, local pre-commit and pre-push gates, MinVer tags and NuGet releases.",
        },
        new("repo-administration", "Repo administration", "Governance, CODEOWNERS, releases, automation.", "Contributing & internals")
        {
            SearchTitle = "GitHub branch protection and repository settings",
            Description = "The GitHub settings behind the repository: branch protection on main, required code owner review, why no status checks are required, and workflow secrets.",
        },
        new("ai-agents", "Building with AI assistants", "Conventions for AI coding agents working on Rask.", "Contributing & internals")
        {
            SearchTitle = "Building .NET apps with AI coding assistants",
            Description = "Point an AI coding assistant at the repo's AGENTS.md and llms.txt so it scaffolds and extends C# apps following the framework's conventions and diagnostics.",
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
        ["Start here", "Tutorial", "One Person Framework", "Core", "Integration",
         "Mobile & devices", "Browser API reference", "Advanced", "Contributing & internals"];

    /// <summary>The catalog entry for a slug, or <c>null</c> when no guide has that name.</summary>
    /// <remarks>
    ///     A guide's head is built from the whole entry — its search title, its description, its group — so
    ///     the page asks for the entry once rather than for each field by slug. An unknown slug is not in the
    ///     sitemap and is not prerendered, so it only ever reaches a visitor who typed it.
    /// </remarks>
    public static GuideEntry? Find(string slug)
    {
        foreach (var g in All)
        {
            if (g.Slug == slug)
            {
                return g;
            }
        }

        return null;
    }

    public static string TitleFor(string slug)
    {
        foreach (var g in All)
        {
            if (g.Slug == slug)
            {
                return g.Title;
            }
        }

        return slug;
    }

    // The doc's path under docs/, for the "edit on GitHub" link. Subfolder guides carry it explicitly
    // (the slug is only the bare leaf); top-level guides default to "{slug}.md".
    public static string SourcePath(string slug)
    {
        foreach (var g in All)
        {
            if (g.Slug == slug)
            {
                return g.Source ?? $"{slug}.md";
            }
        }

        return $"{slug}.md";
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
