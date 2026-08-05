# Rask.Dashboard

An operator dashboard for the [Rask](https://github.com/pal-tamas/rask) One Person Framework batteries.
Mounts at `/_ops` and reads the outbox, background jobs, queued mail and cache **out of your application's
own database** — the tables are already there, so there is nothing to run and nothing to export.

- **Dead letters, named as such.** Every queue is split into due / delayed / **failed** / processed, where
  failed means what the processors mean by giving up: out of attempts and still unprocessed. Processed
  climbing looks healthy while a queue retries itself to death, so the dead-letter count gets a banner of
  its own rather than a tile among tiles.
- **The error behind the row.** Expand any row for its last failure message and its stored payload.
- **Only what you run.** A panel appears only when its battery is both registered *and* mapped into the
  `DbContext`, so the nav is an inventory of this deployment rather than a menu of dead links.
- **Fix it from here.** Retry a dead letter (or all of them), purge processed rows, evict a cache key. The
  retry guard is the inverse of the processors' own drain query, so it can only match rows they've already
  given up on — a row in flight is untouchable, and no coordination with a running processor is needed.
- **Cache, logs, system and schedule.** Cache keys with sizes and expiry; a bounded in-memory tail of the
  `ILogger` pipeline (the failures that leave no row anywhere — Litestream exiting, an unregistered job
  type, handler faults); SQLite pragmas read live, database size, and the recurring-job schedule joined to
  when each job last actually fired.
- **Fail-closed by default.** See below — this matters more than any of the above.

## Use

```bash
dotnet add package Rask.Dashboard
```

```csharp
builder.Services.AddRaskDashboard<AppDbContext>();

// Who may operate the app. Without this the dashboard denies everyone outside Development.
builder.Services.AddAuthorization(o =>
    o.AddPolicy(RaskDashboardPolicies.Access, p => p.RequireRole("Admin")));
```

Then browse to `/_ops`.

## Security

The dashboard shows job payloads, stored email bodies and log lines. An open one is close to publishing
your database, so it is designed to be safe when you configure nothing:

| Situation | Result |
| --- | --- |
| You define the `RaskDashboard` policy | Your policy decides, always. |
| No policy, `Development` | Open, with a warning banner on every page. |
| No policy, anything else | **Denied for everyone**, and a warning logged at startup. |

The policy is applied to the route layout, so it covers every dashboard page — including ones added by a
future version — and is re-checked on each in-app navigation, not just the first request.

## Options

```csharp
builder.Services.AddRaskDashboard<AppDbContext>(o =>
{
    o.RefreshInterval = TimeSpan.FromSeconds(2);   // how often an open panel re-reads
    o.MaxPollDuration = TimeSpan.FromMinutes(5);   // then it parks and offers Resume
    o.PageSize        = 25;

    o.Actions         = RaskDashboardActions.Safe; // default: retry, purge, evict
    // o.Actions      = RaskDashboardActions.All;  // adds delete-a-row and flush-the-cache

    o.LogBufferSize   = 500;
    o.LogMinimumLevel = LogLevel.Information;
    // o.CaptureLogs  = false;                     // no logging provider is registered at all
});
```

Panels poll, compare, and only re-render on a real change — an idle system produces no diff and no
WebSocket traffic. The loop is bounded on purpose: every open tab is a reader competing with the
processors for SQLite's single write lock.

## Backups

The backup card is opt-in, because reading Litestream and snapshot state would otherwise force a native
SQLite provider bundle onto every consumer and tie a provider-agnostic dashboard to SQLite. Implement
`IDashboardBackupProbe` (about ten lines over `LitestreamStatus.Current` and
`ISqliteSnapshotStore.ListAsync`) and register it to light the card up.

Full documentation: <https://github.com/pal-tamas/rask/blob/main/docs/dashboard.md>
