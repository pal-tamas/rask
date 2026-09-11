# Chapter 4 — Background jobs

> **Goal:** send order processing off the request thread with a durable background job that survives restarts.
> **You'll write:** a job record and its handler under `Features/Shared/`.

When a customer places an order you don't want to make them wait while you do slow follow-up work (charging,
emailing, updating stock). `Rask.Jobs` enqueues that work as a **durable job** — a row in your SQLite
database that a background worker picks up and runs, retrying on failure. No Redis, no broker; it rides the
same `app.db`.

## 1. Write a job

Create `Features/Shared/SendOrderReceipt.cs` — a job record and its handler:

```csharp
namespace Shop.Features.Shared;

public sealed record SendOrderReceipt : IBackgroundJob;

public sealed class SendOrderReceiptHandler : ICommandHandler<SendOrderReceipt>
{
    public Task HandleAsync(SendOrderReceipt job, CancellationToken cancellationToken)
    {
        // TODO: do the work.
        return Task.CompletedTask;
    }
}
```

A job is a record marked `IBackgroundJob` plus the class that does the work. `ICommandHandler<T>` is simply
the interface the job worker hands a job to, and nothing registers the handler: it is found at build time.
Give the job the data it needs by adding a parameter:

```csharp
public sealed record SendOrderReceipt(Guid OrderId) : IBackgroundJob;
```

Pass the id, not the order. The job runs later — possibly after a restart — and should read the order as it
is *then*.

Fill in the handler with whatever the work is (we'll make it send an email in the next chapter). Reading the
order is the same call a page makes:

```csharp
using Shop.Features.Orders;   // for Order

public sealed class SendOrderReceiptHandler : ICommandHandler<SendOrderReceipt>
{
    public async Task HandleAsync(SendOrderReceipt job, CancellationToken ct)
    {
        var order = await Order.FindAsync(job.OrderId, ct);
        // … process the order …
    }
}
```

There's nothing to inject for that read. `Order.FindAsync` opens its own context and disposes it before it
returns, which is as right on a background worker as it is on a page.

## 2. What's already wired

Chapter 1's `rask new` registered jobs for you. Worth reading anyway, because two of these lines are
the ones you'd have to get right by hand.

In `Program.cs` the scaffold wrote `builder.Services.AddRaskJobs<AppDbContext>();`. To tune the worker, give
that same line options:

```csharp
builder.Services.AddRaskJobs<AppDbContext>(o =>
{
    o.PollInterval = TimeSpan.FromSeconds(5);   // how often the worker checks for due jobs
    o.MaxAttempts  = 25;                        // retry a failing job up to N times
});
```

Both values are the defaults, so this changes nothing until you edit a number. `AddRaskJobs` resolves
`IDbContextFactory<AppDbContext>` — never a scoped `DbContext`, because a live session is long-lived over a
WebSocket and a scoped context would outlive any unit of work. It hands each job to its handler through the
scaffold's `AddRaskCqrs()` line, which is why that line is there.

The jobs tables are mapped in `AppDbContext.OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);       // every Model<TId> you declared
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    modelBuilder.AddRaskOutbox();
    modelBuilder.AddRaskJobs();               // ← the Job + RecurringJobState tables
    modelBuilder.AddRaskMail();
    modelBuilder.AddRaskCache();
    modelBuilder.AddRaskAuth();
    modelBuilder.ApplyRaskConventions();      // always last
}
```

The first migration `rask new` applied already created those tables, so there is nothing to migrate.

## 3. Enqueue from your code

Inject `IJob` into chapter 3's `CreateOrder` page in `Features/Orders/CreateOrder.cs`, and enqueue right
after the order is saved. `SendOrderReceipt` lives in `Features/Shared/`, so the page also needs
`using Shop.Features.Shared;`:

```csharp
public sealed partial class CreateOrder(IJob jobs, Navigator navigator) : Component
{
    // … the fields and Render() are unchanged …

    private async Task SaveAsync(OrderModel model)
    {
        try
        {
            var order = await Order.CreateAsync(model, CancellationToken);
            await jobs.EnqueueAsync(new SendOrderReceipt(order.Id), CancellationToken);   // ← enqueue
            navigator.NavigateTo(Routes.OrdersPage());
        }
        catch (Exception)
        {
            _error = "Something went wrong — please try again.";
        }
    }
}
```

`CreateAsync` hands back the saved `Order`, its `Id` included. `EnqueueAsync` returns as soon as the job row
is written — the customer's request finishes immediately, and the worker runs the job moments later. Need it
*later*? `ScheduleAsync(job, TimeSpan.FromHours(24))` or `ScheduleAsync(job, aDateTimeOffset)`. Need it
*repeatedly*? Register a recurring job in the same `AddRaskJobs` options:
`o.AddRecurring<PurgeStaleCarts>("purge-carts", every: TimeSpan.FromHours(1), () => new PurgeStaleCarts())`.

> **Two writes, not one.** The order and the job are saved separately, so a crash between them keeps the
> order and loses its receipt. For a receipt that is a real gap, and [Chapter 7](07-outbox-events.md)
> closes it.

## Verify

- Placing an order returns instantly and a row appears in the jobs table.
- Add a `Console.WriteLine` (or a breakpoint) in `SendOrderReceiptHandler.HandleAsync` — it fires within
  `PollInterval` of the order being created.
- Throw from the handler once and watch it retry (up to `MaxAttempts`) rather than losing the work.

**Learn more:** [background jobs](../jobs.md)

Next → **[Chapter 5: Transactional email](05-email.md)**
