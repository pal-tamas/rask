# Rask.Jobs

**Durable background jobs** for a Rask app — enqueued work that runs off the request thread, stored in the
app's own database, with no broker or Redis.

- Mark a type **`IBackgroundJob`** and it dispatches to an ordinary [Rask.Cqrs](https://www.nuget.org/packages/Rask.Cqrs)
  **`ICommandHandler<TJob>`** — a job *is* a command executed later.
- A background **`JobProcessor`** polls the `Job` table and runs each due job — **at-least-once**, with
  **exponential-backoff** retries up to `MaxAttempts` (then left as a dead letter for inspection).
- **Delayed** (`ScheduleAsync(job, delay)`) and durable **interval-recurring** (`AddRecurring<T>(name, every, …)`)
  jobs — recurring runs are tracked in the DB, so a restart never double-runs them. Read the schedule back
  from `JobOptions.RecurringJobs`.
- **Metrics** on the `Rask.Jobs` meter: processed / failed / **dead-lettered** counters, a duration
  histogram, and pending / dead-letter gauges. `rask.jobs.deadletters` is the one to alert on.
- A **source generator** registers every `IBackgroundJob` type for reflection-free rehydration on the run path.

## Use

```csharp
public sealed record SendWelcomeEmail(Guid UserId) : IBackgroundJob;

public sealed class SendWelcomeEmailHandler(IEmailSender email) : ICommandHandler<SendWelcomeEmail>
{
    public Task HandleAsync(SendWelcomeEmail cmd, CancellationToken ct) => email.SendWelcomeAsync(cmd.UserId, ct);
}

// Program.cs
builder.Services.AddRaskCqrs();
builder.Services.AddRaskJobs<AppDbContext>(o =>
    o.AddRecurring<PurgeStaleCarts>("purge-carts", every: TimeSpan.FromHours(1), () => new PurgeStaleCarts()));

// AppDbContext.OnModelCreating:  modelBuilder.AddRaskJobs();
// then:  rask db add AddJobs && rask db update
```

```csharp
// enqueue from anywhere IJob is injected:
await jobs.EnqueueAsync(new SendWelcomeEmail(user.Id));
await jobs.ScheduleAsync(new SendReminder(order.Id), delay: TimeSpan.FromHours(24));
```

Register your context as an `IDbContextFactory<AppDbContext>` (Rask Server sessions are long-lived).
Several instances is safe: each processor **leases** the batch it claims, so a job runs on exactly one of
them. On SQLite you will still usually run one, because SQLite is single-writer, so the processor claims
work by polling and writing sequentially. Need a job to commit atomically with a business change? Raise a domain event and deliver it with
[Rask.Outbox](https://www.nuget.org/packages/Rask.Outbox) instead — the two pillars are complementary.
