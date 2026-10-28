# The batteries dashboard

Every DB-backed pillar keeps its state in a table in your application's own database. That is what makes
`Rask.Dashboard` possible: one package reference and one line mounts an operator dashboard at `/_rask` over
the outbox, background jobs, queued mail, cache and stored files — no exporter, no second datastore, no agent.

> Included in [`Rask.Server`](../README.md) — nothing to install. It is **on**; an app that does without it says so:
>
> ```csharp
> app.Configure(c => c.Ops.Off());
> ```

```csharp
builder.Services.AddRaskDashboard<AppDbContext>();

// Who may operate the app. Without this, /_rask denies everyone outside Development.
builder.Services.AddAuthorization(o =>
    o.AddPolicy(RaskDashboardPolicies.Access, p => p.RequireRole("Admin")));
```

`rask new` wires both lines for you — the dashboard is on by default (`--no-ops` leaves it out).

## Where it can run

The dashboard is server-rendered and reads your database directly, so it lives wherever your ASP.NET host
does — the `server` template, and a host that serves a Rask WebAssembly app through
[`MapRaskSpa`](deployment.md#serving-a-webassembly-app).

A WebAssembly host normally runs no components at all: it serves the app and an API. Mounting the dashboard gives
it exactly one server-rendered route chain, scoped so the client keeps everything else:

```csharp
builder.Services.AddRaskServer();       // the live runtime, for the dashboard's pages
builder.Services.AddRaskSpaHost();      // compression for the app's files

// …

app.MapRaskServer<RaskDashboardShell>("/_rask/{**path}");   // the dashboard, server-rendered
app.MapRaskSpa();                                           // the app, everywhere else
```

`rask new` turns all of it on for a server app (`--no-ops` leaves it out), including the database the panels read. A wasm-hosted
app gets it from `RaskApp.Create(args).Serve()`, which mounts the dashboard under `/_rask` and maps the
browser app's fallback after it; the lines above are the same thing by hand.

Two details are worth knowing if you assemble this by hand. The app's fallback is the lowest precedence
there is, so mounting the dashboard above it claims the dashboard's routes without taking any of the
client's. And both halves want `/_rask/a/{hash}` — the dashboard's scoped styles and a WebAssembly app's
baked ones — so the dashboard's endpoint answers a hash its own process never registered from the web
root, where `MapRaskSpa` puts the app's.

`RaskDashboardShell` is the root the pages render through: a host serving a WebAssembly app has no component of its
own for `MapRaskServer<TApp>` to name.

## What it shows

| Panel | Answers |
| --- | --- |
| **Overview** | Is anything wrong? One card per queue, plus a banner the moment any dead letter exists. |
| **Queues** — outbox / jobs / mail | Due, delayed, **failed**, processed, as a navbar of counts that is also the filter. Open a row for its last error and stored payload. |
| **Cache** | Keys, sizes, expiry, and how many are expired but not yet swept. |
| **Storage** | At `/_rask/storage`, read-only: how many files and bytes [`Rask.Storage`](file-storage.md) holds, how many are public, usage per provider, and a searchable list of recent files — plus a notice when files are on disk, which no backup covers. |
| **Logs** | A live tail of the `ILogger` pipeline — the failures that leave no row anywhere — plus a searchable **History** over the stored log when [`Rask.Logging`](logging.md) is installed. |
| **System** | SQLite pragmas read live, database size, and the recurring-job schedule with when each last fired. |

A panel appears **only when its battery is registered and its table is mapped**. An app with jobs and
nothing else gets a jobs panel and no empty placeholders — the nav is an inventory of what this deployment
actually runs.

The frame is Flux's sidebar layout: the sections down the side, and a header that, on a queue, carries a
**breadcrumb** saying which one — `Ui.Sidebar`, `Ui.Header` and `Ui.Breadcrumbs`, Flux UI's pieces from the
kit. Every queue shares the one `Queues` item, and the breadcrumb's last step is a dropdown of the queues
beside the one you are reading — so a deployment running all three does not spend half its navigation on them.

Every list is a Flux table (`Ui.Table` with `Ui.Pagination` under it), with a width on every column but one, so a
page or a filter that brings other rows moves no column, and a long value ends in an ellipsis with the whole of it
in its tooltip. On a phone the sidebar slides over the page and a table scrolls inside its own box, so nothing is
wider than the screen.

### Failed is the number that matters

Delivery is at-least-once with backoff. A queue that retries itself to death still has a healthy-looking
*processed* count, so the dashboard treats dead letters as the headline: **processed climbing is good,
failed above zero is the alert.**

"Failed" means exactly what the processors mean by giving up — `ProcessedAt IS NULL AND Attempts >=
MaxAttempts`, the inverse of their own drain query. It is not a status column; there isn't one.

## Security

The dashboard shows job payloads, stored email bodies, log lines and the names of uploaded files. Treat
`/_rask` as a view of your database, because that is what it is.

`/_rask` is the framework's own reserved prefix — scoped assets are served from `/_rask/a/{hash}.{ext}`,
the live runtime owns `/_rask/auth/redeem`, `/_rask/upload`, `/_rask/auth/who` and `/_rask/download/{token}`, and
[`Rask.Storage`](file-storage.md) owns `/_rask/files/public/{id}` and `/_rask/files/{token}`.
Those are literal routes and the dashboard's pages resolve through a catch-all, so they coexist by
ordinary routing precedence and none of them shadows an application route of yours.

Pages are gated on the `RaskDashboard` policy, applied to the route **layout** — so it protects every page
including ones a future version adds, and is re-checked on each in-app navigation rather than only the
first request.

| Situation | Result |
| --- | --- |
| You define the `RaskDashboard` policy | Your policy decides. |
| No policy, `Development` | Open, with a warning banner on every page. |
| No policy, any other environment | **Denied for everyone**, whatever `AllowAnonymousAccess` says. |

Defining the policy is the only thing standing between an operator surface and the internet, so nothing
about it is inferred: forget it and you get 403, never an open dashboard.

```csharp
// Roles, claims, a specific user list — it is an ordinary ASP.NET policy.
builder.Services.AddAuthorization(o =>
    o.AddPolicy(RaskDashboardPolicies.Access, p => p.RequireRole("Admin")));
```

> **`AllowAnonymousAccess` opens nothing outside Development.** The console is for whoever operates the app,
> and its panels show job payloads, stored email bodies and log lines. `Rask:Ops:AllowAnonymousAccess` —
> which, like every `Rask:Ops` key, can arrive as an environment variable
> (`Rask__Ops__AllowAnonymousAccess=true`) — is honoured in Development only, where the console is open
> already. In every other environment, and on a host that names none, it is ignored: the console requires
> the `RaskDashboard` policy, and the host logs one warning at startup saying the setting was not honoured.
> An internal tool that used it to skip the policy defines one instead, `p => p.RequireAssertion(_ => true)`
> if it truly means everyone.

## Actions

Reading tells you what broke; these fix it.

| Action | Tier | Guard |
| --- | --- | --- |
| Retry a dead letter, retry all | Safe | `ProcessedAt IS NULL AND Attempts >= MaxAttempts` |
| Purge processed rows | Safe | `ProcessedAt IS NOT NULL AND ProcessedAt < cutoff` |
| Evict a cache key | Safe | key match |
| Delete an outstanding row | Destructive | `ProcessedAt IS NULL` |
| Flush the whole cache | Destructive | — |

```jsonc
{
  "Rask": {
    "Dashboard": {
      "Actions": "All"
    }
  }
}
```

Every dashboard option is `Rask:Ops` in `appsettings.json`; a callback —
`AddRaskDashboard<AppDbContext>(o => o.Actions = RaskDashboardActions.All)` — runs after the section and
wins. `Actions` defaults to `Safe`. Buttons for a tier that is off are hidden, not disabled.

**Why retry is safe against a live queue.** Its guard is the inverse of the drain query, so it can only
ever match rows a processor has already given up on — a row currently in flight is invisible to it. Every
action is a single `ExecuteUpdate`/`ExecuteDelete` with the guard in the `WHERE` clause, evaluated by the
database at the moment of the write rather than by the page when it rendered. Retry resets `Attempts` to 0,
sets `RunAt` to now, and clears the stale `Error` so it doesn't read as a fresh failure.

Purge only ever matches processed rows, so outstanding work **and dead letters** survive it whatever cutoff
you pass.

## Logs

```jsonc
{
  "Rask": {
    "Dashboard": {
      "LogBufferSize": 500,
      "LogMinimumLevel": "Information"
      // "CaptureLogs": false      // the logging provider drops everything
    }
  }
}
```

A bounded in-memory ring buffer fed by a registered `ILoggerProvider`, so it sees exactly what every other
sink sees. That is the point: the failures this dashboard exists for often leave no row in any table —
Litestream exiting, a job type that won't deserialize, a handler that threw.

**On its own, it is a tail rather than a log store.** Memory-only, bounded by count, gone on restart. Log
lines can contain secrets, which is another reason the policy is fail-closed.

Install [`Rask.Logging`](logging.md) and the page grows a second mode:

| Mode | Reads | Survives a restart | Cost |
| --- | --- | --- | --- |
| **Live** (default) | The in-memory buffer above | No | None — no query at all, and it renders on a real log line rather than a timer |
| **History** | The durable store, paged, with level/category filters and an indexed substring search | Yes | One query per refresh, against the log store's **own** SQLite file — never the application database |

```csharp
builder.Services.AddRaskLogging();   // opens Rask:ConnectionStrings:Logs
```

They are two modes rather than one merged view because the store's writer flushes on an interval: the newest
lines are in the buffer but not yet on disk, and a merged view would quietly disagree with itself for a second
at a time.

> **`LogMinimumLevel` is a floor, not an override.** The logging pipeline applies your
> `Logging:LogLevel` configuration *first*, so an entry filtered there never reaches the dashboard however
> low you set this. The scaffolded `appsettings.Production.json` sets `"Default": "Warning"` — so a
> production app shows warnings and errors here and no `Information`, which is correct rather than broken.
> Lower the level in configuration if you want more.

## Backups

The backup card is opt-in. Reading Litestream and snapshot state directly would pull a native SQLitePCLRaw
provider bundle into every consumer and tie a provider-agnostic dashboard to SQLite, so instead you supply
a probe:

```csharp
// Both dependencies are OPTIONAL on purpose — see the warning below.
public sealed class BackupProbe(LitestreamStatus? litestream = null, ISqliteSnapshotStore? snapshots = null)
    : IDashboardBackupProbe
{
    public Task<BackupReplicationInfo?> Replication(CancellationToken ct)
    {
        if (litestream is null)
        {
            return Task.FromResult<BackupReplicationInfo?>(null);
        }

        var s = litestream.Current;
        return Task.FromResult<BackupReplicationInfo?>(
            new(s.IsReplicating, s.LastStartedAt, s.RestartCount, s.LastError));
    }

    public async Task<IReadOnlyList<BackupSnapshotInfo>> Snapshots(CancellationToken ct) =>
        snapshots is null
            ? []
            : [.. (await snapshots.List(ct)).Select(s => new BackupSnapshotInfo(s.Name, s.SizeBytes, s.CreatedAt))];
}

builder.Services.AddSingleton<IDashboardBackupProbe, BackupProbe>();
```

> **Take those dependencies as optional.** `AddRaskSqliteLitestream` is config-gated in everything
> `rask new` scaffolds: with no `Rask:Litestream:ReplicaUrl` set it never runs, so `LitestreamStatus` is not in
> the container. A probe that requires it starts cleanly and then throws the first time somebody opens the
> System panel — a failure that shows up only in the environment which skipped the configuration.

Without a probe the card stays hidden — reporting "no backups" when the app simply never said is a claim
the dashboard can't support.

## Cost

Panels poll on `RefreshInterval` (default 2s), compare the reading with the previous one, and re-render
only on a real change — an idle system produces no diff and no WebSocket traffic at all.

The loop is **bounded** (`MaxPollDuration`, default 5 minutes) and then offers a Resume button. That bound
is deliberate: every open tab is a reader competing with the processors for SQLite's single write lock, so
a dashboard left open on a wall display is a real cost, not a free convenience.

```jsonc
{
  "Rask": {
    "Dashboard": {
      "RefreshInterval": "00:00:05",
      "MaxPollDuration": "00:10:00",
      "PageSize": 50
    }
  }
}
```

## How it looks, and why you cannot change it

The console is drawn with Flux UI's pieces from [the UI kit](ui-kit.md) and nothing else of the kit's: tables,
cards, headings, badges, callouts, the navbar, the sidebar layout. `DashboardIsKitOnlyTests` reads the package's
source and fails on any kit component that is not in Flux's catalogue. Its own layout — a grid of cards, the gap
between two buttons, the box a payload scrolls in — is written in Tailwind beside them, and the only stylesheet in
its document is the console's own: the kit, compiled by the console's own Tailwind build the way
[an app's is](tailwind.md#what-a-new-project-starts-with) (`Styles/dashboard.css`), embedded and inlined by the
layout.

The console owns its whole document, so it needs a page reset the way any application does. That travels
in the kit's sheet as well, keyed to the attribute a kit-only document writes on its `<html>`
(`UiStylesheet.DocumentAttribute`), so an application that links the kit is untouched by it. The
console's sheet imports Tailwind's theme and utilities and not its preflight.

**Dark mode is Flux's.** The layout's head carries `Ui.AppearanceScript`, which puts `dark` on `<html>` before
the first paint from the operator's stored choice (`Rask.appearance`, kept per browser) or, until they make one,
their system's. Every piece the console draws has its dark inks, so it reads the same in either.

Your own pages are unaffected: the console is a mounted application with its own document, so its
stylesheet, its reset and its theme reach nothing of yours. The same goes for its pages. A live page of
your app never renders one of the console's routes in place, and the console never renders one of yours.
A link or a back-button step from one to the other loads the destination as a full page, so each
application's own root always draws it.

The console's assembly carries `[assembly: MountOnlyRoutes]` (from `Rask.Core.Routing`), which is what keeps
its pages out of your app's own route table even though every server app now carries the assembly: an app
that turned the battery off answers `/_rask/…` with its 404, not with a page whose policy was never
registered. A package that ships a mounted surface of its own marks its assembly the same way.

## Related

- [Observability](observability.md) — logging categories, the `Rask.Server` meter, tracing, health checks.
- [The UI kit](ui-kit.md) — the Flux UI components the console is drawn with, and dark mode.
- [Jobs](jobs.md) · [Outbox](outbox.md) · [Mail](mail.md) · [Cache](cache.md) · [File storage](file-storage.md) — the pillars it reads.
- [SQLite](sqlite.md) — pragmas, continuous backup, and snapshots.
