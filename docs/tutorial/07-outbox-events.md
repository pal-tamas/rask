# Chapter 7 — Domain events + the outbox

> **Goal:** react to "an order was placed" reliably — the reaction runs even if the process crashes right
> after the sale.
> **You'll write:** an `IOutboxEvent`, a domain method that raises it, a component that places orders, and
> a handler.

In Chapter 4 we enqueued a job explicitly. Sometimes you'd rather have the *domain* announce that something
happened and let any number of handlers react — without the code that placed the order knowing who's
listening. That's a **domain event**. And if the reaction must not be lost (you really will refund that
card), it should be delivered through a **transactional outbox**: the event is written **in the same
database transaction** as the order, so it can never be committed without the event, and a background
processor delivers it after commit — retrying until it succeeds.

## 1. Placing an order is a domain operation

Chapter 3's order form saves an `OrderModel` with `Order.CreateAsync(model)`. That is exactly right for data
entry (a member of staff types an order in) and exactly wrong for announcing anything, because a form save
writes properties and nothing in it says that what just happened was a *sale*. It records a row; it isn't the
business event, so it isn't where the event belongs.

A customer buying a product is a different thing, and it gets its own path: a static factory on the aggregate
that raises the event, and a small component that inserts what it built. Four small additions.

**The event.** `Features/Orders/OrderEvents.cs` — one record per thing that happened:

```csharp
namespace Shop.Features.Orders;

public sealed record OrderPlaced(Guid Id) : IOutboxEvent;
```

**Raising it.** Announce the change from the same code that makes it, so an order can never be placed
without saying so. Here is chapter 3's `Features/Orders/Order.cs` with `Place` added — the whole file, so you
can see where it goes:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Shop.Features.Orders;

public sealed class Order : Aggregate<Guid>
{
    [Range(0, 1_000_000)]
    public decimal Total { get; private set; }

    public Guid ProductId { get; private set; }

    public DateTime Placed { get; private set; }

    public static Order Place(Guid productId, decimal total, DateTime now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(total);

        var order = new Order { Id = Guid.CreateVersion7(), ProductId = productId, Total = total, Placed = now };
        order.Raise(new OrderPlaced(order.Id));
        return order;
    }
}
```

The fields are chapter 3's, untouched, so the staff pages keep working. `Place` is the aggregate's **factory**:
the domain's way to create an order, which is why `Order` still declares no constructor. `Raise` comes from
`Aggregate<Guid>`, and the event sits on the aggregate until it is saved, which is what makes the next part
atomic.

**Placing it.** `Features/Orders/PlaceOrder.cs` — a "Buy" button that places an order for one product:

```csharp
namespace Shop.Features.Orders;

// A "Buy" button. A sale is a domain operation, not a form edit, so it goes through Order.Place — the
// factory that announces it — and Order.CreateAsync inserts the order Place built.
public sealed partial class PlaceOrder : Component
{
    private bool _placing;

    public Guid ProductId { get; set; }

    public decimal Price { get; set; }

    private async Task PlaceAsync()
    {
        _placing = true;
        try
        {
            var order = Order.Place(ProductId, Price, DateTime.UtcNow);
            await Order.CreateAsync(order, cancellationToken: CancellationToken);   // the order AND its OrderPlaced, one transaction
        }
        finally
        {
            _placing = false;
        }
    }

    protected override Component? Render() =>
        UiButton.Tone(UiTone.Primary).Size(UiSize.Sm).Disabled(_placing).OnClick(PlaceAsync)["Buy"];
}
```

`Order.CreateAsync(order)` is the create for an aggregate you built yourself. It saves through the same
interceptors a form save does, so the order and the event it carries are written in one transaction. The
`_placing` flag disables the button while the save runs, so a double click can't buy twice.

Drop it into `ProductsPage`'s actions column (with `using Shop.Features.Orders;` at the top of that file):

```csharp
PlaceOrder.ProductId(p.Id).Price(p.Price)
```

**Receipts follow sales now.** Take chapter 4's `EnqueueAsync` back out of `CreateOrder` — the handler in
section 3 queues the receipt from the event instead, durably.

## 2. One line, and nothing to remember

Look at what the scaffold wrote into `Program.cs`:

```csharp
builder.Services.AddRaskData<AppDbContext>();
builder.Services.AddRaskOutbox<AppDbContext>();
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskSqlite(sp)
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

