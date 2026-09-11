# Rask.Logging

A **durable log store** for a Rask app — kept in a SQLite file of its own, or in your PostgreSQL or SQL Server
database, with no agent and no hosted log service. What happened survives the restart that hid it.

- Registers a standard **`ILoggerProvider`**, so it captures exactly what every other sink sees — your own
  categories and the framework's (`Rask.Live`, `Rask.Lifecycle`, `Rask.Diff`, …) — with no wiring at the call
  sites.
- Log calls **never wait on the disk**. Entries go into a bounded in-memory buffer and a background writer
  flushes them in batches; when the buffer is full an entry is dropped and counted, never queued unbounded and
  never blocked on.
- **Retention by age and by row count**, swept in short pages so the write lock is never held for a whole
  sweep. Both are on by default: age alone doesn't bound the disk when an app gets chatty.
- **Queryable** — filter by level, category, text, and time range, with paging. `Rask.Dashboard`'s Logs page
  reads it directly.

## Use

```csharp
// Program.cs
builder.Services.AddRaskLogging();
```

That is the whole setup — the store opens the `Rask:ConnectionStrings:Logs` connection string, and the schema
is created on first use, so there is no migration to add. Every option is configuration too:

```jsonc
// appsettings.json
{
  "Rask": {
    "ConnectionStrings": {
      "Logs": "Data Source=logs.db"
    },
    "Logging": {
      "MinimumLevel": "Warning",      // a floor, not an override (see below)
      "Retention": "30.00:00:00",
      "MaxRows": 250000               // the backstop against a log storm
    }
  }
}
```

A callback — `AddRaskLogging(o => …)` — runs after the `Rask:Logging` section and wins.

```csharp
// Read it back from your own code.
var page = await store.SearchAsync(new LogQuery
{
    MinimumLevel = LogLevel.Error,
    Search = "checkout",
    From = DateTimeOffset.UtcNow.AddHours(-6),
});
```

`MinimumLevel` is a **floor, not an override**: the logging pipeline applies your `Logging:LogLevel`
configuration first, so an entry filtered there never reaches the store however low you set it.

> **Its own file, on purpose — on SQLite.** There this store does not map onto your `DbContext`. Log lines arrive
> at machine rates, and the line you most want is the one written *while a transaction is failing* — on the app's
> context it would roll back with the failure. The trade-off: the log file is **not** covered by `rask db backup`
> or Litestream, and log lines can contain secrets, so treat it as sensitive and put it on the same persistent
> volume as your database.

On **PostgreSQL or SQL Server**, keep the log in the application's database instead:

```csharp
builder.Services.AddRaskLogging<AppDbContext>();

// AppDbContext.OnModelCreating — then: rask db add AddLogs && rask db update
modelBuilder.AddRaskLogging();
```

A server locks rows rather than the whole database, and each flush runs on a context and connection of its own, so
a line logged inside a failing transaction still survives its rollback. The SQL EF Core runs for the store itself
is never captured back into it.

Watch **`rask.logs.dropped`** on the `Rask.Logging` meter. A non-zero drop rate is the only thing that will
tell you the log you are reading is incomplete.
