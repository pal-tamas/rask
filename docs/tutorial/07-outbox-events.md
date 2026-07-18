# Chapter 7 — Domain events + the outbox

> **Goal:** react to "an order was placed" reliably — the reaction runs even if the process crashes right
> after the sale.
> **You'll add:** an `IOutboxEvent`, `AddRaskOutbox<ProductsDbContext>()`, and a handler.

In Chapter 4 we enqueued a job explicitly. Sometimes you'd rather have the *domain* announce that something
happened and let any number of handlers react — without the code that placed the order knowing who's
listening. That's a **domain event**. And if the reaction must not be lost (you really will refund that
card), it should be delivered through a **transactional outbox**: the event is written **in the same
database transaction** as the order, so it can never be committed without the event, and a background
processor delivers it after commit — retrying until it succeeds.

## 1. Define an event and raise it

Add an event record marked `IOutboxEvent` (put it next to the entity, e.g. `Features/Orders/OrderEvents.cs`):

```csharp
public sealed record OrderPlaced(Guid Id) : IOutboxEvent;
```

Raise it from the entity itself, so *creating* an order always announces it. Open `Features/Orders/Order.cs`
and have `Create` raise the event (`Raise` is provided by the `Entity<TId>` base):

```csharp
public static Order Create(decimal total, Guid productId, DateTime placed)
{
    var order = new Order(total, productId, placed);
    order.Raise(new OrderPlaced(order.Id));
    return order;
}
```

## 2. Wire up the outbox

The outbox needs domain events to be dispatched **after** the transaction commits (not in-process during
`SaveChanges`). In `Program.cs`, tell `Rask.Data` to hand events to the outbox, and register it:

```csharp
builder.Services.AddRaskCqrs();
builder.Services.AddRaskData(o => o.DispatchDomainEventsInProcess = false);   // ← was AddRaskData()
builder.Services.AddRaskOutbox<ProductsDbContext>(o =>
{
    o.PollInterval = TimeSpan.FromSeconds(5);
    o.MaxAttempts  = 10;
});
```

Your `AddDbContextFactory<ProductsDbContext>` call already has
`.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>())` (from Chapter 2), so the outbox interceptor is
picked up automatically. Map the outbox table in `OnModelCreating`:

```csharp
modelBuilder.AddRaskOutbox();       // ← the OutboxMessage table
```

Migrate:

```bash
rask db add AddOutbox
rask db update
```

## 3. React to the event

Any `INotificationHandler<OrderPlaced>` now runs when an order is placed — delivered by the outbox
processor, post-commit, with retries:

```csharp
public sealed class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger)
    : INotificationHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Order {Id} placed — updating stock / analytics…", notification.Id);
        return Task.CompletedTask;
    }
}
```

Because the event row committed atomically with the order, the handler is guaranteed to run **exactly once
per order, eventually** — even if the app is killed the instant after the sale.

> **Shortcut for new features.** You wired the outbox by hand here to see the moving parts. When you scaffold
> a feature, `rask generate feature <Name> … --outbox` emits the event records, the `Raise` calls, the
> handler, and prints the `AddRaskData(… DispatchDomainEventsInProcess = false)` + `AddRaskOutbox<…>()`
> registration for you. Use `--events` for plain in-process domain events without the durable outbox.

## Verify

- After `rask db update`, placing an order writes an `OutboxMessage` row in the same transaction as the order.
- The `OrderPlacedHandler` log line appears within the poll interval.
- Kill the app immediately after placing an order, restart it — the handler still runs (the event was
  durably stored, not lost).

**Learn more:** [outbox](../outbox.md) · [Rask.Data](../data.md) · [background jobs](../jobs.md)

Next → **[Chapter 8: Production SQLite](08-production-sqlite.md)**
