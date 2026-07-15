# Rask documentation

Guides and references for building with Rask, **the .NET One Person Framework** — build, run, and ship a
whole product solo, in C#, on one server. **New to Rask?** Read
[Getting started](getting-started.md) start to finish — it goes from zero to a running, routed,
interactive app. Want the philosophy first? Read **[The .NET One Person Framework](one-person-framework.md)**.
Want the pitch and a quick demo? See the project [README](../README.md).

## Start here

| Guide | What it covers |
|-------|----------------|
| [**The .NET One Person Framework**](one-person-framework.md) | The doctrine: one developer, a whole product, one C# codebase, one server, SQLite-first — and the batteries that make it real. |
| [Roadmap](roadmap.md) | The One-Person-Framework pillars — shipped vs planned (DB-backed jobs, outbox, mail, cache, broadcast). |

## Guides

| Guide | What it covers |
|-------|----------------|
| [Getting started](getting-started.md) | Prerequisites, scaffold an app, a tour of the generated files, your first component, interactivity, routing, and troubleshooting. |
| [The `rask` CLI](cli.md) | The optional `Rask.Cli` .NET tool: `rask new` (scaffold), `rask dev` (hot-reload run), `rask info` — a thin wrapper over the .NET SDK. |
| [Live playground](playground.md) | Write Rask C# in the browser with IntelliSense, as-you-type diagnostics, and a gallery of examples, then see it compile & render live (Roslyn in WebAssembly) — how it works, the entry-point convention, and its limitations. |
| [Best practices](best-practices.md) | Production patterns and common pitfalls across component design, state, forms, data access, security, accessibility, performance and testing — each linking to the deep dive. |
| [Routing](routing.md) | `[Route]`, route/query params, nested routes, type-safe `Routes.*` URLs, `Navigator`, `RouteState`. |
| [Composition](composition.md) | Children & fragments, callbacks (child→parent), context (provide/consume), toast messages (`IToaster`/`ToastOutlet`), `VirtualizeModel`, drag-and-drop. |
| [JS interop](js-interop.md) | Scoped CSS & JS conventions, calling JS via `IJSRuntime`, element refs (`Ref:`), typed browser APIs, asset delivery. |
| [Browser APIs](browser-apis.md) | The map of all 43 typed Web-API wrappers — shared vs WASM-only, one-shot vs subscription, the inject-from-ctor and push/`[JSInvokable]` patterns. |
| [Capability matrix](browser-capabilities.md) | Where each of the 43 APIs works (Web / PWA / Native) and which have a native iOS/Android backend — links to a reference page per API under [`apis/`](apis/). |
| [📱 Mobile & PWA](pwa.md) | Build installable, offline mobile apps in C# (WASM): web app manifest, service worker, Web Push (`IWebPush`), `dotnet new rask-wasm --pwa`. |
| [📱 Native mobile (iOS/Android)](native.md) | Ship the same components as a native iOS/Android app with `Rask.Native` (preview): the WebView-hybrid host, the `rask-native` template + platform heads, `NativeAppHost` Local/Server modes, `INativeWebView`, safe-area insets. |
| [AOT compilation](aot.md) | Opt-in full WASM AOT (`-p:RaskWasmAot=true`): the reflection-free binding registry, registering custom `IParsable` types, `InvokeAsync<T>` under AOT, and the continuous analyzer gate. |
| [Forms & validation](forms.md) | Two-way binding, `Form<T>`/`EditContext`, inline / DataAnnotations / FluentValidation / async validators, radio & checkbox groups. |
| [Lifecycle](lifecycle.md) | `OnMount` / `OnPropsChanged` / `OnRendered` / `OnUnmount`, async-hook rules, cancellation, common gotchas. |
| [Data access (EF Core)](data-access.md) | EF Core + SQLite in a Server app: `IDbContextFactory`, loading in the lifecycle, vertical slices, a DDD aggregate + value objects, and the SQLite decimal gotcha. |
| [SQLite production pragmas](sqlite.md) | Rails-style production SQLite via `UseRaskSqlite` / `AddRaskSqlite` (standalone `Rask.SQLite`): WAL, `foreign_keys`, `busy_timeout` & friends applied on every connection open. |
| [CQRS](cqrs.md) | Source-generated, trim-safe queries / commands / notifications and pipeline behaviors via `AddRaskCqrs()` + `IDispatcher` (standalone `Rask.Cqrs`). |
| [Authentication](authentication.md) | Production auth: cookie & JWT, Server & WASM, `Authorize`, route guards, Identity / Keycloak / Auth0 / Cognito / Duende. |
| [Accessibility](accessibility.md) | Setting ARIA attributes, `Role`/`TabIndex`, and focus on any element; the `Img` alt-text analyzer (RASK023). |
| [Testing](testing.md) | Unit-testing components with `Rask.TestSupport`, driving event handlers, when to reach for E2E. |
| [Migrating from Blazor](migration-from-blazor.md) | Concept mapping, behavioural gotchas, and what stays the same. |
| [Building with AI assistants](ai-agents.md) | The `AGENTS.md` / `llms.txt` artifacts that let AI tools scaffold and extend Rask apps. |

## Bootstrap components

The optional `Rask.Bootstrap` package — typed Bootstrap 5.3 component factories, layered on top of
core. Start at the [hub](bootstrap.md) for setup and the component map; each component group then has
its own page:

| Guide | What it covers |
|-------|----------------|
| [Bootstrap](bootstrap.md) | Setup (`BootstrapStyles()`), color modes, the typed enums, the component map, and versioning. |
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
| [Diagnostics (RASK001–031)](diagnostics.md) | Every analyzer/generator diagnostic, what triggers it, and how to fix it. |
| [Code analysis](code-analysis.md) | Analyzers, warnings-as-errors, and the per-PR adoption procedure. |

## Contributing

| Doc | What it covers |
|-----|----------------|
| [Development workflow](development-workflow.md) | The format → warnings-as-errors → tests → benchmarks → docs → review → PR gate, CI, nightly, releases. |

## Architecture

| Doc | What it covers |
|-----|----------------|
| [Live rendering & the diff codec](architecture/live-rendering.md) | How the render walk, frame stream, edit-op diff, keyed reconciliation, and the two transports (Server WS / WASM JSImport) work. |

---

The in-repo map for contributors lives in [CLAUDE.md](../CLAUDE.md). Runnable feature demos are
under [`samples/`](../samples).
