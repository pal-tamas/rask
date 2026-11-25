# Chapter 4 — Background jobs

> **Goal:** send an order's receipt off the request thread, with a durable background job that survives restarts.
> **You'll write:** a job and its handler, the operation the handler calls, and a button that queues it.

When a member of staff wants a receipt sent you don't make them wait while you do slow work (rendering, mailing,
talking to another system). `Rask.Jobs` enqueues that work as a **durable job** — a row in your SQLite database that
a background worker picks up and runs, retrying on failure. No Redis, no broker; it rides the same `app.db`.

## 1. What the order remembers

A receipt is something that happens *to an order*, so the order records it, through an operation of its own. Add two
members at the end of the `Order` class in `Features/Orders/Order.cs`:

```csharp
    public bool ReceiptSent { get; private set; }

    public void MarkReceiptSent() => ReceiptSent = true;
```

That is all: `MarkReceiptSent` is an operation like `Ship`, so the build gives it an entry —
`await Order.MarkReceiptSent(id, version)` — and, because the aggregate is `Versioned`, a second one with no version:
`await Order.MarkReceiptSent(id)`. That one loads the order as it now stands and runs the method on that. It is the
**system's** call, for a job or an event handler that read no version, never a form's: a person changes what they
read ([operations](../domain.md#operations-what-the-aggregate-does)).

## 2. Write a job

`Features/Orders/SendOrderReceipt.cs` — a job record and its handler:

```csharp
namespace Shop.Features.Orders;

public sealed record SendOrderReceipt(OrderId Order) : IJob;

public sealed class SendOrderReceiptHandler : IJobHandler<SendOrderReceipt>
{
    public async Task Handle(SendOrderReceipt job)
    {
        var order = await Order.Where(o => o.Id == job.Order).FirstOrDefault();
        if (order is null || order.ReceiptSent)
        {
            return;   // deleted since, or done already: a job may run twice
        }

        // … the work itself: chapter 5 sends the email here …

        await Order.MarkReceiptSent(job.Order);
    }
}
```

A job is a record marked `IJob` plus the class that does the work, an `IJobHandler<T>`; the worker hands each
`SendOrderReceipt` it picks up to its `Handle`. Nothing registers the handler — it is found at build time.

- **The job carries the id, not the order.** It runs later — possibly after a restart — and reads the order as it is
  *then*, through the read face. `FirstOrDefault` is `null` for an order deleted since it was queued.
- **The change goes through the aggregate.** The handler decides nothing and writes no SQL: it calls the order's own
  operation, which loads it, runs `MarkReceiptSent`, asks its rules and saves it in one transaction — exactly what a
  page's form does. Nothing is injected, and no token is passed: the read and the write are cancelled with the job.
- **A job may run twice.** A worker that dies after the work and before the job is marked done runs it again. The
  order's `ReceiptSent` is what makes the second run do nothing.

## 3. What's already wired

Nothing to add: the jobs battery is on, and its tables — `Job` and `RecurringJobState` — were created by the first
migration `rask new` applied. Its tuning has defaults; to change one, add a `Rask:Jobs` section:

```jsonc
"Rask": {
  "Jobs": {
    "PollInterval": "00:00:05",   // how often the worker checks for due jobs
    "MaxAttempts": 25             // retry a failing job up to N times
  }
}
```

`Order` gained a column, though, so it needs a migration:

```bash
rask db add AddOrderReceiptSent
```

## 4. Queue it

A button that queues the job, `Features/Orders/SendReceiptButton.cs`:

```csharp
namespace Shop.Features.Orders;

// A row's "Send receipt": queues the job and says so. The receipt goes out from the job worker, not from this click.
public sealed partial class SendReceiptButton : Component
{
    public OrderId Order { get; set; }

    protected override Component? Render()
    {
        var send = QueryClient.Command();

        return Ui.Button.Ghost.Sm
            .Disabled(send.IsPending)
            .OnClick(async () => await send.Send(async ct => await Jobs.Enqueue(new SendOrderReceipt(Order), ct), CancellationToken))[
                send.IsSuccess ? "Queued" : "Send receipt"];
    }
}
```

`Jobs.Enqueue` returns as soon as the job row is written — the click finishes immediately, and the worker runs the
job moments later. It goes through the page's **command**, so a failed enqueue lands on `send.IsError` instead of
escaping the click, and `IsPending` greys the button meanwhile ([queries and commands](../query.md)).

Put it in the order's editor, under the ship form, in `OrdersPage.EditOrder`:

```csharp
        Form.For(Order.Ship, id)[f => [
            Ui.Field.Bind(() => f.Tracking).Label("Tracking number"),
            Ui.Button.Submit["Ship it"]
        ]],
        SendReceiptButton.Order(id),
```

Need it *later*? `Jobs.Enqueue(job).In(24.Hours)`, or `.At(aMoment)`. Need it *repeatedly*? That is code, in
`Program.cs`:

```csharp
var app = RaskApp.Create(args);

app.Configure(c =>
{
    c.Jobs.Run<PurgeStaleCarts>().Every(1.Hour);
    c.Jobs.Run<NightlyBackup>().Daily.At(3, 0);
});

app.Run<App>();
```

The job row also records who queued it, and the worker runs the handler as that user — so `Current.UserId` reads the
same in the handler as it did on the page, though nobody is signed in on the worker's thread
([more](../jobs.md#the-user-and-tenant-a-job-runs-for)).

> **Someone has to press it.** Here a person queues the receipt. [Chapter 7](07-outbox-events.md) has the order
> queue it itself, in the same transaction that places it, so a receipt can never be forgotten.

## 5. Prove it

A handler is a plain class, so a test runs it the way the worker would — on the test's own database.
`Shop.Tests/Features/Orders/ReceiptTests.cs`:

```csharp
using Shop.Features.Orders;

namespace Shop.Tests.Features.Orders;

public sealed class ReceiptTests
{
    [Fact]
    public async Task The_receipt_job_marks_the_order_once_it_has_run()
    {
        await using var database = await TestDatabase.Start();
        var order = await Test.Make<Order>();

        await new SendOrderReceiptHandler().Handle(new SendOrderReceipt(order.Id));

        var stored = await Order.Where(o => o.Id == order.Id).First();
        Assert.True(stored.ReceiptSent);
    }
}
```

## Verify

- *Send receipt* returns instantly, and a row appears in the `Job` table.
- Within `PollInterval` the order's `ReceiptSent` is `1`, and pressing *Send receipt* again queues a job that does
  nothing.
- Throw from the handler once and watch it retry (up to `MaxAttempts`) rather than losing the work.
- `dotnet test` passes.

**Learn more:** [background jobs](../jobs.md) · [operations](../domain.md#operations-what-the-aggregate-does)

Next → **[Chapter 5: Transactional email](05-email.md)**
