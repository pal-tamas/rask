# Configuration

**Every Rask setting lives in `appsettings.json`, under `Rask`.** Each registration reads its own section
when the host builds its options — `AddRask` reads `Rask:Live`, `Rask:Server`, `Rask:Culture` and
`Rask:Uploads`, `AddRaskMail` reads `Rask:Mail`, and so on — so `Program.cs` says what the app is made of
and `appsettings.json` says how it is tuned:

```jsonc
// appsettings.json
{
  "Rask": {
    "ConnectionStrings": {
      "App": "Data Source=app.db"
    },
    "Server": {
      "MaxInboundFramesPerSecond": 500,
      "SessionGracePeriod": "00:00:20"
    },
    "Mail": {
      "From": "no-reply@example.com"
    }
  }
}
```

```csharp
builder.Services.AddRask();
builder.Services.AddRaskMail<AppDbContext>();
```

Nobody writes `GetSection(...).Bind(...)`. Every option has a production-safe default, so a section appears
only when the app sets something, and `AddRask()` with no configuration at all is fully functional.

## Precedence

Lowest first — each step overrides the ones above it:

1. The options type's own defaults.
2. **`RaskApp` only:** the [development defaults](#raskapps-development-defaults) that let a battery which
   is on by default start with nothing configured.
3. `appsettings.json`.
4. `appsettings.{Environment}.json`.
5. User secrets (in Development).
6. Environment variables.
7. Command-line arguments.
8. The code callback on the registration — `AddRaskMail<AppDbContext>(o => …)`, or
   `app.Configure(c => c.Mail.Configure(o => …))` in a `RaskApp`.
9. Validation, which sees the result of all of the above.

**Code wins over configuration.** A callback is for the rare value that has to be code; a value that is
merely a value belongs in `appsettings.json`, where a deployment can override it without a rebuild.

Steps 3–7 are ASP.NET Core's standard configuration sources as `WebApplication.CreateBuilder` adds them, so a
source you add yourself (a key vault, a custom provider) takes part in the same order.

## Environment variables

A nested key uses a double underscore in the variable name:

```bash
Rask__Mail__From=orders@example.com
Rask__ConnectionStrings__App="Data Source=/data/app.db"
Rask__Server__SessionGracePeriod=00:00:20
```

That is how a deployment overrides a committed value, and how a secret reaches the app without being in
source: `rask deploy --env "Rask__Mail__Smtp__Password=…"`. See [Secrets](secrets.md).

## Value formats

| Kind | Write it as | Example |
| --- | --- | --- |
| `TimeSpan` | `"[d.]hh:mm:ss[.fffffff]"` | `"00:00:20"` (20 s), `"1.00:00:00"` (one day) |
| Enum | The member's name | `"DisabledFull"`, `"Warning"` |
| `bool` | `true` / `false` | `"SessionResume": false` |
| Number | A JSON number | `"MaxSessions": 1000` |
| List | A JSON array, or an index per key | `"SupportedCultures": [ "en", "hu" ]`, `Rask__Culture__SupportedCultures__0=en` |

**A list that already has entries is appended to, not replaced.** This matters for three lists:
`Rask:Culture:SupportedCultures`, `Rask:Logging:ExcludedCategories` and `Rask:Spa:ImmutablePathPrefixes`.
Configuration adds to whatever the defaults (or an earlier source) put there; index `0` in configuration is
the first entry *configuration* adds, not the first entry of the list. To remove a default entry, do it in
the callback.

## A bad value stops the host starting

Options are validated when the host starts, and a bad value fails the start with
`Microsoft.Extensions.Options.OptionsValidationException`. The message names the section, whether the value
came from `appsettings.json`, the environment or a callback:

```text
Microsoft.Extensions.Options.OptionsValidationException: Rask:Server: SessionGracePeriod must be positive. …
```

A value the binder cannot convert at all — `"five"` for a `TimeSpan` — is reported the same way. Before this,
most `AddRaskX` calls threw from the registration line itself; now nothing is read until the options are
built, so a source added after the registration still counts.

A few things to know:

- **A bearer signing key that cannot sign refuses to start outside Development** (`Rask:Auth`). In
  Development the app stays on cookies, so a first run needs no configuration.
- **A container with no `IConfiguration`** — a bare `ServiceCollection` in a unit test — reads no section
  and gets the defaults plus the callback, exactly what it got before configuration existed.
- **`Rask:Cqrs` is the one section read while services are registered**, because the handler lifetime and
  the validation switch decide which services exist. It is read from the host builder's configuration as
  it stands at the `AddRaskCqrs` call, so appsettings, user secrets and environment variables are all there;
  a bare container reads none.
- **`Rask:Database:Provider` is read while services are registered too**, by `RaskApp`, because the database decides
  which batteries exist — where the log is kept, whether snapshots run. Like `Rask:Cqrs`, it is read from the host
  builder's configuration as it stands.
- **A database connection string is required, not guessed.** `UseRaskDatabase(sp)`, `UseRaskSqlite(sp)`, `UseRaskPostgres(sp)`,
  `UseRaskSqlServer(sp)`, `AddRaskSqlite()` and `AddRaskLogging()` throw when their
  connection string is missing, naming the key
  to set (`Rask:ConnectionStrings:App` / `Rask__ConnectionStrings__App`). A database quietly opened in the
  working directory is how a container writes its data somewhere the next deploy deletes.

## Every section

| Section | Options type | Package | Notes |
| --- | --- | --- | --- |
| `Rask:Live` | `RaskLiveOptions` | `Rask.Server` | `DiffMode`, `MaxSessions`, `MinifyScopedAssets`, `PathBase`. A non-empty `UseRask<App>(pathBase:)` argument wins over `PathBase`. [Details](#live-runtime--rasklive). |
| `Rask:Server` | `RaskServerOptions` | `Rask.Server` | WebSocket caps, grace periods, resume, shutdown drain, and the initial render's `QuiescenceTimeout`. [Details](#server-host--raskserver). |
| `Rask:Culture` | `RaskCultureOptions` | `Rask.Server` | `SupportedCultures` (the first is the default; appended to), negotiation switches. See [localization](localization.md). |
| `Rask:Uploads` | `RaskUploadOptions` | `Rask.Server` | [File uploads](#file-uploads--raskuploads). |
| `Rask:DataProtection:KeyPath` | — | `Rask.Server` | Where the key ring persists. See [deployment](deployment.md#your-users-stay-signed-in-across-a-deploy). |
| `Rask:Auth` | `AuthOptions` | `Rask.Auth` | `Bearer`, `BearerSigningKey`, `BearerLifetime`, `FirstRunToken`, `CookieName`, the page paths, password and lockout rules. Keep `BearerSigningKey` in user secrets or the environment. See [authentication](authentication.md). |
| `Rask:Api` | `ApiOptions` | `Rask.Api` | `NotFound`, `Controllers`. |
| `Rask:Signaling` | `RaskSignalingOptions` | `Rask.Signaling` | `Path`, `RequireAuthorization` and the relay limits. `AuthorizeRoom` is code-only. |
| `Rask:Dashboard` | `RaskDashboardOptions` | `Rask.Dashboard` | Includes `AllowAnonymousAccess` — see [below](#guard-the-environment-like-code). See [dashboard](dashboard.md). |
| `Rask:Spa` | `SpaHostingOptions` | `Rask.Spa.Hosting` | Read when `UseRaskSpa` maps the app. `ImmutablePathPrefixes` is appended to; `ExcludeFromFallback` and `OnPrepareResponse` are code-only. See [TypeScript front ends](spa.md). |
| `Rask:Meta` | `MetaHostingOptions` | `Rask.Meta.Hosting` | `Framework` by the build's names (`nuxt`, `nextjs`, `tanstack-start`, `solidstart`, `sveltekit`, `analog`). Precedence: build metadata, then this section, then the callback, then a `rask dev` session's dev server. See [meta frameworks](meta.md). |
| `Rask:Data` | `RaskDataOptions` | `Rask.Data` | See [Rask.Data](data.md). |
| `Rask:Database:Provider` | — | `Rask` | Which database the app opens at `Rask:ConnectionStrings:App`: `sqlite` (the default), `postgres` or `sqlserver`. Read by `UseRaskDatabase(sp)`, and by `RaskApp` while services are registered. See [choosing the database](data.md#choosing-the-database). |
| `Rask:ConnectionStrings:App` | — | `Rask.SQLite`, `Rask.SQLite.EntityFrameworkCore`, `Rask.Postgres`, `Rask.SqlServer` | The application database. Also the default `DatabasePath` for Litestream and snapshots. |
| `Rask:Sqlite` | `SqliteOptions` | `Rask.SQLite` | The pragmas, `StrictTables`, and `Retry`. Read by `AddRaskSqlite()` and by `UseRaskSqlite(sp)`. See [SQLite](sqlite.md). |
| `Rask:Postgres` | `PostgresOptions` | `Rask.Postgres` | The session timeouts and `Retry`. Read by `UseRaskPostgres(sp)`. See [PostgreSQL](data.md#postgresql). |
| `Rask:SqlServer` | `SqlServerOptions` | `Rask.SqlServer` | `CommandTimeout`, `LockTimeout`, `AbortOnError` and `Retry`. Read by `UseRaskSqlServer(sp)`. See [SQL Server](data.md#sql-server). |
| `Storage` (not yet under `Rask`) | `StorageOptions` | `Rask.Storage` | The one exception for now: file storage still reads its own top-level section — `Storage__Provider`, `Storage__S3__Bucket` and the rest. See [file storage](file-storage.md). |
| `Rask:Litestream` | `LitestreamOptions` | `Rask.SQLite.Litestream` | `ReplicaUrl`, `ConfigPath`, `ExecutablePath`, `Verification`. `DatabasePath` defaults to the file behind `Rask:ConnectionStrings:App`. See [continuous backup](sqlite.md#continuous-backup-with-litestream). |
| `Rask:Snapshots` | `SqliteSnapshotOptions` | `Rask.SQLite.Snapshots` | `DestinationDirectory`, `Interval`, `Retain`. `DatabasePath` defaults the same way. See [snapshots](sqlite.md#scheduled-snapshots). |
| `Rask:Cache` | `CacheOptions` | `Rask.Cache` | See [cache](cache.md). |
| `Rask:Jobs` | `JobOptions` | `Rask.Jobs` | `AddRecurring` is code-only. See [jobs](jobs.md). |
| `Rask:ConnectionStrings:Logs` | — | `Rask.Logging` | The log store's own file. |
| `Rask:Logging` | `RaskLoggingOptions` | `Rask.Logging` | `ExcludedCategories` is appended to. See [logging](logging.md). |
| `Rask:Mail` | `MailOptions` | `Rask.Mail` | Any `Rask:Mail:Smtp` key turns SMTP delivery on; put `Rask__Mail__Smtp__Password` in the environment. See [mail](mail.md). |
| `Rask:Outbox` | `OutboxOptions` | `Rask.Outbox` | See [outbox](outbox.md). |
| `Rask:WebPush` | `WebPushOptions` | `Rask.WebPush` | `VapidKeys:PublicKey`, `VapidKeys:PrivateKey`, `Subject`, `DefaultTtl`. The keys belong in user secrets or the environment. See [Web Push](webpush.md). |
| `Rask:Cqrs` | `CqrsOptions` | `Rask.Cqrs` | `HandlerLifetime`, `NotificationPublishStrategy`, `StopOnFirstNotificationException`, `ValidateRequests`. Read at registration (above); behaviors are code-only. See [CQRS](cqrs.md). |
| `Rask:Cqrs:Server` | `RaskCqrsServerOptions` | `Rask.Cqrs.Server` | `RequireAuthenticatedUser`, `RoutePrefix`, the request and upload limits. |

### Guard the environment like code

Configuration can turn things *off* as easily as on. `Rask:Dashboard:AllowAnonymousAccess`,
`Rask:Signaling:RequireAuthorization` and `Rask:Cqrs:Server:RequireAuthenticatedUser` are all settable from
an environment variable, which is the point of them being configuration — and it means whoever can set the
deploy environment's variables can open the operator console to the internet. Treat the deploy environment
(`.env.production`, CI secrets, the host's Docker access) with the same care as the code.

## What stays in code

Configuration carries values. Anything that is behaviour stays on the callback:

- Delegates: `RaskSignalingOptions.AuthorizeRoom`, `SpaHostingOptions.ExcludeFromFallback` and
  `SpaHostingOptions.OnPrepareResponse`.
- Builder methods: `JobOptions.AddRecurring` (a schedule is code), `CqrsOptions.AddBehavior` and
  `CqrsOptions.AddOpenBehavior`.
- A `MetaHostingOptions.Framework` preset for a framework Rask has no name for.
- Removing an entry a list starts with.

**Browser apps are code-only.** A WebAssembly app built with `WasmHostBuilder` has no `appsettings.json` to
read — anything in its bundle is readable by every visitor anyway — so its options come from the callbacks
alone.

## `RaskApp`'s development defaults

An app built with the `Rask` package's `RaskApp` turns every battery on, and a few of them cannot start
without a value. `RaskApp` supplies those as the **lowest-precedence** configuration source, beneath
`appsettings.json`, so every one of them is overridden by the same key set anywhere else:

| Key | Default |
| --- | --- |
| `Rask:ConnectionStrings:App` | `Data Source=app.db` |
| `Rask:ConnectionStrings:Logs` | `Data Source=logs.db` |
| `Rask:Sqlite:StrictTables` | `true` |
| `Rask:Mail:From` | `no-reply@example.com` |
| `Rask:Mail:PickupDirectory` | `mail-pickup` |
| `Rask:Snapshots:DestinationDirectory` | `snapshots` |

`example.com` is reserved for documentation, so an app that never sets a From address cannot send as a
domain somebody owns. The consequence of booting with these is that a running app writes `app.db`,
`logs.db`, `mail-pickup/` and `snapshots/` beside itself (`rask new` gitignores them); `rask deploy` points
both connection strings at its volume.

`RaskAppOptions.ConnectionString`, set in code, beats every configuration source for
`Rask:ConnectionStrings:App` — the same way a callback does.

The `app.db` and `snapshots` defaults are SQLite's alone. With `Rask:Database:Provider` set to `postgres` or
`sqlserver` neither is added: a missing `Rask:ConnectionStrings:App` fails naming the key rather than handing a file
path to a server driver, and any `Rask:Snapshots` value is one the app set — which, with no SQLite file to copy,
refuses the start. See [choosing the database](data.md#choosing-the-database).

> **Migrating from the old keys.** Before every options type read its own section, a handful of settings
> lived at top-level keys. **Those keys are no longer read, and nothing warns you** — an app that still sets
> them silently runs on the defaults.
>
> | Old key | New key |
> | --- | --- |
> | `ConnectionStrings:App` | `Rask:ConnectionStrings:App` |
> | `ConnectionStrings:Logs` | `Rask:ConnectionStrings:Logs` |
> | `Litestream:ReplicaUrl` | `Rask:Litestream:ReplicaUrl` |
> | `Sqlite:SnapshotDirectory` | `Rask:Snapshots:DestinationDirectory` |
> | `WebPush:PublicKey` / `WebPush:PrivateKey` | `Rask:WebPush:VapidKeys:PublicKey` / `Rask:WebPush:VapidKeys:PrivateKey` |
> | `WebPush:Subject` | `Rask:WebPush:Subject` |
> | `Mail:PickupDirectory` | `Rask:Mail:PickupDirectory` |
> | `Rask:<ServerOption>` (e.g. `Rask:MaxInboundFramesPerSecond`) | `Rask:Server:<ServerOption>` |
>
> Environment variables follow the same rename: `ConnectionStrings__App` is now
> `Rask__ConnectionStrings__App`. **Upgrade the `rask` CLI together with the packages** — `rask deploy` now
> sets `Rask__ConnectionStrings__App` and `Rask__ConnectionStrings__Logs`, which an older app does not read,
> and an older CLI sets the old names, which a newer app does not read.
>
> Five overloads lost their connection-string parameter; the connection string comes from configuration:
>
> | Was | Now |
> | --- | --- |
> | `o.UseRaskSqlite(connectionString, configure)` | `AddDbContextFactory<AppDbContext>((sp, o) => o.UseRaskSqlite(sp, configure))` |
> | `services.AddRaskSqlite(connectionString, configure)` | `services.AddRaskSqlite(configure)` |
> | `services.AddRaskLogging(connectionString, configure)` | `services.AddRaskLogging(configure)` |
> | `o.UseRaskPostgres(connectionString, configure)` | `AddDbContextFactory<AppDbContext>((sp, o) => o.UseRaskPostgres(sp, configure))` |
> | `o.UseRaskSqlServer(connectionString, configure)` | `AddDbContextFactory<AppDbContext>((sp, o) => o.UseRaskSqlServer(sp, configure))` |
>
> `AddRaskSqliteLitestream`, `AddRaskSqliteSnapshots` and `AddRaskWebPush` no longer require a callback.
> And a `configureServer: o => builder.Configuration.GetSection("Rask").Bind(o)` written against the old
> guidance can simply be deleted: `Rask:Server` is bound for you.

## Live runtime — `Rask:Live`

`RaskLiveOptions`, shared by the Server and WASM runtimes (on the Server host, read from `Rask:Live`; in a
browser app, set in code).

| Option | Default | Purpose |
| --- | --- | --- |
| `DiffMode` | `Auto` | Wire payload shape — `Auto` ships a diff when smaller, `DisabledFull` always full HTML, `Forced` always a diff. |
| `PathBase` | `""` | URL prefix so two Rask apps share one origin (e.g. `/appA`). An explicit, non-empty `UseRask<App>(pathBase: …)` wins over it. |
| `MaxSessions` | `0` (uncapped) | Hard cap on concurrent live sessions; a GET past the cap gets `503` + `Retry-After`. Pairs with the [health check](observability.md#health-checks). See [sizing it for a memory budget](#sizing-maxsessions-for-a-memory-budget). |
| `MinifyScopedAssets` | `null` (auto) | Minify the scoped-CSS bundle (strip comments + insignificant whitespace) before it's hashed and served. `null` = **auto**: on outside `Development`, off in `Development` (so hot-reloaded CSS stays readable) — resolved by `UseRask` from `IHostEnvironment`. Set `true`/`false` to force it. Minifying before hashing keeps the digest, immutable URL, and brotli/gzip caches all keyed off the minified bytes. Conservative: only the CSS bundle is minified (JS is served as-is), and only whitespace around `{ } ; ,` is stripped, so combinators and `calc()` are untouched. |

```jsonc
{
  "Rask": {
    "Live": {
      "MaxSessions": 1000,
      "PathBase": "/appA"
    }
  }
}
```

In the environment: `Rask__Live__MaxSessions=1000`.

## Server host — `Rask:Server`

`RaskServerOptions`: WebSocket safety caps and session grace periods — only the ASP.NET host has these.

| Option | Default | Purpose |
| --- | --- | --- |
| `MaxInboundFrameBytes` | `8 MB` | Cap on a single reassembled inbound WebSocket frame — bounds a fragmented-frame memory DoS. |
| `MaxPendingHandlers` | `512` | Max queued handler dispatches before the socket is closed (backpressure). `0` disables. |
| `MaxInboundFramesPerSecond` | `1000` | Per-connection inbound message rate cap over a sliding 1 s window — bounds a small-frame CPU DoS. `0` disables. |
| `SessionGracePeriod` | `30 s` | How long a session is retained after its socket disconnects, for reconnect. |
| `UnconnectedSessionGracePeriod` | `10 s` | How long a GET-minted session is retained before its first `hello` arrives. |
| `IdleSocketTimeout` | `0` (off) | Close a connected socket that sends no inbound frame for this long (the session survives for reconnect). Reclaims silently-idle connections. |
| `MaxPendingHandlerBytes` | `0` (off) | Aggregate-bytes companion to `MaxPendingHandlers` — bounds the queued cloned-payload *memory*, not just the queue length. |
| `SendTimeout` | `30 s` | How long one outbound frame may take before the socket is aborted. A client that stops reading TCP would otherwise pin its session's render lock — and its disposal — indefinitely. The session survives for the grace period, so a briefly-stalled link reconnects normally. `0` disables. |
| `SessionResume` | `true` | Let a client rebuild its page on a host that has never heard of its session — after a restart or a redeploy. See [surviving a restart](#surviving-a-restart-or-a-redeploy). Turn it off to keep the reload. |
| `ResumeTokenLifetime` | `1 h` | How long a resume record stays redeemable. Not the reconnect grace period: that covers a blip against the *intact* session, this covers the session being gone. |
| `HandlerTimeout` | `0` (off) | Cancel a handler's `Component.CancellationToken` after this long. A handler that threads that token into its async work unwinds cleanly instead of pinning the render pipeline (cooperative — a token-ignoring handler can't be force-aborted). |
| `ShutdownDrainTimeout` | `5 s` | Budget for the graceful shutdown drain: announce the shutdown, let in-flight handlers finish, close each socket with a real handshake, dispose the sessions. `0` disables the drain (abort immediately). See [Shutdown and redeploy](#shutdown-and-redeploy). |
| `QuiescenceTimeout` | `5 s` | How long the initial `GET` waits for a page's async lifecycle work to settle before serving its HTML. `0` disables the wait. See [live pages](render-modes.md#the-initial-get-waits-for-your-data). |

```jsonc
{
  "Rask": {
    "Server": {
      "MaxInboundFramesPerSecond": 500,
      "SessionGracePeriod": "00:00:20",
      "QuiescenceTimeout": "00:00:02"
    }
  }
}
```

In the environment: `Rask__Server__SessionGracePeriod=00:00:20`, `Rask__Server__QuiescenceTimeout=00:00:02`.

`AddRask` still takes the two callbacks — `configure` for `RaskLiveOptions` and `configureServer` for
`RaskServerOptions` — and they run after the sections, so a value set there wins:

```csharp
builder.Services.AddRask(
    live   => live.MaxSessions = 1000,
    server => server.SessionGracePeriod = TimeSpan.FromSeconds(20));
```

### Shutdown and redeploy

On `SIGTERM` — a redeploy, a container recycle, `Ctrl+C` — Rask drains instead of severing, with no
wiring on your part:

1. **Admission closes.** New sessions are refused with `503` + `Retry-After: 1`, and the `ready`-tagged
   health check goes unhealthy so a proxy or load balancer with active probes stops routing here.
2. **Connected browsers are told.** Every live session gets a `{"type":"shutdown"}` frame, so the client
   shows **"Updating…"** rather than the reconnect spinner — and, because the drop is now *expected* rather
   than guessed at, it reconnects immediately instead of walking a 500 ms → 5 s backoff ladder first.
3. **In-flight handlers finish.** A click that is mid-`SaveChangesAsync` completes rather than being
   cancelled halfway.
4. **Sockets close cleanly** — WebSocket status `1001` ("going away"), not an abort — and the sessions
   are disposed before the host stops.

Anything still connected when `ShutdownDrainTimeout` elapses is aborted, and
`rask.shutdown.sessions.abandoned` counts it.

> **What happens to the page itself is the replacement server's decision, not the client's.** A live
> session is a component tree plus a DI scope living in *that* process, so the new instance cannot inherit
> it. The client therefore reconnects and lets the host that answers decide: if it can rebuild the page,
> nothing reloads at all; if it reports the session unknown, the client reloads — in ~250 ms, saying
> "Updating…", and restoring your scroll position, your focus, and whatever you had typed.
>
> The drain's job is to make that drop *expected*. Before it, the socket was aborted with no close frame,
> so the browser could not tell a deployment from a crash: it froze, backed off, and eventually announced
> a four-second "Your session timed out" for a session that had not timed out.
>
> **What the user had typed comes back too, as a three-way merge.** Only fields they actually edited are
> candidates, each carrying the value the server had rendered *before* they touched it. After the reload
> a field is re-applied only when the replacement rendered that same value — its state is unchanged, so
> the edit is still the newest thing anyone knows. If the replacement rendered something different it
> knows something the stale copy doesn't, so it wins and the edit is dropped silently. Whatever is
> restored is then pushed back over the socket, so the server's model holds what the page shows rather
> than the pristine values it just rendered.
>
> Two rules worth knowing when you build forms:
>
> - **A field needs an `id` or a `name` to be restorable.** A bound `Input` gets a `name` from the bound
>   property automatically, so this is usually free — but if a key matches more than one control on the
>   page, that field is skipped rather than guessed at.
> - **`data-rask-no-restore` opts a field (or a whole subtree) out.** Passwords, file, hidden and
>   one-time-code inputs, and anything with a `cc-*` / `current-password` / `new-password` `autocomplete`,
>   are excluded unconditionally and never reach `sessionStorage` in the first place.
>
> `<select>` is not restored yet. The lagging-frame guard it needs — to hold off the replacement's first
> catch-up render — now exists alongside the ones for `value` and `checked`, but the save/apply side does
> not cover it. A `<select multiple>` needs one more thing first: the change dispatch reports the select's
> single `value`, which is only its first selected option.

`ShutdownDrainTimeout` must fit inside `HostOptions.ShutdownTimeout`, which must fit inside whatever your
container runtime allows between `SIGTERM` and `SIGKILL` (`rask deploy` uses 20 s). Rask logs a warning at
startup if the first of those doesn't hold. Note that `HostOptions.ServicesStopConcurrently` is `false` by
default, so other hosted services' shutdown work is spent from the same budget first.

### Reconnect UX

When the WebSocket drops, the client reconnects with exponential backoff. The framework's built-in
overlay handles the user-facing side automatically — nothing to configure:

- **Debounced.** A sub-second blip reconnects before the overlay ever appears (≈700 ms grace), so a
  brief hiccup never flashes a full-screen freeze over the app.
- **Escalating.** If reconnection keeps failing — or the browser reports itself offline — the overlay
  escalates from a neutral spinner to an explanatory message ("You're offline…" / "Still trying…") with
  a manual **Retry now** button. Regaining connectivity (the `online` event) reconnects immediately.
- **Session-expiry aware.** If the drop outlasts `SessionGracePeriod` the server discards the session;
  the client then shows "Your session timed out. Reload to continue." with a **Reload** button (and a
  fallback auto-reload) instead of silently reloading and wiping in-progress UI state. Raise
  `SessionGracePeriod` if your users routinely background the tab longer than the 30 s default.
- **Deploy aware.** A drop caused by the server shutting down is *not* reported as a timeout — see
  [Shutdown and redeploy](#shutdown-and-redeploy).

> **Using `HandlerTimeout`:** thread `Component.CancellationToken` into the cancellable async work your
> event handlers start, so the timeout (or socket close) can unwind them. Inside a handler that token
> reflects the dispatch; in a lifecycle hook it's just the component's lifetime token.
> ```csharp
> Button.OnClick(async () =>
>     _data = await http.GetFromJsonAsync<T>(url, CancellationToken))["Load"]
> ```

### File uploads — `Rask:Uploads`

`RaskUploadOptions` caps uploads:

| Option | Default | Purpose |
| --- | --- | --- |
| `MaxFileSize` | `50 MB` | Maximum size of a single uploaded file. |
| `MaxFilesPerRequest` | `16` | Maximum files in one multipart upload request. |
| `MaxBytesPerSession` | `0` (off) | Maximum cumulative staged-upload bytes one session may hold at once; a request over the quota is rejected with `413`. Released when the session ends. |

```jsonc
{
  "Rask": {
    "Uploads": {
      "MaxFileSize": 10485760
    }
  }
}
```

In the environment: `Rask__Uploads__MaxFileSize=10485760`. `AddRask` has no callback for these; a
`builder.Services.Configure<RaskUploadOptions>(o => …)` after it still runs last and wins.

## Surviving a restart or a redeploy

A live session is a component tree, a DI scope and a set of cancellation tokens. None of that can be
serialized, so a session cannot be moved or saved — when the process holding it goes away, it is gone.

That used to be the end of the story: every `rask deploy` replaces the container, so every connected
client got *"Your session timed out. Reload to continue."* The deploy was zero-downtime for HTTP and a
full reload for everyone actually using the app.

What travels instead is a small sealed record of **where the page was** and **what the app declared**, and
a host that receives it back **rebuilds** the page around it. Nothing resumes — the page is built again,
which is exactly why what you declare is what comes back:

```csharp
public sealed partial class OrdersPage(IPersistentState state) : Component
{
    private string _filter = "";

    protected override void OnMount() => state.TryGet<string>("filter", out _filter!);

    private void Search(string term)
    {
        _filter = term;
        state.Persist("filter", term);   // survives a rebuild; everything else does not
    }
}
```

Inject it through the **constructor** — a settable non-nullable property becomes a required chain
parameter ([RASK002](diagnostics.md)).

**Declare state, don't stream it.** The bag is capped at 16 KB across all keys and rides the wire inside
the render payload. Persist identifiers and selections — a filter, a wizard step, a draft — not the rows
they resolve to. Over budget the session keeps working but declares itself unresumable and falls back to
the reload it would have had anyway, and logs a warning saying so.

**Even declaring nothing is worth something.** The route alone turns a deploy from a full-page reload into
a re-render of the page the user was already on.

**What does not come back:** anything you didn't name. In-flight async work, undeclared fields, open
interop handles. And note that a value only *reads* back if the JSON still fits the type — if you change
the shape of a persisted type, change its key too, or a renamed property will read back as a default
rather than a miss.

### What makes it safe

The record lives in the browser, which is what makes it need no shared store, no sticky routing and no new
infrastructure. It is encrypted and authenticated under its own data-protection purpose, so it is opaque
and unforgeable to the client holding it. Expiry is enforced by ASP.NET's time-limited protector rather
than a field we compare, so an expired record cannot be opened at all. It carries **no principal** — a
reconnect authenticates from its session cookie exactly as before — but it is bound to the
identity it was issued to, so it cannot be replayed onto another account, and signing in or out
invalidates it. A rebuild takes a `MaxSessions` slot through the same atomic reservation a `GET` uses, so
the reconnect storm after a deploy sheds like ordinary traffic instead of walking past the cap.

> **This needs a persisted key ring.** A record sealed by the container you just replaced can only be
> opened by the replacement if both share data-protection keys. `rask new` scaffolds that (`/data/keys`,
> on the volume the deploy already mounts) — see [deployment](deployment.md#your-users-stay-signed-in-across-a-deploy).
> Without it, every record is refused after a deploy and you are back to the reload. Watch
> `rask.sessions.resume_rejected{reason="unprotect"}`: a spike right after a deploy is exactly that.

## Sizing `MaxSessions` for a memory budget

`MaxSessions` defaults to `0` (uncapped), which is only safe when something else bounds who can reach
the app. Every session pins a component tree, a DI scope, and several buffers, so an uncapped host
facing untrusted traffic can be pushed into memory exhaustion. To set the cap you need to know what a
session costs — measure it with the capacity report:

```bash
dotnet run -c Release --project tests/Rask.Benchmarks -- session-footprint
```

Measured on the framework's own data-table page (Apple M4, .NET 10, Server GC, 200 sessions per row):

| Page | Page HTML | Unconnected | Connected | Sessions per GiB |
| --- | ---: | ---: | ---: | ---: |
| Empty shell | 292 B | 11 KB | 16 KB | ~66,000 |
| 5-row table | 1 KB | 35 KB | 52 KB | ~20,300 |
| 200-row grid | 29 KB | 1.01 MB | 1.39 MB | ~735 |
| 1,000-row grid | 147 KB | 5.3 MB | 7.0 MB | ~146 |

**Page size, not user count, is what moves this** — the same host holds ~66,000 sessions of a trivial
page or ~146 of a big grid, a ~450× swing. Sessions are cheap until the page isn't. A session retains
roughly:

- **two rendered-HTML buffers** — the current render plus the last-applied baseline, used for
  dedup and the head-compare. They're `char` arrays, so a page costs ~**4 bytes of RAM per HTML
  character** across the pair, and they're rented from a pool that rounds up to a power of two.
- **two frame buffers** (~40 B per node, when the diff codec is on — it is by default) and **two
  payload buffers**.
- **the component tree.** Usually the largest term on a big page. A subtree is compacted — its element
  graph released in favour of a frame snapshot — unless it contains a nested component, so most rows
  hold a compact snapshot rather than a graph of objects.

Every one of these is rented at the size the page turns out to need, then grows to a high-water mark
and is reused, so per-session cost converges. Cost is a function of your largest page, not of uptime.

These are steady-state figures, and steady state is what a session settles into: a soak of 100 sessions
over 200 updates each holds flat to the byte, and 500 create-and-dispose cycles leave nothing behind at
all. Teardown also hands every pooled array back, so the next session reuses them instead of paying to
allocate its own — worth ~19% of the allocation a create-render-dispose cycle costs on a 200-row page.

### Fitting is not the same as serving

The table above answers how many sessions a host can *hold*. What it costs to actually *use* them is a
different question, and a capacity number you can't serve isn't a capacity number:

```bash
dotnet run -c Release --project tests/Rask.Benchmarks -- session-load
```

That drives real WebSockets against a real host and times the round trip a user actually feels — the
click, the render it causes, and the acknowledgement that closes it. Indicative figures (Apple M4, 20
concurrent sessions, closed loop):

| Page | Events/sec | p50 | p99 |
| --- | ---: | ---: | ---: |
| Empty shell | ~100,000 | 0.15 ms | ~1 ms |
| 5-row table | ~85,000 | 0.19 ms | ~1 ms |
| 200-row grid | ~26,000 | 0.53 ms | ~6 ms |

Read the shape, not the absolutes: **page size costs you throughput before it costs you memory.** An
empty shell and a 5-row page are within noise of each other; a 200-row grid costs roughly 4× the
per-event time and a quarter of the throughput, because every interaction re-renders and re-diffs the
whole page.

Two things the harness deliberately does not measure. It reports no memory — the load generator shares a
process with the host it drives, so a heap reading would count the client's own sockets; that is what
`session-footprint` is for. And it turns off `MaxInboundFramesPerSecond`, because a closed-loop generator
trips a per-connection DoS cap that no human ever will. Leave that cap on in production.

To pick a number: take the RAM you'll give the process, subtract the app's own baseline, and divide by
the connected cost of your **largest** page — then leave headroom, because `MaxSessions` also counts
sessions created by a bare `GET` whose WebSocket never arrived (they hold a slot for
`UnconnectedSessionGracePeriod`, 10 s), and because a rejected user gets a `503`.

```jsonc
// ~2 GiB of session budget for an app whose heaviest page measures ~1.34 MB connected.
{
  "Rask": {
    "Live": {
      "MaxSessions": 1200
    }
  }
}
```

Two caveats before you trust the table. These are **framework floors** — they exclude the WebSocket
transport (Kestrel adds ~32 KB of per-connection buffers) and, more importantly, your own scoped
services: one `DbContext` per session can dwarf everything above. And they're measured on one page
shape. Run the report against your own budget rather than quoting these numbers, and pair the cap with
the [live-session health check](observability.md#health-checks) so an orchestrator sheds load before
the host starts refusing sessions.

## A note on limits and reverse proxies

The frame-size / frame-rate / pending-handler caps are coarse per-connection backstops, not a
substitute for edge protection. For precise admission control and rate limiting, pair them with a
reverse proxy (nginx, Envoy, a cloud load balancer) and the `MaxSessions` cap + the
[live-session health check](observability.md#health-checks) so an orchestrator can shed load before
the host starts refusing sessions.
