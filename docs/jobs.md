# Rask.Jobs — durable background jobs on your database

`Rask.Jobs` runs **background work off the request thread**, stored in the app's own database — no message
broker, no Redis. Enqueue a job and a hosted worker runs it later, **at-least-once**, with exponential-backoff
retries; it also runs **delayed** and durable **interval-recurring** jobs. Scaffold one with
`rask generate job <Name>`.

```bash
dotnet add package Rask.Jobs
```

## Why background jobs

Some work shouldn't happen inline with a request — sending a welcome email, generating a report, calling a
slow third-party API. You want to return to the user immediately and do the work in the background, **durably**:
if the process restarts, the work isn't lost, and a transient failure is retried rather than dropped.

`Rask.Jobs` persists each job to a table in your database and a hosted worker polls it, so there's nothing else
to run. A job **is** a [Rask.Cqrs](cqrs.md) command — you write an ordinary `ICommandHandler<TJob>` to do the
work, and the worker dispatches to it.

## Use

```csharp
public sealed record SendWelcomeEmail(Guid UserId) : IJob;

public sealed class SendWelcomeEmailHandler(IEmailSender email) : ICommandHandler<SendWelcomeEmail>
{
    public Task HandleAsync(SendWelcomeEmail job, CancellationToken ct) => email.SendWelcomeAsync(job.UserId, ct);
}

// Program.cs
builder.Services.AddRaskCqrs();
builder.Services.AddRaskJobs<AppDbContext>(o =>
{
    o.PollInterval = TimeSpan.FromSeconds(5);
    o.MaxAttempts = 25;
    o.AddRecurring<PurgeStaleCarts>("purge-carts", every: TimeSpan.FromHours(1), () => new PurgeStaleCarts());
});

builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite("Data Source=app.db"));
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    modelBuilder.AddRaskJobs();   // maps the Job + RecurringJobState tables
}
```

Add a migration for the new tables before running — `rask db add AddJobs && rask db update`
(or `dotnet ef migrations add AddJobs` directly). Then enqueue from anywhere `IJobQueue` is injected:

```csharp
await jobs.EnqueueAsync(new SendWelcomeEmail(user.Id));                    // run asap
await jobs.ScheduleAsync(new SendReminder(order.Id), delay: TimeSpan.FromHours(24));  // run later
```

## How it works

- **`IJobQueue`** — writes one `Job` row (type name + JSON payload + `RunAt`) through your
  `IDbContextFactory<TContext>`.
- **`JobProcessor<TContext>`** — a hosted `BackgroundService` that polls on `PollInterval` for **due** jobs
  (`RunAt <= now`, oldest first), dispatches each through `IDispatcher` to its `ICommandHandler`, and stamps
  `ProcessedAt`. On failure it records the error, increments the attempt count, and pushes `RunAt` out by an
  **exponential backoff** (`BaseRetryDelay × 2^(attempts-1)`, capped at `MaxRetryDelay`), retrying until
  `MaxAttempts` — after which the job is left as a **dead letter** for inspection. A failing job never crashes
  the app. Completed jobs are purged after `RetentionPeriod` (default 7 days; `TimeSpan.Zero` keeps them).
- **Recurring** — `AddRecurring<T>(name, every, factory)` enqueues a fresh job on each interval, tracked
  durably in `RecurringJobState`, so a restart never double-runs it (and runs a single catch-up if the app was
  down past the due time).
- **The `Rask.Jobs` source generator** registers every `IJob` type (name → CLR type) at module load, so the
  processor rehydrates a stored job with no runtime `Type.GetType` or assembly scanning.

## Notes

- **Server-side.** The processor is a hosted service and the store is your EF Core database — this is not a
  browser/WASM concern.
- **A job type must be a concrete, non-generic type** — the source generator registers concrete `IJob`
  implementations (open-generic and abstract types are skipped), which is how a stored job is rehydrated.
- **SQLite is single-writer**, so the processor polls and claims sequentially (there is no `SKIP LOCKED` to
  lean on). Run **one processor per app**. Because `EnqueueAsync` writes while the processor may also be
  writing, use [`UseRaskSqlite`](sqlite.md) (WAL + a `busy_timeout`) on your context so a concurrent enqueue
  waits for the write lock instead of failing with `SQLITE_BUSY`.
- **Jobs vs. the outbox.** A job is *explicitly enqueued*, so its write is its own transaction — it does not
  commit atomically with an unrelated business change. When you need an event delivered atomically *with* a
  data change, raise a domain event and use [`Rask.Outbox`](outbox.md) instead. The two pillars are
  complementary: the outbox delivers events derived from a transaction; jobs run work you schedule.
