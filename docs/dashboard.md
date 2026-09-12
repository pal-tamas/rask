# The batteries dashboard

Every DB-backed pillar keeps its state in a table in your application's own database. That is what makes
`Rask.Dashboard` possible: one package reference and one line mounts an operator dashboard at `/_rask` over
the outbox, background jobs, queued mail, cache and stored files — no exporter, no second datastore, no agent.

> Included in the [`Rask`](../README.md) package — nothing to install. It is **on**; an app that does without it says so:
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
does — the `server` template, and a host that serves a single-page app (a Rask WebAssembly app or a
TypeScript front end) through [`UseRaskSpa`](spa.md#a-rask-webassembly-app).

A SPA host normally runs no components at all: it serves the app and an API. Mounting the dashboard gives
it exactly one server-rendered route chain, scoped so the client keeps everything else:

```csharp
builder.Services.AddRaskServer();       // the live runtime, for the dashboard's pages
builder.Services.AddRaskSpaHost();      // compression for the app's files

// …

app.UseRaskServer<RaskDashboardShell>("/_rask/{**path}");   // the dashboard, server-rendered
app.UseRaskSpa();                                           // the app, everywhere else
```

`rask new --ops` writes all of it on a server app, including the database the panels read.

Two details are worth knowing if you assemble this by hand. The SPA's fallback is the lowest precedence
there is, so mounting the dashboard above it claims the dashboard's routes without taking any of the
client's. And both halves want `/_rask/a/{hash}` — the dashboard's scoped styles and a WebAssembly app's
baked ones — so the dashboard's endpoint answers a hash its own process never registered from the web
root, where `UseRaskSpa` puts the app's.

`RaskDashboardShell` is the root the pages render through: a host serving a SPA has no component of its
own for `UseRaskServer<TApp>` to name.

## What it shows

| Panel | Answers |
| --- | --- |
| **Overview** | Is anything wrong? One tile per queue, plus a banner the moment any dead letter exists. |
| **Queues** — outbox / jobs / mail | Due, delayed, **failed**, processed, as a row of counts that is also the filter. Open a row for its last error and stored payload. |
| **Cache** | Keys, sizes, expiry, and how many are expired but not yet swept. |
| **Storage** | At `/_rask/storage`, read-only: how many files and bytes [`Rask.Storage`](file-storage.md) holds, how many are public, usage per provider, and a searchable list of recent files — plus a notice when files are on disk, which no backup covers. |
| **Logs** | A live tail of the `ILogger` pipeline — the failures that leave no row anywhere — plus a searchable **History** over the stored log when [`Rask.Logging`](logging.md) is installed. |
| **System** | SQLite pragmas read live, database size, and the recurring-job schedule with when each last fired. |

A panel appears **only when its battery is registered and its table is mapped**. An app with jobs and
nothing else gets a jobs panel and no empty placeholders — the nav is an inventory of what this deployment
actually runs.

The chrome is two rows: a **breadcrumb bar** saying what you are looking at, over a **tab bar** saying
which part of the console you are in. Every queue shares the one `Queues` tab, and the breadcrumb carries
a switcher between them — so a deployment running all three does not spend half its navigation on them.
The switcher is a plain `<select>`: the console ships no JavaScript, so it is keyboard-navigable for free
and opens the platform's own picker on a phone.

It is built mobile-first. Below `sm` every table stacks each row into labelled lines rather than scrolling
sideways — a table you have to swipe has hidden the column you came for — and from `sm` up a secondary
column waits until the table has room for it. A long list pages through a window of page numbers, so nothing
on a phone is wider than the screen.

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
the live runtime owns `/_rask/auth/redeem`, `/_rask/upload/{id}` and `/_rask/download/{id}/{token}`, and
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
| No policy, any other environment | **Denied for everyone**, and a warning logged at startup. |

Defining the policy is the only thing standing between an operator surface and the internet, so nothing
about it is inferred: forget it and you get 403, never an open dashboard.

```csharp
// Roles, claims, a specific user list — it is an ordinary ASP.NET policy.
builder.Services.AddAuthorization(o =>
    o.AddPolicy(RaskDashboardPolicies.Access, p => p.RequireRole("Admin")));
```

> **`AllowAnonymousAccess` is configuration too.** `Rask:Dashboard:AllowAnonymousAccess` set to `true`
> opens the console to everyone, in every environment, and like every `Rask:Dashboard` key it can arrive as
> an environment variable (`Rask__Dashboard__AllowAnonymousAccess=true`). The panels show job payloads,
> stored email bodies and log lines, so guard the deploy environment's variables as carefully as the code.

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

Every dashboard option is `Rask:Dashboard` in `appsettings.json`; a callback —
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
| **History** | The durable store, paged, with level/category filters and a full-text search | Yes | One query per refresh, against the log store's **own** SQLite file — never the application database |

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
    public Task<BackupReplicationInfo?> ReplicationAsync(CancellationToken ct)
    {
        if (litestream is null)
        {
            return Task.FromResult<BackupReplicationInfo?>(null);
        }

        var s = litestream.Current;
        return Task.FromResult<BackupReplicationInfo?>(
            new(s.IsReplicating, s.LastStartedAt, s.RestartCount, s.LastError));
    }

    public async Task<IReadOnlyList<BackupSnapshotInfo>> SnapshotsAsync(CancellationToken ct) =>
        snapshots is null
            ? []
            : [.. (await snapshots.ListAsync(ct)).Select(s => new BackupSnapshotInfo(s.Name, s.SizeBytes, s.CreatedAt))];
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

The console is drawn with [the UI kit](ui-kit.md) and nothing else. Every page is `Rask.Ui` components,
and the only stylesheet in its document is the kit's, inlined by the layout. It writes no class strings
at all, for a reason that applies to any library: Tailwind emits a utility only where it can see the class
name in the source it scans, and the kit's sheet is compiled from the kit's source — so a `.Class("mt-4")`
written in `Rask.Dashboard` would render as nothing. `DashboardIsKitOnlyTests` fails on any `.Class(…)`,
`.Style(…)` or `UiStyles.` in the package. Where a page needs something the kit cannot draw, the kit grows
a typed step instead: that is where `UiDataGrid`'s `ShowFrom`, `RowTone` and `PageHref`, `UiCard.Href`
and `UiEmpty` came from.

The console owns its whole document, so it needs a page reset the way any application does. That travels
in the kit's sheet as well, keyed to the class `UiShell` writes (`.rask-ops`), so an application that links
the kit is untouched by it.

**It is pinned to daisyUI's `light` theme, and that is not configurable.** The theme scope goes on
`<html>` with an explicit `data-theme`, and `UiShell` names the same theme, so the console ignores both the
host application's theme and the reader's `prefers-color-scheme`. An operator surface is a set of contrast
ratios checked against one ground; letting it follow the OS would move every one of them silently.

It is not a hypothetical, either — it is what the console did before this was enforced. Its own
palette was a fixed light one while `UiShell` painted with daisyUI's, so on a machine set to dark mode
the chrome and the cards went dark and every label on them stayed near-black: the queue titles on the
overview measured **1.09:1**, with every class name in the markup correct and the whole unit suite
green.

Your own pages are unaffected: the console is a mounted application with its own document, so its
stylesheet, its reset and its theme reach nothing of yours.

## Related

- [Observability](observability.md) — logging categories, the `Rask.Server` meter, tracing, health checks.
- [The UI kit](ui-kit.md) — the daisyUI components the console is drawn with, and the theme scope.
- [Jobs](jobs.md) · [Outbox](outbox.md) · [Mail](mail.md) · [Cache](cache.md) · [File storage](file-storage.md) — the pillars it reads.
- [SQLite](sqlite.md) — pragmas, continuous backup, and snapshots.
