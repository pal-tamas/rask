# The batteries dashboard

Every DB-backed pillar keeps its state in a table in your application's own database. That is what makes
`Rask.Dashboard` possible: one package reference and one line mounts an operator dashboard at `/_rask` over
the outbox, background jobs, queued mail and cache — no exporter, no second datastore, no agent.

```bash
dotnet add package Rask.Dashboard
```

```csharp
builder.Services.AddRaskDashboard<AppDbContext>();

// Who may operate the app. Without this, /_rask denies everyone outside Development.
builder.Services.AddAuthorization(o =>
    o.AddPolicy(RaskDashboardPolicies.Access, p => p.RequireRole("Admin")));
```

`rask new` wires both lines for you — the dashboard is on by default (`--no-ops` leaves it out).

## Where it can run

The dashboard is server-rendered and reads your database directly, so it lives wherever your ASP.NET host
does — the `server` template, and the `.Server` project of a client-plus-host solution.

In that shape the host normally runs no components at all: it serves a WASM bundle and an API. Mounting
the dashboard gives it exactly one server-rendered route chain, scoped so the client keeps everything else:

```csharp
builder.Services.AddRaskServer();       // the live runtime, for the dashboard's pages
builder.Services.AddRaskWasmHost();     // compression for the published bundle

// …

app.UseRaskServer<RaskDashboardShell>("/_rask/{**path}");   // the dashboard, server-rendered
app.UseRaskWasmHost();                                      // the SPA, everywhere else
```

`rask new --ops` writes all of it on a server app, including the database the panels read.

Two details are worth knowing if you assemble this by hand. The calls are spelled `AddRaskServer` /
`AddRaskWasmHost` rather than `AddRask` because **both** packages declare an `AddRask` on
`IServiceCollection`, and C# does not report an ambiguity — the WASM host's takes no optional parameters
and the server's takes two, so the tie-break silently picks the WASM one and the app starts with no live
runtime, failing on its first request. And the SPA is a `MapFallback`, the lowest precedence there is, so
mounting the dashboard above it claims the dashboard's routes without taking any of the client's.

`RaskDashboardShell` is the root the pages render through: a host serving a WASM bundle has no component
of its own for `UseRaskServer<TApp>` to name.

## What it shows

| Panel | Answers |
| --- | --- |
| **Overview** | Is anything wrong? One tile per queue, plus a banner the moment any dead letter exists. |
| **Queues** — outbox / jobs / mail | Due, delayed, **failed**, processed. Expand a row for its last error and stored payload. |
| **Cache** | Keys, sizes, expiry, and how many are expired but not yet swept. |
| **Logs** | A live tail of the `ILogger` pipeline — the failures that leave no row anywhere — plus a searchable **History** over the stored log when [`Rask.Logging`](logging.md) is installed. |
| **System** | SQLite pragmas read live, database size, and the recurring-job schedule with when each last fired. |

A panel appears **only when its battery is registered and its table is mapped**. An app with jobs and
nothing else gets a jobs panel and no empty placeholders — the nav is an inventory of what this deployment
actually runs.

### Failed is the number that matters

Delivery is at-least-once with backoff. A queue that retries itself to death still has a healthy-looking
*processed* count, so the dashboard treats dead letters as the headline: **processed climbing is good,
failed above zero is the alert.**

"Failed" means exactly what the processors mean by giving up — `ProcessedAt IS NULL AND Attempts >=
MaxAttempts`, the inverse of their own drain query. It is not a status column; there isn't one.

## Security

The dashboard shows job payloads, stored email bodies and log lines. Treat `/_rask` as a view of your
database, because that is what it is.

`/_rask` is the framework's own reserved prefix — scoped assets are served from `/_rask/a/{hash}.{ext}`,
and the live runtime owns `/_rask/auth/redeem`, `/_rask/upload/{id}` and `/_rask/download/{id}/{token}`.
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

## Actions

Reading tells you what broke; these fix it.

| Action | Tier | Guard |
| --- | --- | --- |
| Retry a dead letter, retry all | Safe | `ProcessedAt IS NULL AND Attempts >= MaxAttempts` |
| Purge processed rows | Safe | `ProcessedAt IS NOT NULL AND ProcessedAt < cutoff` |
| Evict a cache key | Safe | key match |
| Delete an outstanding row | Destructive | `ProcessedAt IS NULL` |
| Flush the whole cache | Destructive | — |

```csharp
builder.Services.AddRaskDashboard<AppDbContext>(o => o.Actions = RaskDashboardActions.All);
```

`Actions` defaults to `Safe`. Buttons for a tier that is off are hidden, not disabled.

**Why retry is safe against a live queue.** Its guard is the inverse of the drain query, so it can only
ever match rows a processor has already given up on — a row currently in flight is invisible to it. Every
action is a single `ExecuteUpdate`/`ExecuteDelete` with the guard in the `WHERE` clause, evaluated by the
database at the moment of the write rather than by the page when it rendered. Retry resets `Attempts` to 0,
sets `RunAt` to now, and clears the stale `Error` so it doesn't read as a fresh failure.

Purge only ever matches processed rows, so outstanding work **and dead letters** survive it whatever cutoff
you pass.

## Logs

```csharp
builder.Services.AddRaskDashboard<AppDbContext>(o =>
{
    o.LogBufferSize   = 500;
    o.LogMinimumLevel = LogLevel.Information;
    // o.CaptureLogs  = false;   // registers no logging provider at all
});
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
builder.Services.AddRaskLogging(
    builder.Configuration.GetConnectionString("Logs") ?? "Data Source=logs.db");
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
> `rask new` scaffolds: with no `Litestream:ReplicaUrl` set it never runs, so `LitestreamStatus` is not in
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

```csharp
builder.Services.AddRaskDashboard<AppDbContext>(o =>
{
    o.RefreshInterval = TimeSpan.FromSeconds(5);
    o.MaxPollDuration = TimeSpan.FromMinutes(10);
    o.PageSize        = 50;
});
```

## Related

- [Observability](observability.md) — logging categories, the `Rask.Server` meter, tracing, health checks.
- [Jobs](jobs.md) · [Outbox](outbox.md) · [Mail](mail.md) · [Cache](cache.md) — the pillars it reads.
- [SQLite](sqlite.md) — pragmas, continuous backup, and snapshots.
