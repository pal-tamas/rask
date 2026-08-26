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
    public Task HandleAsync(SendOrderReceipt job, CancellationToken cancellationToken)
    {
        // TODO: do the work.
        return Task.CompletedTask;
    }
}
```

A job is just a record marked `IJob` plus an `ICommandHandler<T>` — the same handler shape the CRUD slices
use. Give the job the data it needs by adding a parameter:

```csharp
public sealed record SendOrderReceipt(Guid OrderId) : IJob;
```

Fill in the handler with whatever the work is (we'll make it send an email in the next chapter):

```csharp
using Microsoft.EntityFrameworkCore;   // for IDbContextFactory

public sealed class SendOrderReceiptHandler(IDbContextFactory<AppDbContext> dbFactory)
    : ICommandHandler<SendOrderReceipt>
{
    public async Task HandleAsync(SendOrderReceipt job, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.Orders.FindAsync([job.OrderId], ct);
        // … process the order …
    }
}
```

## 2. What's already wired

Chapter 1's `rask new` registered jobs for you. Worth reading anyway, because two of these lines are
the ones you'd have to get right by hand.

In `Program.cs`:

```csharp
builder.Services.AddRaskJobs<AppDbContext>(o =>
{
    o.PollInterval = TimeSpan.FromSeconds(5);   // how often the worker checks for due jobs
    o.MaxAttempts  = 25;                        // retry a failing job up to N times
});
```

`AddRaskJobs` needs `AddRaskCqrs()` to dispatch jobs to their handlers, and it resolves
`IDbContextFactory<AppDbContext>` — never a scoped `DbContext`, because a live session is long-lived over a
WebSocket and a scoped context would outlive any unit of work. Both are already in place.

The jobs table is mapped in `AppDbContext.OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    modelBuilder.ApplyRaskConventions();
    modelBuilder.AddRaskJobs();               // ← the Jobs table
}
```

Then migrate:

```bash
rask db add AddJobs
rask db update
```

## 3. Enqueue from your code

Inject `IJobQueue` into the generated `CreateOrderCommandHandler` in `Features/Orders/CreateOrder.cs` and
enqueue right after the order is saved:

```csharp
public sealed class CreateOrderCommandHandler(
    IDbContextFactory<AppDbContext> dbContextFactory,
    IJobQueue jobs) : ICommandHandler<CreateOrderCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        var entity = Order.Create(command.Request.Total, command.Request.ProductId, command.Request.Placed);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.Orders.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        await jobs.EnqueueAsync(new SendOrderReceipt(entity.Id), cancellationToken);   // ← enqueue
        return entity.Id;
    }
}
```

`EnqueueAsync` returns as soon as the row is written — the customer's request finishes immediately, and the
worker runs the job moments later. Need it *later*? `ScheduleAsync(job, TimeSpan.FromHours(24))` or
`ScheduleAsync(job, aDateTimeOffset)`. Need it *repeatedly*? Register a recurring job at startup:
`o.AddRecurring<PurgeStaleCarts>("purge-carts", every: TimeSpan.FromHours(1), () => new PurgeStaleCarts())`.

## Verify

- After `rask db update`, placing an order returns instantly and a row appears in the jobs table.
- Add a `Console.WriteLine` (or a breakpoint) in `SendOrderReceiptHandler.HandleAsync` — it fires within
  `PollInterval` of the order being created.
- Throw from the handler once and watch it retry (up to `MaxAttempts`) rather than losing the work.

**Learn more:** [background jobs](../jobs.md) · [CQRS](../cqrs.md)

Next → **[Chapter 5: Transactional email](05-email.md)**
