# Rask.Outbox — a transactional outbox on your database

> **In practice:** [Tutorial Ch 7](tutorial/07-outbox-events.md) · recipe [publish a domain event through the outbox](recipes.md#publish-a-domain-event-through-the-outbox) · [cheat sheet](cheatsheet.md).

`Rask.Outbox` runs a handler **durably**: the event is written to a table in the same transaction as the change
that raised it, and the handler runs from there after the commit, retried until it succeeds and run again after a
crash rather than lost. It uses the app's own database, with no message broker and no Redis. Chapter 7 of
[the tutorial](tutorial/07-outbox-events.md) builds one.

> Included in [`Rask.Server`](../README.md), with nothing to install. It is always on while the data battery is:
> a **handler** chooses durability, so there is nothing to switch off.

## One event, two kinds of handler

An event is an ordinary `IEvent`. Each handler says how it wants it:

```csharp
[LocalOnly]                                                     // only the server says a sale happened
public sealed record OrderPlaced(Guid Id) : IEvent;              // raised on your aggregate

public sealed class RefreshDashboard : IEventHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced e) => …;                      // in memory, right after the commit
}

public sealed class SendReceipt : IDurableHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced e) => …;                      // through the outbox: atomic, retried
}
```

- **`IEventHandler<T>`** runs in memory straight after the save commits. It is fast, but it is lost if the process
  dies between the commit and the handler. Use it for what can be rebuilt: a cache, a counter, a screen.
- **`IDurableHandler<T>`** gets its own outbox row, written in the save's transaction. So it can never be skipped
  for a change that committed, and never runs for one that rolled back. Use it for what must happen: a receipt, a
  charge, a call to another system.

Each durable handler is its **own row**. When `SendReceipt` fails, only `SendReceipt` is retried; a second durable
handler of the same event has already succeeded and is not run again.

An event published straight through the dispatcher, with no save behind it, stores its durable rows in a small
transaction of its own. From that moment it is just as crash-safe; it just isn't atomic with any other change,
because there is none:

```csharp
await dispatcher.Publish(new OrderPlaced(id));   // SendReceipt → outbox row, RefreshDashboard → now
```

## Configure

Every key has a default; set only what you change:

```csharp
app.Configure(c => c.Outbox.Configure(o => o.PollInterval = 1.Second));
```

```jsonc
// appsettings.json
{
  "Rask": {
    "Outbox": {
      "PollInterval": "00:00:05",        // the safety-net poll; a save wakes the processor at once
      "BatchSize": 100,
      "MaxAttempts": 10,
      "RetentionPeriod": "7.00:00:00"   // "00:00:00" keeps published messages forever
    }
  }
}
```

The code runs after the section and wins.

### Wiring it by hand

`RaskApp` does all of this for you. A hand-wired host adds:

```csharp
builder.Services.AddRaskCqrs();
builder.Services.AddRaskOutbox<AppDbContext>();   // brings AddRaskData with it

builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseSqlite("Data Source=app.db")
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    modelBuilder.AddRaskOutbox();              // maps the OutboxMessage table
    modelBuilder.ApplyRaskConventions(this);   // last: it walks every table mapped above
}
```

Add a migration for the table before running: `rask db add AddOutbox && rask db update`
(or `dotnet ef migrations add AddOutbox` directly). Without an outbox (a browser app, or `Rask.Cqrs` on its own) a
durable handler runs in memory like any other.

## Upgrading

Two changes arrive together.

- **Durability moved to the handler.** `IOutboxEvent` is gone. Declare the event `: IEvent`, and give the handler that
  must not be lost `IDurableHandler<T>` instead of `IEventHandler<T>`. `c.Outbox.Off()` is gone as well, since
  there is nothing to switch off.
- **The table gained a `Handler` column**, holding one row per durable handler. Add it with
  `rask db add AddOutboxHandler && rask db update`. Until you do, the processor logs exactly that line on every
  poll. A row stored before the upgrade has no handler, and runs every handler of its event once, as it would have
  then. The processor drains only while the app has a durable handler, so keep at least one until those rows are
  gone.

A later release added a `UserId` column, the user a handler runs for. Add it with
`rask db add AddOutboxUser && rask db update`; the processor logs that line until you do.

## How it works

- **`OutboxInterceptor`**: in `SavingChanges`, writes an `OutboxMessage` row for each durable handler of each event
  the tracked aggregates raised, on the same context, so the rows commit with the change. After the commit it wakes
  the processor.
- **`DomainEventInterceptor`** (Rask.Data) publishes the same events to their `IEventHandler`s after the commit.
  The events are cleared only then, so the order of the two interceptors doesn't matter.
- **`OutboxProcessor<TContext>`**: a hosted `BackgroundService` woken by every save that wrote rows. It also polls
  every `PollInterval`, as the safety net for rows another instance wrote, for a backlog, and after a restart. It
  runs each row's handler and stamps `ProcessedAt`, or records the error and attempt count, retrying up to
  `MaxAttempts`. Published messages older than `RetentionPeriod` are purged hourly, in pages, so the table doesn't
  grow for the life of the app. **Dead letters are never purged**, because they have no `ProcessedAt` for the
  retention predicate to match. A failing handler never crashes the app, and neither does a failing poll: a
  transient database error is logged and retried on the next one. Each message's outcome is saved on its own, so a
  row edited or deleted underneath the drain costs that one row, rather than re-running everything the batch had
  already delivered.
- **The Rask.Cqrs source generator** records every durable handler beside its event, so the processor reads a
  stored event back and runs its handler with no runtime `Type.GetType` or assembly scanning. A handler renamed or
  deleted while rows still name it fails those rows with a message saying so, and they dead-letter where the
  dashboard shows them. `OutboxSerializerRegistry.RegisterEvent("Old.Name", typeof(OrderPlaced))` keeps a renamed
  *event*'s stored rows readable.
- **The tenant travels with the event.** In an app with [multi-tenancy](multi-tenancy.md), each row records the
  tenant in flight when the change was saved, and the processor re-enters it before running the handler. So a
  handler that reads a tenant-scoped table sees the same tenant the change was made in, even though no one is
  signed in on the processor's thread. An event raised by the host itself, or inside `Tenant.Across()`, records none.
- **So does the user.** Each row records `Current.UserId` when the change was saved, and the handler reads the same
  value. A handler is work in progress like a request, so `Jobs.Enqueue(…)`, `Mail.Send(…)` and the model's reads
  work inside it with nothing injected.
  The table is not partitioned by a filter, because one processor drains every tenant's events.
- **`OutboxMessage` has a read face**, like every Rask.Data entity, so the queue can be queried with no context of
  your own: `OutboxMessage.Where(m => m.ProcessedAt == null).Count()`.

## Shutdown

On `SIGTERM` the processor stops picking up **new** messages immediately, but the one already inside your
handler gets `OutboxOptions.ShutdownGracePeriod` (default 5s) to finish rather than being cancelled
mid-call. Only one message is ever in that window, so shutdown is extended by at most a single grace period.

A message that outlives its grace is cancelled and run again whole on the next boot. It does **not** count as a
failed attempt: `MaxAttempts` defaults to 10, so counting redeploys would let ten unlucky deploys abandon a message
nobody ever failed to publish. `rask.outbox.interrupted` counts these, and a warning is logged.

`ShutdownGracePeriod` cannot exceed `HostOptions.ShutdownTimeout`: once that elapses the host stops waiting
for hosted services, so a longer grace silently does not happen. `TimeSpan.Zero` cancels immediately.

## Notes

- **Server-side.** The processor is a hosted service and the store is your EF Core database; this is not a
  browser/WASM concern.
- **Mark a domain event `[LocalOnly]`.** An event travels from a browser like any message, so without it a signed-in
  user could publish `OrderPlaced` for an order nobody placed, and `SendReceipt` would run. See
  [`[LocalOnly]`](cqrs.md#localonly).
- **An event with a durable handler is stored as JSON**, so it should be a plain record of values. It is read back
  by the name of its type, so keep that name, or register the old one when you rename it (above).
- **Running more than one instance is safe.** Each processor *leases* the batch it claims, so a message is run by
  exactly one instance. See [running more than one instance](scaling.md#running-more-than-one-instance). On SQLite
  you will still usually run one instance, for the unrelated reason that SQLite has a single writer. WAL and a busy
  timeout (see [Rask.SQLite](sqlite.md)) keep reads flowing while it writes.
- **`Attempts` counts attempts *started*, not failures.** The claim increments it, so a handler that takes
  the process down with it still counts toward `MaxAttempts` instead of being retried forever.
- **Ordering is per-poll, not globally strict.** Each cycle runs the oldest unprocessed batch in insertion order,
  but because delivery is **at-least-once** with retries, a message that fails and is retried can land after later
  ones. Make durable handlers **idempotent** and don't rely on strict cross-message ordering.
- **Outbox vs. jobs.** A durable handler reacts to an event *derived from a transaction*, atomic with the data
  change; [`Rask.Jobs`](jobs.md) runs work you *explicitly enqueue*. Reach for a durable handler when a reaction
  must commit with its change, and for a job when you're scheduling work.