Registering the outbox is what hands it delivery. `AddRaskData` needs no *options* argument to match — its
type argument is the unrelated one that names the context to the model surface, from Chapter 2 — and the two
calls work in either order: the handover is settled when the container is built, not when either line runs.

That is worth a sentence, because the alternative is a bug you would never see. `DomainEventInterceptor`
drains and **clears** every entity's events during `SaveChanges`. Were it still running alongside the outbox,
it would empty them before `OutboxInterceptor` could copy them: the outbox table stays empty, delivery quietly
stops being durable, and **nothing fails**, because the handlers still run in-process. Every test passes. You
find out when a crash loses an order confirmation. A framework that makes you opt out of that by hand is
asking you to remember something on pain of silent data loss, so Rask decides it for you.

The factory call's `.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>())` is what puts both
interceptors in the `SaveChanges` pipeline — which is why `PlaceOrder`'s `Order.CreateAsync` gets the
outbox, the timestamps and the version bump exactly as chapter 2's form saves do. Where `AddDbContextFactory`
sits relative to the other two lines does not matter: that callback runs when the factory is first resolved,
by which point the container holds every registration.

The outbox table is mapped in `AppDbContext` (`modelBuilder.AddRaskOutbox()`) and was created by the first
migration, so there is nothing to migrate. If losing an event on a crash is acceptable, plain in-process
domain events need no outbox at all — without `AddRaskOutbox`, `AddRaskData` alone dispatches them.

## 3. React to the event

`Features/Orders/OrderPlacedHandler.cs`. Any `INotificationHandler<OrderPlaced>` runs when an order is placed
— delivered by the outbox processor, post-commit, with retries. This one logs the sale and queues chapter 4's
receipt job, so the receipt is now derived from the order's own transaction:

```csharp
using Microsoft.Extensions.Logging;
using Shop.Features.Shared;

namespace Shop.Features.Orders;

public sealed class OrderPlacedHandler(IJob jobs, ILogger<OrderPlacedHandler> logger)
    : INotificationHandler<OrderPlaced>
{
    public async Task HandleAsync(OrderPlaced notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Order {Id} placed", notification.Id);
        await jobs.EnqueueAsync(new SendOrderReceipt(notification.Id), cancellationToken);
    }
}
```

`INotificationHandler<T>` is the one interface the outbox delivers to, so it's the one thing in this chapter
you implement rather than call. Nothing registers it: it's found at build time.

Because the event row committed atomically with the order, the handler is guaranteed to run **eventually**,
even if the app is killed the instant after the sale — which closes the gap Chapter 4 left open. Delivery is
**at-least-once**, so make the handler safe to repeat: it can run twice if the process dies between the work
and the acknowledgement. Here that could mean two receipt jobs, so a real shop has the job check whether a
receipt already went out before sending another.

> **Outbox or job?** Both end in a background worker, and the distinction is worth holding onto: the outbox
> delivers what is *derived from* a transaction (the order committed, so confirm it), and
> [jobs](04-background-jobs.md) run what you *schedule* (in an hour, purge stale carts). A confirmation email
> belongs to the order's transaction. A nightly cleanup does not.

> **What raises nothing.** Editing an order through `UpdateOrder` announces nothing: a form save writes the
> model's properties and calls no method, so nothing raises an event. When a change *is* something the business
> cares about (a cancellation, a shipment) give `Order` a method for it that raises its event, and call it
> through `Order.UpdateAsync(id, o => o.Cancel(now))`.

## Verify

- Clicking **Buy** on `/products` writes an `OutboxMessage` row in the same transaction as the order.
- The `OrderPlacedHandler` log line appears within the poll interval, and the row's `ProcessedAt` is set
  while `Error` stays null and `Attempts` stays `0` — that combination is what "delivered cleanly" means. A
  message that can't be deserialized doesn't throw; it records an error and retries until `MaxAttempts`.
- A receipt job row follows, and then the receipt in `mail-pickup` — the chain from Chapters 4 and 5, now
  starting from the transaction.
- Kill the app immediately after buying, restart it — the handler still runs.
- **Go further:** watch the outbox, job and mail counters move, in that order, on `/ops`
  ([Chapter 10](10-ops.md)).

**Learn more:** [outbox](../outbox.md) · [Rask.Data](../data.md) · [background jobs](../jobs.md)

Next → **[Chapter 8: Production SQLite](08-production-sqlite.md)**
