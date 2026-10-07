# Rask.Outbox

A **transactional outbox** for [Rask.Data](https://www.nuget.org/packages/Rask.Data) aggregates. It runs a handler
durably, on the app's own database, with no broker or Redis.

- Give a handler **`IDurableHandler<T>`** and the event is written to an `OutboxMessage` table **in the same
  transaction** as the change that raised it. It is never lost, and it never fires for a change that rolled back.
  Plain `IEventHandler<T>`s of the same event keep running in memory; each handler chooses.
- A background **`OutboxProcessor`** is woken by every save that wrote rows and runs each row's handler, **at
  least once**, with retries and an attempt count. Each durable handler is its own row, retried on its own.
- Published messages are **purged after `Retention`** (default 7 days) so the table doesn't grow
  forever. Dead letters are never purged, because they have no `ProcessedAt` for the predicate to match.
- **Metrics** on the `Rask.Outbox` meter: processed / failed / **dead-lettered** counters, a duration
  histogram, and pending / dead-letter gauges. `rask.outbox.deadletters` is the one to alert on.

## Use

```csharp
[LocalOnly]                                                    // only the server says a sale happened
public sealed record OrderPlaced(Guid Id) : IEvent;           // raised on your aggregate

public sealed class SendReceipt : IDurableHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced e) => …;                    // through the outbox: atomic, retried
}

// Program.cs
builder.Services.AddRaskCqrs();
builder.Services.AddRaskOutbox<AppDbContext>(o => o.PollInterval = 5.Seconds);

builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseSqlite("Data Source=app.db")
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

// AppDbContext.OnModelCreating
modelBuilder.AddRaskOutbox(); // maps the OutboxMessage table
```

In a multi-tenant app each message records the tenant the change was saved in, and the processor re-enters it
before running the handler, so a handler reading a tenant-scoped table sees that tenant. The user travels the same
way (`Current.UserId`), and `Jobs.Enqueue(…)` or `Mail.Send(…)` work inside a handler. `OutboxMessage.Where(…)`
queries the table with no context of your own.

**Server-side.** The processor is a hosted `BackgroundService` and the store is your EF Core database
(SQLite by default). Part of the [Rask](https://github.com/pal-tamas/rask) framework. MIT licensed.
