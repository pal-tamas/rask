# Rask.Outbox

A **transactional outbox** for [Rask.Data](https://www.nuget.org/packages/Rask.Data) entities — durable,
crash-safe domain-event delivery on the app's own database, with no broker or Redis.

- Mark a domain event **`IOutboxEvent`** and it's written to an `OutboxMessage` table **in the same
  transaction** as the change that raised it — so an event is never lost, and never fires for a change that
  rolled back.
- A background **`OutboxProcessor`** polls the table and publishes each message through
  [Rask.Cqrs](https://www.nuget.org/packages/Rask.Cqrs) (`IDispatcher.PublishAsync`) — **at-least-once**,
  with retries and an attempt count.
- Published messages are **purged after `RetentionPeriod`** (default 7 days) so the table doesn't grow
  forever. Dead letters are never purged — they have no `ProcessedAt` for the predicate to match.
- **Metrics** on the `Rask.Outbox` meter: processed / failed / **dead-lettered** counters, a duration
  histogram, and pending / dead-letter gauges. `rask.outbox.deadletters` is the one to alert on.
- A **source generator** registers every `IOutboxEvent` type for reflection-free lookup on the drain path.

## Use

```csharp
public sealed record OrderPlaced(Guid Id) : IOutboxEvent;   // raised on your Entity

// Program.cs
builder.Services.AddRaskCqrs();
builder.Services.AddRaskData(o => o.DispatchDomainEventsInProcess = false); // the outbox owns delivery
builder.Services.AddRaskOutbox<AppDbContext>(o => o.PollInterval = TimeSpan.FromSeconds(5));

builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseSqlite("Data Source=app.db")
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

// AppDbContext.OnModelCreating
modelBuilder.AddRaskOutbox(); // maps the OutboxMessage table
```

`db.SaveChanges()` now writes an `OutboxMessage` row for each `IOutboxEvent` the entity raised, in the
same transaction; the processor drains and publishes them just after commit. Any
`INotificationHandler<OrderPlaced>` reacts — the same handler works whether events are delivered in-process
(Rask.Data) or via the outbox.

**Server-side.** The processor is a hosted `BackgroundService` and the store is your EF Core database
(SQLite by default). Part of the [Rask](https://github.com/pal-tamas/rask) framework. MIT licensed.
