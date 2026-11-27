# Chapter 7 — Domain events + the outbox

> **Goal:** when an order is placed, take its stock and queue its receipt — reliably, so both happen even if the
> process crashes right after the sale.
> **You'll write:** an event the order raises, a handler on `Product` that takes the stock, and a durable handler that
> queues the receipt.

In chapter 6 the catalog announced a change and a cache, in memory, heard it: lose that moment and the cache simply
expires. Some reactions must not be lost — a sale must take its stock, a receipt must go out — so they are delivered
through a **transactional outbox**: the event is written **in the same database transaction** as the order, so it can
never be committed without the event, and a background processor delivers it after the commit, retrying until it
succeeds.

## 1. The order says it was placed

**The event.** `Features/Orders/OrderEvents.cs` — one record per thing that happened:

```csharp
using Shop.Features.Products;

namespace Shop.Features.Orders;

[LocalOnly]
public sealed record OrderPlaced(OrderId Order, IReadOnlyList<OrderedItem> Items) : IEvent;

public sealed record OrderedItem(ProductId Product, long Quantity);
```

`[LocalOnly]` keeps it on the server: without it, a signed-in browser could publish an `OrderPlaced` for an order
nobody placed. Only `Order.Place` should ever say a sale happened. The event carries what its handlers need — the
products and how many — so none of them has to read the order back.

**Raising it.** The factory raises it on the order it builds, so an order can never be placed without saying so. Here
is `Features/Orders/Order.cs` with the one new line in `Place`:

```csharp
namespace Shop.Features.Orders;

public readonly partial record struct OrderId(Guid Value);

public sealed partial record CustomerEmail(string Value) : Email(Value);

public sealed partial record TrackingNumber(string Value) : Text(Value, MaxLength: 40, Upper: true);

public sealed partial record GiftNote(string Value) : Text(Value, MaxLength: 300);

public sealed partial record ShippingAddress(string Street, string City, string PostCode) : Value
{
    protected override IEnumerable<Problem> Rules()
    {
        if (PostCode.Trim().Length is < 3 or > 10)
        {
            yield return Problem.At(PostCode, "A post code is 3 to 10 characters.");
        }
    }
}

public enum OrderStatus
{
    Placed,
    Shipped,
}

public sealed partial class Order : Aggregate<OrderId>, IHasTimestamps, IVersioned, IDeletable
{
    public CustomerEmail Customer { get; private set; } = null!;

    public ShippingAddress ShipTo { get; private set; } = null!;

    public GiftNote? Gift { get; private set; }

    public OrderStatus Status { get; private set; }

    public TrackingNumber? Tracking { get; private set; }

    public HasMany<OrderLine> Lines { get; } = new();

    private static Order Place(CustomerEmail customer, ShippingAddress shipTo, GiftNote? gift, IReadOnlyList<OrderLineSpec> lines)
    {
        var order = new Order { Id = OrderId.Create(), Customer = customer, ShipTo = shipTo, Gift = gift };
        order.Take(lines);
        order.Raise(new OrderPlaced(order.Id, [.. lines.Select(l => new OrderedItem(l.Product, l.Quantity.Value))]));
        return order;
    }

    public void Change(ShippingAddress shipTo, Change<GiftNote?> gift, IReadOnlyList<OrderLineSpec> lines)
    {
        Refuse.Unless(Status == OrderStatus.Placed, "A shipped order cannot change.");
        (ShipTo, Gift) = (shipTo, gift.Applied(Gift));
        Take(lines);
    }

    public void Ship(TrackingNumber tracking)
    {
        Refuse.Unless(Status == OrderStatus.Placed, "This order has already shipped.");
        (Status, Tracking) = (OrderStatus.Shipped, tracking);
    }

    private void Take(IReadOnlyList<OrderLineSpec> lines)
    {
        Refuse.When(lines.Count == 0, () => lines, "An order has at least one line.");
        _ = Lines.Sync(lines, l => l.Id, OrderLine.For, static (line, spec) => line.Take(spec));
    }

#pragma warning disable S1144 // found by convention: every delete asks it
    private IEnumerable<Problem> ValidateDelete()
    {
        if (Status == OrderStatus.Shipped)
        {
            yield return Problem.Whole("A shipped order stays on the books.");
        }
    }
#pragma warning restore S1144

    public bool ReceiptSent { get; private set; }

    public void MarkReceiptSent() => ReceiptSent = true;
}
```

