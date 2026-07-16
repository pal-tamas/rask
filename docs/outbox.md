# Rask.Outbox — a transactional outbox on your database

`Rask.Outbox` gives [`Rask.Data`](data.md) aggregates **durable, crash-safe domain-event delivery** on the
app's own database — no message broker, no Redis. It's the durable counterpart to `Rask.Data`'s in-process
publisher, and what `rask generate feature --outbox` wires up.

```bash
dotnet add package Rask.Outbox
```

## Why an outbox

Publishing a domain event *after* a transaction commits (the in-process path) is fast, but not crash-safe:
if the process dies between the commit and the publish, the event is lost. The **transactional outbox**
fixes that by writing the event to a table **in the same transaction** as the change that raised it — so it
commits atomically with the data (and is never written for a change that rolled back) — then a background
worker publishes it and marks it done. Delivery is **at-least-once**.

Because the outbox table lives in the same database as your data, that atomicity is real (a cross-database
enqueue would not be). And since it rides your existing database, there's nothing else to run.

## Use

```csharp
public sealed record OrderPlaced(Guid Id) : IOutboxEvent;   // raised on your AggregateRoot

// Program.cs
builder.Services.AddRaskCqrs();
builder.Services.AddRaskData(o => o.DispatchDomainEventsInProcess = false); // the outbox owns delivery
builder.Services.AddRaskOutbox<AppDbContext>(o =>
{
    o.PollInterval = TimeSpan.FromSeconds(5);
    o.BatchSize = 100;
    o.MaxAttempts = 10;
});

builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseSqlite("Data Source=app.db")
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    modelBuilder.ApplyRaskConventions();
    modelBuilder.AddRaskOutbox();   // maps the OutboxMessage table
}
```

Now `db.SaveChanges()` writes an `OutboxMessage` row for every `IOutboxEvent` the aggregate raised, in the
same transaction; the background `OutboxProcessor` drains and publishes them just after commit. Any
`INotificationHandler<OrderPlaced>` reacts — the **same handler** works whether the event is delivered
in-process or via the outbox (`IOutboxEvent` is an `INotification`).

Add a migration for the new table before running — `rask db add AddOutbox && rask db update`
(or `dotnet ef migrations add AddOutbox` directly).

## How it works

- **`OutboxInterceptor`** — in `SavingChanges`, drains each tracked aggregate's `IOutboxEvent`s into
  `OutboxMessage` rows on the same context (atomic with the change).
- **`OutboxProcessor<TContext>`** — a hosted `BackgroundService` that polls the table on `PollInterval`,
  publishes the oldest unprocessed batch through `IDispatcher`, and stamps `ProcessedAt` (or records the
  error + attempt count, retrying up to `MaxAttempts`). A failing handler never crashes the app.
- **The `Rask.Outbox` source generator** registers every `IOutboxEvent` type (name → CLR type) at module
  load, so the processor rehydrates a stored message with no runtime `Type.GetType` or assembly scanning.

## Notes

- **Server-side.** The processor is a hosted service and the store is your EF Core database — this is not a
  browser/WASM concern.
- **SQLite is single-writer**, so the processor polls (there is no `SKIP LOCKED` to lean on); WAL + a
  busy-timeout (see [Rask.SQLite](sqlite.md)) keep reads flowing while it writes. One processor per app.
- **In-process vs. durable.** `--events` (in-process) is fast and simple; `--outbox` is durable and
  crash-safe. Use the outbox when losing an event on a crash is unacceptable; keep the in-process publisher
  disabled when the outbox is on, so events aren't delivered twice.
