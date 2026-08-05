# The batteries dashboard

Every DB-backed pillar keeps its state in a table in your application's own database. That is what makes
`Rask.Dashboard` possible: one package reference and one line mounts an operator dashboard at `/_ops` over
the outbox, background jobs, queued mail and cache — no exporter, no second datastore, no agent.

```bash
dotnet add package Rask.Dashboard
```

```csharp
builder.Services.AddRaskDashboard<AppDbContext>();

// Who may operate the app. Without this, /_ops denies everyone outside Development.
builder.Services.AddAuthorization(o =>
    o.AddPolicy(RaskDashboardPolicies.Access, p => p.RequireRole("Admin")));
```

`rask new --ops` (and `--all-batteries`) wires both lines for you.

## What it shows

| Panel | Answers |
| --- | --- |
| **Overview** | Is anything wrong? One tile per queue, plus a banner the moment any dead letter exists. |
| **Queues** — outbox / jobs / mail | Due, delayed, **failed**, processed. Expand a row for its last error and stored payload. |
| **Cache** | Keys, sizes, expiry, and how many are expired but not yet swept. |
| **Logs** | A tail of the `ILogger` pipeline — the failures that leave no row anywhere. |
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

The dashboard shows job payloads, stored email bodies and log lines. Treat `/_ops` as a view of your
database, because that is what it is.

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

**It is a tail, not a log store.** Memory-only, bounded by count, gone on restart. Point a real logging
provider somewhere durable for anything you need to keep, and remember that log lines can contain secrets —
another reason the policy is fail-closed.

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
