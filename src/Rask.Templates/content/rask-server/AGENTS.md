# AGENTS.md — building this app with an AI assistant

This is a **Rask** app (a C# component framework for .NET 10). This file tells AI coding
assistants the conventions so generated code compiles and runs. Full docs:
https://github.com/pal-tamas/rask/tree/main/docs

## Mental model
- Components are **plain C# classes** deriving from `Component`. Override `Component? Render()`
  and return a tree of HTML built with **generated factory methods** — no `.razor`, no JSX.
- The **same component code** runs server-rendered (live diff over WebSockets) or on WASM.

## Writing components — the rules that matter
- **Use factories, never `new`** for components: `Div(...)`, `Button(OnClick: ...)`, `Counter(...)`.
  Constructing a component with `new` outside the framework is a compile error (RASK014).
- **Children go through the indexer**, not a constructor arg: `Div()[Span()["hi"], "text"]`.
  A bare `string` becomes a text node. Pass a list directly for collections: `Ul()[items]`.
  `..` spread does **not** work inside `[...]`.
- **Props are factory parameters.** A nullable prop is optional; a non-nullable prop with no
  initializer is **required**. Declare HTML attributes nullable (`bool? Disabled`) to keep them optional.
- **A page/root component must render the full shell**: `[Doctype(), Html(...)[Head(...), Body(...)]]`
  (RASK021). The framework injects its runtime `<script>` automatically — don't add one.
- **Text vs raw:** `Text("..")` / a bare string HTML-encodes; `Raw("..")` is verbatim (XSS risk — avoid for user input).
- **Accessibility:** set ARIA via the `Aria` dictionary on any element — `Button(Aria: new() { ["label"] = "Close" })`
  renders `aria-label="Close"`. `Role:` / `TabIndex:` are typed params. `Img` needs `Alt:` (or `Alt: ""`
  for decorative images) or RASK023 warns. The `Bs*` form controls auto-wire `aria-invalid` +
  `aria-describedby` + a `role="alert"` error region when a bound field is invalid. See `docs/accessibility.md`.
- **Observability:** framework faults flow into your `ILogger` automatically; metrics publish on the
  `Rask.Server` meter (`dotnet-counters --counters Rask.Server` or `AddMeter(...)`), traces on the
  `Rask.Server` activity source. Add `services.AddHealthChecks().AddRaskLiveSessions()` for a
  live-session capacity probe. See `docs/observability.md`.

## Routing & lifecycle
- Route with an attribute: `[Route("/users/{id:int}")]` + `[RouteParam] public int Id { get; set; }`
  (or `[QueryParam]`). Nested routes: `[ParentRoute(typeof(Parent))]` + `Outlet()`.
- Lifecycle hooks: `OnMount`/`OnMountAsync` (once), `OnPropsChanged*` (on bound-prop/route change),
  `OnRendered(bool firstRender)`, `OnUnmount*`. Navigate only from event handlers via injected `Navigator`.
- **Inject services (`HttpClient`, `Navigator`, `IJSRuntime`, the typed browser APIs
  `IBrowserStorage`/`ICookies`/`IClipboard`/`IGeolocation`/`IPermissions`/`IVibration`/`IPageVisibility`/`INavigatorInfo`/`INetworkInfo`/`IMediaQuery`/`ISpeechSynthesis`/`IScreenInfo`/`IStorageEstimator`/`IVisualViewport`/`IBroadcastChannel`/`IIntersectionObserver`/`IResizeObserver`/`IMutationObserver`/`IMediaSession`/`IGamepad`/`IDeviceOrientation`/`IDeviceMotion`/`ICrypto`/`IPerformance`/`IIndexedDb`/`IFileSystemAccess`/`IWebAuthn`, your own) through the constructor**,
  not as settable properties (a non-nullable settable property becomes a required factory param).
- **Async cancellation:** pass `CancellationToken` into the cancellable work a handler or lifecycle hook
  starts, e.g. `await http.GetFromJsonAsync<T>(url, CancellationToken)`. It cancels on unmount, and —
  inside an event handler — also on the optional `RaskServerOptions.HandlerTimeout` / socket close.

## Events, scoped CSS/JS, forms, auth
- **Events / parent callbacks** are plain delegate props: child declares `Action<int>? OnRate`,
  invokes `OnRate?.Invoke(n)`; parent passes `OnRate: n => _x = n`. Invoking re-renders the parent.
- **Toast messages:** inject `IToaster` and call `toast.Success("Saved")` (`Info`/`Warning`/`Error`) before `Navigator.NavigateTo(...)`; it's scoped per session, so the message survives the navigation. Mount one `ToastOutlet` (headless) — or `Rask.Bootstrap`'s `BsToaster` — in your layout to show them once.
- **Scoped CSS/JS:** put `MyComponent.css` / `MyComponent.js` next to `MyComponent.cs`; auto-scoped.
- **Forms:** two-way bind with `Input.Bound(() => model.Name)`; build choice controls by implementing `IFormControl<T>` (the generator synthesizes the bound + controlled factories) or with the public `ExpressionAccessor`/`BindingHelpers`/`EditContext.RegisterFieldValidator` API in `Rask.Core.Forms` (or use the typed controls in **Rask.Bootstrap**: `BsRadioGroup`/`BsCheckboxGroup`/`BsMultiSelect`/`BsInput`/`BsSelect`/`BsCheck`).
- **Bootstrap (Rask.Bootstrap):** typed Bootstrap 5.3 components (`BsButton`/`BsCard`/`BsModal`/`BsAlert`/…); interactive ones (modal/dropdown/accordion/tabs) run with **zero JS** via the live runtime. Link the CSS with `BootstrapStyles()` in `App.Head`; typed utility classes via `Bs.Join(Shadow.Sm, Margin.Bottom(4))`.
- **CQRS (`Rask.Cqrs`):** opt-in, source-generated mediator — scaffold it with `dotnet new rask-server --cqrs` (adds a sample query + handler and a `/greeting` page under `Cqrs/`), or add it by hand: `dotnet add package Rask.Cqrs`, define `IQuery<T>`/`ICommand`/`ICommand<T>`/`INotification` messages + their handlers, call `builder.Services.AddRaskCqrs()`, then inject `IDispatcher` and call `DispatchAsync` (one method for queries and commands; response type inferred) or `PublishAsync` for notifications. Reflection-free (trim/AOT-safe); pipeline behaviors are the decorator hook (`AddOpenBehavior`). See docs/cqrs.md.
- **SQLite production pragmas (`Rask.SQLite`):** opt-in — `dotnet add package Rask.SQLite`, then swap `UseSqlite(cs)` for `UseRaskSqlite(cs)` on your EF Core `DbContextOptionsBuilder` (or `builder.Services.AddRaskSqlite(cs)` + inject `IRaskSqliteConnectionFactory` for raw ADO.NET). Applies the Rails 8 production pragma set — WAL, `synchronous=NORMAL`, `foreign_keys=ON`, `busy_timeout=5000`, `cache_size`, `mmap_size`, `journal_size_limit` — on every connection open, so concurrent writers stop hitting "database is locked". Override any via `SqlitePragmaOptions` (or set one to null to skip). Server-side only. See docs/sqlite.md.
- **SQLite backup (`Rask.SQLite.Litestream`):** opt-in — managed [Litestream](https://litestream.io) supervisor. `builder.Services.AddRaskSqliteLitestream(o => { o.DatabasePath = dbPath; o.ReplicaUrl = "s3://bucket/app"; })` runs a hosted service that streams the WAL to S3/GCS/Azure/file storage; call `await app.Services.RestoreSqliteFromLitestreamAsync()` after `Build()` (before opening the DB) to restore on a fresh host. Requires the `litestream` binary (set `ExecutablePath` if not on PATH); single-writer only; keep the DB on local disk (not a network share). Great fit for ephemeral containers / App Service Linux. See docs/sqlite.md.
- **SQLite snapshots (`Rask.SQLite.Snapshots`):** opt-in — scheduled consistent backups with no external binary. `builder.Services.AddRaskSqliteSnapshots(o => { o.DatabasePath = dbPath; o.DestinationDirectory = "/backups"; o.Interval = TimeSpan.FromHours(6); o.Retain = 14; })` runs a hosted service that snapshots the DB via SQLite's Online Backup API and prunes to the newest N; or inject `ISqliteSnapshotter` for an on-demand `SnapshotAsync()` (e.g. before a migration). Plug in `ISqliteSnapshotStore` for object storage. Pairs with Litestream (streaming) or stands alone. See docs/sqlite.md.
- **Auth:** gate by injecting `IUserProvider` and reading `.Current` (a never-null `ClaimsPrincipal`), or the `Authorize(...)` component (its `Authorized: user => …` slot is handed the principal; static content uses the `[ … ]` indexer);
  `[Authorize]`/`[AllowAnonymous]` on a page. Configure on ASP.NET's own `AddCookie`/`AddJwtBearer`.
- **PWA (`--pwa`):** scaffolded with `--pwa`. `builder.Services.AddRaskPwa(new WebAppManifest { … })` (in `Program.cs`) serves the manifest + Rask's service worker, auto-registers it, and emits the manifest link into the server-rendered `<head>`. Ships `wwwroot/icon.svg` + `wwwroot/offline.html`. A Server PWA is **installable + push-capable** (`IWebPush`/`INotifications`/`IBadge`/`IWakeLock` in `Rask.Core.Browser`), but **not an offline app** — offline navigations show `offline.html`. Add `Rask.WebPush` to send push. See docs/pwa.md.

## Build & run
```bash
dotnet run        # then open the printed URL  (or: rask dev — hot reload, via `dotnet tool install -g Rask.Cli`)
dotnet test       # if the project has tests
```
- **Unit-testing components:** add `Rask.Testing` to your test project, then `RaskTest.Render(new MyComponent())` and drive it via `.ClickAsync()`/`.InputAsync()`/`.SubmitAsync()`, asserting on `.Html` — no browser or server. See docs/testing.md.
- **Deploy (`--docker`):** scaffolded with `dotnet new rask-server --docker` — a multi-stage `Dockerfile` (+ `.dockerignore`) that runs on `aspnet:10.0` (non-root, port 8080). `docker build -t myapp . && docker run -p 8080:8080 myapp`. `UseHttpsRedirection()` no-ops in-container; terminate TLS at your proxy and forward the WebSocket upgrade to 8080. See docs/deployment.md.

If you hit a `RASKxxx` compile error, see https://github.com/pal-tamas/rask/blob/main/docs/diagnostics.md