The event sits on the aggregate until it is saved — and that is what makes the next part atomic.

## 2. Nothing to remember

There is nothing to register. The outbox is on whenever the database is, its table is mapped by `RaskAppDbContext`,
and the first migration created it. Every save runs the outbox's interceptor beside the timestamps and the version
bump, which is why `Order.Place` — through its form, a test or anything else — stores the order **and** its event's
deliveries in one transaction.

## 3. The stock: a handler on the aggregate it changes

An order must not change a product: each aggregate is its own transaction boundary. What an event does to **another
aggregate** is that aggregate's own business, so the handler lives on it, as one method. `Features/Products/Product.Sales.cs`
— a second part of the `partial` class, for the half of `Product` that hears about orders:

```csharp
using Shop.Features.Orders;

namespace Shop.Features.Products;

// What selling does to a product: the half of Product that hears about orders.
public sealed partial class Product
{
    public void Sell(long count)
    {
        Stock = StockLevel.Create(Math.Max(0, Stock.Value - count));
        Raise(new CatalogChanged(Id));
    }

    // An order was placed: each product it names sells what was ordered, in a transaction of its own.
    private static async Task On(OrderPlaced e)
    {
        var sold = e.Items.Select(i => i.Product).ToList();
        await Product.UpdateEach(p => sold.Contains(p.Id), p => p.Sell(e.Items.Where(i => i.Product == p.Id).Sum(i => i.Quantity)));
    }
}
```

