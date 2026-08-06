# Chapter 7 — Domain events + the outbox

> **Goal:** react to "an order was placed" reliably — the reaction runs even if the process crashes right
> after the sale.
> **You'll run:** `rask generate feature Order … --outbox`

In Chapter 4 we enqueued a job explicitly. Sometimes you'd rather have the *domain* announce that something
happened and let any number of handlers react — without the code that placed the order knowing who's
listening. That's a **domain event**. And if the reaction must not be lost (you really will refund that
card), it should be delivered through a **transactional outbox**: the event is written **in the same
database transaction** as the order, so it can never be committed without the event, and a background
processor delivers it after commit — retrying until it succeeds.

## 1. Scaffold the slice with events

Chapter 3 created the Orders feature. Regenerate it with `--outbox` (or pass the flag the first time):

```bash
rask generate feature Order Customer:string Total:decimal --outbox --force
```

That emits the pieces you'd otherwise write by hand:

- `Features/Orders/OrderEvents.cs` — `OrderCreated` / `OrderUpdated` / `OrderDeleted`, each an `IOutboxEvent`;
- the `Raise(...)` calls on the entity, so *creating* an order always announces it;
- `Features/Orders/OrderCreatedHandler.cs` — a handler to fill in;
- and the DI: `AddRaskOutbox<AppDbContext>()`, plus the one line below that people get wrong.

`--events` gives you plain in-process domain events without the durable outbox, if losing one on a crash is
acceptable.

## 2. The one line that matters

Look at what the generator wrote into `Program.cs`:

```csharp
builder.Services.AddRaskData(o => o.DispatchDomainEventsInProcess = false);   // ← not AddRaskData()
builder.Services.AddRaskOutbox<AppDbContext>();
```

That `false` is not a preference. With the in-process publisher left on, `DomainEventInterceptor` drains and
**clears** every entity's events during `SaveChanges`, before `OutboxInterceptor` can copy them. The outbox
table stays empty, delivery quietly stops being durable — and **nothing fails**, because the handlers still
run in-process. Every test passes. You find out when a crash loses an order confirmation.

The registration order matters for the same reason: `AddRaskOutbox` comes **before**
`AddDbContextFactory`, so its interceptor is in the container when the factory resolves
`ISaveChangesInterceptor`. Your factory call already has
`.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>())` from Chapter 2.

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
