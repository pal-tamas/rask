# Rask.Logging — a durable log store on your database

> **In practice:** the [dashboard](dashboard.md)'s Logs page reads it · [observability](observability.md) ·
> [deployment](deployment.md#the-log-store).

`Rask.Logging` keeps the application's log in a SQLite file of its own — no agent, no hosted log service. It
registers a standard **`ILoggerProvider`**, so it captures exactly what every other sink sees, buffers entries
through a bounded channel that never blocks the caller, and writes them in batches on a background service.
Retention is enforced by **age** and by **row count**.

```bash
dotnet add package Rask.Logging
```

## Why a log store at all

The failures that matter most leave no row in any table. Litestream exiting, a job type that won't
deserialize, a handler that threw on the one request that mattered — those are log lines, and on a single box
they live in a container's stdout that the next restart takes with it. `stdout` is a fine transport and a
terrible archive.

The dashboard has always had a **live tail**, but it says so plainly: it is bounded by count and gone on
restart. This is the other half — the log you can still read tomorrow, and search.

## Use

```csharp
// Program.cs
builder.Services.AddRaskLogging(
    builder.Configuration.GetConnectionString("Logs") ?? "Data Source=logs.db");
```

That is the whole setup. The schema is created on first use, so **there is no migration to add** — unlike the
other database-backed pillars, this one doesn't touch your `DbContext`.

`rask new MyApp --logs` scaffolds exactly the line above (and `--all-batteries` includes it).

### Options

```csharp
builder.Services.AddRaskLogging(connectionString, o =>
{
    o.MinimumLevel = LogLevel.Warning;       // a floor, not an override — see below
    o.Retention    = TimeSpan.FromDays(30);  // TimeSpan.Zero keeps entries forever
    o.MaxRows      = 250_000;                // 0 removes the cap
    o.FlushInterval = TimeSpan.FromSeconds(1);
    o.BatchSize     = 500;
    o.QueueCapacity = 10_000;
    o.ExcludedCategories.Add("Microsoft.AspNetCore.");

    o.CaptureScopes        = true;  // store ambient ILogger.BeginScope state (default)
    o.MaxScopeValues       = 16;    // per entry
    o.MaxScopeValueLength  = 256;   // per value
});
```

### Scopes

Whatever an `ILogger.BeginScope` was opened with is stored alongside the entry, so the log answers *"what
else happened on that request?"* instead of leaving it to be reconstructed from message text:

```csharp
using (logger.BeginScope("request {RequestId} for {UserId}", requestId, user.Id))
{
    logger.LogInformation("charging {Amount}", amount);   // stored with RequestId and UserId
}
```

Nested scopes are flattened outermost-first; a scope opened with a bare value (`BeginScope("checkout")`) is
stored under the key `Scope`. The message template itself is not stored — only the values it formats.

The cost sits where it has to: flattening happens at the log call, because scope state is short-lived and
may be reused the moment the scope closes. Encoding it to JSON does not — the writer does that on its own
thread, so the request path keeps the "a log call never waits on the disk" property. Both bounds above exist
so a runaway loop of nested scopes, or one large object's `ToString()`, cannot grow a row without limit.

Turn `CaptureScopes` off if your scopes carry values you would rather not have at rest. Note the same
caveat as the rest of the store: **log lines can contain secrets** — treat the file as sensitive.

### Keeping the noise down

An EF Core app logs **every SQL command** at `Information`, so on the defaults that is most of what the store
will contain. Two levers, and the first is usually the right one:

```jsonc
// appsettings.json — filters before the store ever sees the entry
"Logging": { "LogLevel": { "Microsoft.EntityFrameworkCore.Database.Command": "Warning" } }
```

```csharp
// …or skip a category for this sink only, leaving your console output alone
o.ExcludedCategories.Add("Microsoft.EntityFrameworkCore.Database");
```

> **`MinimumLevel` is a floor, not an override.** The logging pipeline applies your `Logging:LogLevel`
> configuration *first*, so an entry filtered there never reaches the store however low you set this. If the
> store looks emptier than you expect, check `appsettings.Production.json` before you check this.

### Reading it back

```csharp
public sealed partial class IncidentPage(ILogStore store) : Component
{
    // …
    var page = await store.QueryAsync(new LogQuery
    {
        MinimumLevel = LogLevel.Error,
        Category = "Shop.Checkout",
        Search = "declined",
        ScopeKey = "RequestId",       // entries captured under this scope key…
        ScopeValue = "0HN7…",         // …with this value. Key alone finds every entry that carried it.
        From = DateTimeOffset.UtcNow.AddHours(-6),
        Page = 1,
        PageSize = 50,
    });
}
```

Each `LogRecord` carries its `Scopes` as key/value pairs. The scope filter matches the stored key exactly
(via SQLite's `json_extract`) rather than searching the row as text, so a request id cannot match an entry
that merely mentioned it in a message.

`LogPage` carries the matching entries (newest first), the `TotalCount` behind the filter, and a `PageCount`.
`ILogStore` also exposes `CategoriesAsync()`, `CountAsync()`, `PurgeAsync(retention, maxRows)` and
`ClearAsync()`.

## How it works

| Step | What happens |
| --- | --- |
| A log call | `ILogger` → the registered provider → a **bounded in-memory channel**. Never blocks, never throws. |
| The buffer fills | The entry is **dropped** and counted on `rask.logs.dropped`. |
| Every `FlushInterval` | A background writer drains the channel and inserts up to `BatchSize` rows per transaction. |
| Shutdown | The writer drains what's buffered, bounded by `ShutdownDrainTimeout` (5s). |
| Every `PurgeInterval` | Entries older than `Retention` go, then the store is trimmed to the newest `MaxRows` — deleted in pages of 1,000 so the write lock is never held for a whole sweep. |

**Dropping is the design, not a bug.** A log call happens on whatever thread is serving a request, and the one
thing it must never do is wait for a disk write. Unbounded buffering would trade a visible drop count for an
invisible memory leak under exactly the log storm that causes it. A batch lost to a failed write is counted
the same way, so `rask.logs.dropped` stays the single honest answer to *"is the log I'm reading complete?"*

### Its own file, on purpose

Every other database-backed pillar (`AddRaskJobs<TContext>`, `AddRaskOutbox<TContext>`, …) maps an entity onto
*your* `DbContext` and rides your migrations. This one deliberately doesn't:

- **Write frequency.** Jobs and emails arrive at human rates; log lines arrive at machine rates. Routing them
  through your `DbContext` would put a high-frequency writer on the same single SQLite write lock your request
  path and processors already contend for.
- **Transaction entanglement.** The most valuable log line is the one written *while a transaction is
  failing*. On your context that line rolls back with the failure — the store would lose exactly what it
  exists to keep.
- **Migration churn.** A framework-owned append-only table has no business in your migration history.

The trade-off is real and worth stating: **`logs.db` is not covered by `rask db backup` or Litestream.** That
is deliberate — logs are expendable and high-churn, and keeping them out of `app.db` keeps snapshots and WAL
replication cheap. If you need the log archived, copy the file like any other artifact.

> **Log lines contain secrets.** This feature writes them to a file that outlives the process. Treat `logs.db`
> as sensitive: keep it off public volumes, and remember that anyone who can read it can read whatever your
> application logged.

## In the dashboard

With `Rask.Dashboard` installed, `/_rask/logs` gains a **History** mode beside the existing **Live** tail:
paged, filterable by level and category, with a full-text search over the message and the exception. Live
stays the default and still reads nothing from disk.

Two modes rather than one merged view because the writer flushes on an interval — the newest lines are in the
buffer but not yet on disk, and a merged view would quietly disagree with itself for a second at a time.

## Metrics

On the `Rask.Logging` meter (`LogMetrics.MeterName`):

| Instrument | Meaning |
| --- | --- |
| `rask.logs.written` | Entries persisted. |
| `rask.logs.dropped` | Entries that never reached the store — a full buffer, or a batch whose write failed. **The number worth alerting on.** |
| `rask.logs.purged` | Entries removed by retention. |
| `rask.logs.stored` | Entries currently held. Sampled by the writer's sweep, not on every observation. |

```bash
dotnet-counters monitor --counters Rask.Logging
```

## Deploying

`rask deploy` sets `ConnectionStrings__Logs=Data Source=/data/logs.db`, on the same named volume as the
application database — without it the log would land in the container's writable layer and be destroyed by the
very restart it exists to survive.

## Notes

- **One writer per app.** SQLite is single-writer; run one process against a given `logs.db`, exactly as with
  the jobs and outbox processors.
- **Trim / AOT safe.** No reflection. Scope state is encoded with a source-generated
  `JsonSerializerContext`, not the reflection serializer, so the package stays free of IL2026/IL3050 in a
  published WASM or AOT app. It depends only on `Microsoft.Data.Sqlite` and the logging/hosting abstractions.
- **The schema upgrades itself.** The `Scopes` column is added to a store created by an earlier version on
  first use — this database is framework-owned and deliberately outside your migration history, so there is
  nothing for you to run.

## See also

- [Dashboard](dashboard.md) — the Logs page that reads this store.
- [Observability](observability.md) — logging categories, meters, tracing, health checks.
- [SQLite](sqlite.md) — the production pragmas this store applies to its own file.