- **`static Task On(OrderPlaced e)`** is a durable handler, exactly as a handler class would be: an outbox row in the
  transaction that placed the order, run from there after the commit, retried alone. No class, no registration, and
  it stays private ([a handler on the aggregate](../outbox.md#a-handler-on-the-aggregate)). The build checks its shape
  ([RASK179](../diagnostics.md#rask179)).
- **It runs in a transaction of its own**, never the order's: one aggregate per transaction, and the event is how a
  change reaches the next one ([the transaction boundary](../domain.md#the-transaction-boundary)).
- **`Product.UpdateEach(where, apply)`** loads every product the order names, runs `Sell` on each and saves it — its
  rules, its version and its events (the `CatalogChanged` that forgets the cache) as for one
  ([writes over many rows](../data-writes.md#writes-over-many-rows)).
- **It decides nothing it cannot decide alone.** A product that has sold out is the shop's to restock: the sale stands,
  and the stock stops at zero. A handler that refused would be retried forever.

## 4. The receipt: a durable handler

Queuing a receipt changes no aggregate, so it is a handler class, `Features/Orders/OrderPlacedHandler.cs`:

```csharp
namespace Shop.Features.Orders;

public sealed partial class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger) : IDurableHandler<OrderPlaced>
{
    public async Task Handle(OrderPlaced e)
    {
        Placed(logger, e.Order);
        await Jobs.Enqueue(new SendOrderReceipt(e.Order));
    }

    [LoggerMessage(LogLevel.Information, "Order {Order} placed")]
    private static partial void Placed(ILogger logger, OrderId order);
}
```

An event can have any number of handlers, and **each chooses** how it is run:

- `IEventHandler<OrderPlaced>` runs in memory straight after the commit — fast, but gone if the process dies at that
  moment. That suits a cache or a dashboard counter.
- `IDurableHandler<OrderPlaced>` — and an aggregate's `On` — gets its own outbox row in the order's transaction, and
  runs from there after the commit, retried until it succeeds. That suits stock and receipts.

The log line is a `[LoggerMessage]` method, .NET's own way to log without formatting a message nobody reads (CA1848
asks for it over `logger.LogInformation(…)`).

Delivery is **at-least-once**: a handler can run twice if the process dies between its work and the outbox's mark.
The receipt job is safe to repeat — the order remembers (chapter 4) — but a second `Sell` would take the
stock twice. For work in the database, turn the window off:
`app.Configure(c => c.Outbox.Configure(o => o.HandlerInTransaction = true))` commits a handler's writes and the outbox's
mark together, so it is exactly once ([exactly once](../outbox.md#exactly-once-for-the-database-handlerintransaction)).
For a call outside the database, `DurableEvent.Current?.MessageId` is the same on every attempt — hand it to the other
side as an idempotency key.

> **Outbox or job?** Both end in a background worker. The outbox delivers what is *derived from* a transaction (the
> order committed, so take its stock), and [jobs](04-background-jobs.md) run what you *schedule* or what is long (send
> this receipt, rebuild that report). A handler on the aggregate that runs long becomes a job with one line in its
> `Configure`: `b.Job<OrderPlaced>()` ([an aggregate's handler as a job](../jobs.md#an-aggregates-handler-as-a-job)).

## 5. Prove it

The receipt handler now queues a job whenever an order is saved, and a test database has no job queue: add the
jobs to `Shop.Tests/FakeBatteries.cs`:

```csharp
namespace Shop.Tests;

// The batteries a save's handlers reach, faked for one test: a TestDatabase runs every handler a save raises, and has
// no cache or job queue of its own. Each fake records what it was asked and does nothing outside the test.
internal sealed class FakeBatteries : IDisposable
{
    public CacheFake Cache { get; } = Rask.Caching.Cache.Fake();

    public JobsFake Jobs { get; } = Rask.Background.Jobs.Fake();

    public void Dispose()
    {
        Jobs.Dispose();
        Cache.Dispose();
    }
}
```

In a test database a durable handler runs in line, straight after the save, so a test sees what the outbox would
have done. `Shop.Tests/Features/Orders/OrderPlacedTests.cs`:

```csharp
using Shop.Features.Orders;
using Shop.Features.Products;

namespace Shop.Tests.Features.Orders;

public sealed class OrderPlacedTests
{
    [Fact]
    public async Task Placing_an_order_sells_its_stock_and_queues_the_receipt()
    {
        await using var database = await TestDatabase.Start();
        using var batteries = new FakeBatteries();
        var anvil = await Test.Make<Product>(o => o.With("stock", StockLevel.Create(5)));

        var order = await Order.Place(
            Test.Make<CustomerEmail>(), Test.Make<ShippingAddress>(), null,
            [new OrderLineSpec(null, anvil.Id, Quantity.Create(2))]);

        var stored = await Product.Where(p => p.Id == anvil.Id).First();
        Assert.Equal(3, stored.Stock);
        Assert.Equal(order, batteries.Jobs.Enqueued<SendOrderReceipt>().Only().Job.Order);
    }
}
```

`o.With("stock", …)` hands the factory's own parameter to `Test.Make`, where the test cares about it.

## Verify

- Place an order on `/orders`: an `OutboxMessage` row per durable handler is written in the same transaction as the
  order, the product's stock drops, and a receipt job follows — then the receipt in `mail-pickup`.
- Kill the app immediately after placing an order, restart it — the stock is still taken and the receipt still goes.
- **Go further:** watch the outbox, job and mail counters move, in that order, on `/ops`
  ([Chapter 10](10-ops.md)).
- `dotnet test` passes.

**Learn more:** [outbox](../outbox.md) · [domain events](../domain.md#domain-events) · [background jobs](../jobs.md)

Next → **[Chapter 8: Production SQLite](08-production-sqlite.md)**
