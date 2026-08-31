# Chapter 7 — Domain events + the outbox

> **Goal:** react to "an order was placed" reliably — the reaction runs even if the process crashes right
> after the sale.
> **You'll write:** `IOutboxEvent`s, the `Raise` calls, and a handler.

In Chapter 4 we enqueued a job explicitly. Sometimes you'd rather have the *domain* announce that something
happened and let any number of handlers react — without the code that placed the order knowing who's
listening. That's a **domain event**. And if the reaction must not be lost (you really will refund that
card), it should be delivered through a **transactional outbox**: the event is written **in the same
database transaction** as the order, so it can never be committed without the event, and a background
processor delivers it after commit — retrying until it succeeds.

## 1. Scaffold the slice with events

Chapter 3 created the Orders feature. Turning it into an event source is three small additions.

**The events.** `Features/Orders/OrderEvents.cs` — one record per thing that happened:

```csharp
namespace Shop.Features.Orders;

public sealed record OrderCreated(Guid Id) : IOutboxEvent;

public sealed record OrderUpdated(Guid Id) : IOutboxEvent;

public sealed record OrderDeleted(Guid Id) : IOutboxEvent;
```

**Raising them.** Announce the change from the same method that makes it, so an order can never be
created without saying so. Here is chapter 3's `Features/Orders/Order.cs` with the three `Raise` calls
added — the whole file, so you can see where they go:

```csharp
namespace Shop.Features.Orders;

public sealed class Order : Entity<Guid>
{
    private Order() { } // EF Core materialization

    private Order(decimal total, Guid productId, DateTime placed)
    {
        Id = Guid.NewGuid();
        this.Total = total;
        this.ProductId = productId;
        this.Placed = placed;
    }

    public decimal Total { get; private set; }

    public Guid ProductId { get; private set; }

    public DateTime Placed { get; private set; }

    public static Order Create(decimal total, Guid productId, DateTime placed)
    {
        var entity = new Order(total, productId, placed);
        entity.Raise(new OrderCreated(entity.Id));
        return entity;
    }

    public void Update(decimal total, Guid productId, DateTime placed)
    {
        this.Total = total;
        this.ProductId = productId;
        this.Placed = placed;
        Raise(new OrderUpdated(Id));
    }

    public void RaiseDeleted() => Raise(new OrderDeleted(Id));
}
```

`Create` changed from an expression body to a block so it can raise before returning; the fields are
chapter 3's, untouched. `Raise` comes from `Entity<TId>`, and the events sit on the entity until
`SaveChanges` — which is what makes the next part atomic.

**Reacting.** `Features/Orders/OrderCreatedHandler.cs` — auto-registered by `AddRaskCqrs()`:

```csharp
using Microsoft.Extensions.Logging;
using Shop.Features.Shared;

namespace Shop.Features.Orders;

public sealed class OrderCreatedHandler(ILogger<OrderCreatedHandler> logger)
    : INotificationHandler<OrderCreated>
{
    public Task HandleAsync(OrderCreated notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Order {Id} created", notification.Id);
        return Task.CompletedTask;
    }
}
```

Plus the DI — `AddRaskOutbox<AppDbContext>()`, and the one line below that people get wrong.

If losing an event on a crash is acceptable, plain in-process domain events need no outbox at all —
`AddRaskData()` alone dispatches them.

## 2. One line, and nothing to remember

Look at what the generator wrote into `Program.cs`:

```csharp
builder.Services.AddRaskData();
builder.Services.AddRaskOutbox<AppDbContext>();
```

Registering the outbox is what hands it delivery. `AddRaskData()` needs no argument to match, and the two
calls work in either order — the handover is settled when the container is built, not when either line runs.

That is worth a sentence, because the alternative is a bug you would never see. `DomainEventInterceptor`
drains and **clears** every entity's events during `SaveChanges`. Were it still running alongside the outbox,
it would empty them before `OutboxInterceptor` could copy them: the outbox table stays empty, delivery quietly
stops being durable, and **nothing fails**, because the handlers still run in-process. Every test passes. You
find out when a crash loses an order confirmation. A framework that makes you opt out of that by hand is
asking you to remember something on pain of silent data loss, so Rask decides it for you.

Your factory call already has `.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>())` from Chapter 2,
which is what puts both interceptors in the `SaveChanges` pipeline. Where `AddDbContextFactory` sits relative
to these two lines does not matter: that callback runs when the factory is first resolved, by which point the
container holds every registration.

Then create the table:

```bash
rask db add AddOutbox
rask db update
```

## 3. React to the event

Fill in the generated handler. Any `INotificationHandler<OrderCreated>` runs when an order is placed —
delivered by the outbox processor, post-commit, with retries:

```csharp
public sealed class OrderCreatedHandler(ILogger<OrderCreatedHandler> logger)
    : INotificationHandler<OrderCreated>
{
    public Task HandleAsync(OrderCreated notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Order {Id} placed — updating stock / analytics…", notification.Id);
        return Task.CompletedTask;
    }
}
```

Because the event row committed atomically with the order, the handler is guaranteed to run **eventually**,
even if the app is killed the instant after the sale. Delivery is **at-least-once**, so make the handler
safe to repeat — it can run twice if the process dies between the work and the acknowledgement.

> **Outbox or job?** Both end in a background worker, and the distinction is worth holding onto: the outbox
> delivers what is *derived from* a transaction (the order committed, so confirm it), and
> [jobs](04-background-jobs.md) run what you *schedule* (in an hour, purge stale carts). A confirmation email
> belongs to the order's transaction. A nightly cleanup does not.

## Verify

- Placing an order writes an `OutboxMessage` row in the same transaction as the order.
- The `OrderCreatedHandler` log line appears within the poll interval, and the row's `ProcessedAt` is set
  while `Error` stays null and `Attempts` stays `0` — that combination is what "delivered cleanly" means. A
  message that can't be deserialized doesn't throw; it records an error and retries until `MaxAttempts`.
- Kill the app immediately after placing an order, restart it — the handler still runs.
- **See it running:** [`samples/Rask.Example.Shop`](../../samples/Rask.Example.Shop) does exactly this, and
  its handler goes further — queueing the confirmation email and scheduling a follow-up job. Watch the
  outbox, mail and job counters move on `/ops`.

**Learn more:** [outbox](../outbox.md) · [Rask.Data](../data.md) · [background jobs](../jobs.md)

Next → **[Chapter 8: Production SQLite](08-production-sqlite.md)**
