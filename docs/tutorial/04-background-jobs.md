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

public sealed record SendOrderReceipt : IJob;

public sealed class SendOrderReceiptHandler : ICommandHandler<SendOrderReceipt>
{
    public Task Handle(SendOrderReceipt job)
    {
        // TODO: do the work.
        return Task.CompletedTask;
    }
}
```

A job is a record marked `IJob` plus the class that does the work. `ICommandHandler<T>` is simply
the interface the job worker hands a job to, and nothing registers the handler: it is found at build time.
Give the job the data it needs by adding a parameter:

```csharp
public sealed record SendOrderReceipt(Guid OrderId) : IJob;
```

Pass the id, not the order. The job runs later — possibly after a restart — and should read the order as it
is *then*.

Fill in the handler with whatever the work is (we'll make it send an email in the next chapter). Reading the
order is the same call a page makes:

```csharp
using Shop.Features.Orders;   // for Order

public sealed class SendOrderReceiptHandler : ICommandHandler<SendOrderReceipt>
{
    public async Task Handle(SendOrderReceipt job)
    {
        var order = await Order.Read.Where(o => o.Id == job.OrderId).FirstOrDefaultAsync(Current.Cancellation);
        // … process the order …
    }
}
```

There's nothing to inject for that read. `Order.Read` opens its own context and disposes it before it
returns, which is as right on a background worker as it is on a page.

## 2. What's already wired

Chapter 1's `rask new` registered jobs for you. Worth reading anyway, because two of these lines are
the ones you'd have to get right by hand.

In `Program.cs` the scaffold wrote `builder.Services.AddRaskJobs<AppDbContext>();`. To tune the worker, give
that same line options:

```csharp
builder.Services.AddRaskJobs<AppDbContext>();
```

Its tuning has defaults, so there is nothing for it in `appsettings.json` yet. To change one, add a
`Rask:Jobs` section:

```jsonc
"Rask": {
  "Jobs": {
    "PollInterval": "00:00:05",   // how often the worker checks for due jobs
    "MaxAttempts": 25             // retry a failing job up to N times
  }
}
```

Both values are the defaults, so this changes nothing until you edit a number. `AddRaskJobs` resolves
`IDbContextFactory<AppDbContext>` — never a scoped `DbContext`, because a live session is long-lived over a
WebSocket and a scoped context would outlive any unit of work. It hands each job to its handler through the
scaffold's `AddRaskCqrs()` line, which is why that line is there.

The jobs tables are mapped in `AppDbContext.OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);       // every aggregate you declared
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    modelBuilder.AddRaskOutbox();
    modelBuilder.AddRaskJobs();               // ← the Job + RecurringJobState tables
    modelBuilder.AddRaskMail();
    modelBuilder.AddRaskCache();
    modelBuilder.AddRaskAuth();
    modelBuilder.ApplyRaskConventions(this);  // always last
}
```

The first migration `rask new` applied already created those tables, so there is nothing to migrate.

## 3. Enqueue from your code

Open chapter 3's `CreateOrder` page in `Features/Orders/CreateOrder.cs` and enqueue right after the order is
saved. Nothing is injected: `Jobs` reaches the queue of the work it runs in, and is cancelled with it.
`SendOrderReceipt` lives in `Features/Shared/`, so the page also needs `using Shop.Features.Shared;`:

```csharp
public sealed partial class CreateOrder(Navigator navigator) : Component
{
    // … the fields and Render() are unchanged …

    private async Task SaveAsync(OrderModel model)
    {
        try
        {
            var order = await Order.CreateAsync(model, cancellationToken: CancellationToken);
            await Jobs.Enqueue(new SendOrderReceipt(order.Id));   // ← enqueue
            navigator.NavigateTo(Routes.OrdersPage());
        }
        catch (Exception)
        {
            _error = "Something went wrong — please try again.";
        }
    }
}
```

`Order.CreateAsync` hands back the saved order, with the `Id` it was given. The enqueue returns as soon as the
job row is written — the customer's request finishes immediately, and the worker runs the job moments later.
Need it *later*? `Jobs.Enqueue(job).In(24.Hours)`, or `.At(aMoment)`. Need it *repeatedly*? Register a
recurring job in the same `AddRaskJobs` options: `o.Run<PurgeStaleCarts>().Every(1.Hour)`, or on the calendar
with `o.Run<NightlyBackup>().Daily.At(3, 00)`.

The job row also records who placed the order, and the worker runs the handler as that user — so a handler
that needs to know whose order it is reads `Current.UserId`, exactly as the page could have, though nobody is
signed in on the worker's thread ([more](../jobs.md#the-user-and-tenant-a-job-runs-for)).

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
