# Rask.Jobs — durable background jobs on your database

> **In practice:** [Tutorial Ch 4](tutorial/04-background-jobs.md) · recipe [run work off the request thread](recipes.md#run-work-off-the-request-thread) · [cheat sheet](cheatsheet.md).

`Rask.Jobs` runs **background work off the request thread**, stored in the app's own database — no message
broker, no Redis. Enqueue a job and a hosted worker runs it later, **at-least-once**, with exponential-backoff
retries; it also runs **delayed** and durable **interval-recurring** jobs. [Tutorial chapter
4](tutorial/04-background-jobs.md) builds one end to end.

> Included in the [`Rask`](../README.md) package — nothing to install. It is **on**; an app that does without it says so:
>
> ```csharp
> app.Configure(c => c.Jobs.Off());
> ```

## Why background jobs

Some work shouldn't happen inline with a request — sending a welcome email, generating a report, calling a
slow third-party API. You want to return to the user immediately and do the work in the background, **durably**:
if the process restarts, the work isn't lost, and a transient failure is retried rather than dropped.

`Rask.Jobs` persists each job to a table in your database and a hosted worker polls it, so there's nothing else
to run. A job **is** a [Rask.Cqrs](cqrs.md) command — you write an ordinary `ICommandHandler<TJob>` to do the
work, and the worker dispatches to it.

## Use

```csharp
public sealed record SendWelcomeEmail(string Email, string Name) : IBackgroundJob;

public sealed class SendWelcomeEmailHandler(IMail mail) : ICommandHandler<SendWelcomeEmail>
{
    public Task HandleAsync(SendWelcomeEmail job, CancellationToken ct) =>
        mail.SendAsync(Email.To(job.Email).Subject("Welcome").Body(new WelcomeEmail(job.Name)), ct);
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
(or `dotnet ef migrations add AddJobs` directly). Then enqueue from anywhere `IJob` is injected:

```csharp
await jobs.EnqueueAsync(new SendWelcomeEmail(user.Email, user.Name));      // run asap
await jobs.ScheduleAsync(new SendReminder(order.Id), delay: TimeSpan.FromHours(24));  // run later
```

## How it works

- **`IJob`** — writes one `Job` row (type name + JSON payload + `RunAt`) through your
  `IDbContextFactory<TContext>`.
- **`JobProcessor<TContext>`** — a hosted `BackgroundService` that polls on `PollInterval` for **due** jobs
  (`RunAt <= now`, oldest first), dispatches each through `IDispatcher` to its `ICommandHandler`, and stamps
  `ProcessedAt`. On failure it records the error, increments the attempt count, and pushes `RunAt` out by an
  **exponential backoff** (`BaseRetryDelay × 2^(attempts-1)`, capped at `MaxRetryDelay`), retrying until
  `MaxAttempts` — after which the job is left as a **dead letter** for inspection. A failing job never crashes
  the app, and neither does a failing poll — a transient database error is logged and retried on the next one.
  Each job's outcome is saved on its own, so a row edited or deleted underneath the drain costs that one row
  rather than re-running everything the batch had already executed. Completed jobs are purged after
  `RetentionPeriod` (default 7 days; `TimeSpan.Zero` keeps them).
- **Recurring** — `AddRecurring<T>(name, every, factory)` enqueues a fresh job on each interval, tracked
  durably in `RecurringJobState`, so a restart never double-runs it (and runs a single catch-up if the app was
  down past the due time). Read the registered schedule back from `JobOptions.RecurringJobs` — join it to the
  `RecurringJobState` row of the same name to show when each one last fired, or call an entry's `Factory()`
  and enqueue the result to run one off-schedule.
- **The `Rask.Jobs` source generator** registers every `IBackgroundJob` type (name → CLR type) at module load, so the
  processor rehydrates a stored job with no runtime `Type.GetType` or assembly scanning.

## Shutdown

On `SIGTERM` — a redeploy, a container recycle, `Ctrl+C` — the processor stops picking up **new** jobs
immediately, but the job already inside your handler gets `JobOptions.ShutdownGracePeriod` (default 5s) to
finish rather than being cancelled mid-call. A job halfway through a `SaveChangesAsync` completes instead of
being torn in two.

Only one job is ever in that window: because no new job starts once the stop signal arrives, shutdown is
extended by at most a single grace period, never one per remaining job.

A job that outlives its grace **is** cancelled, and re-runs from the top on the next boot. It does **not**
count a failed attempt — a redeploy is not a failure, and counting it would march never-failing work toward
its dead letter at the cadence you deploy. `rask.jobs.interrupted` counts these, and a warning is logged;
a nonzero rate means your grace period is shorter than your work.

> **Handlers must be idempotent regardless.** There is no lease, claim or visibility-timeout column — an
> interrupted job re-runs *whole*, not from where it stopped. That is also why an interrupted job is
> immediately eligible again rather than waiting out a lease after a redeploy.

`ShutdownGracePeriod` cannot exceed `HostOptions.ShutdownTimeout`: once that elapses the host stops waiting
for hosted services, so a longer grace silently does not happen. `TimeSpan.Zero` cancels immediately.

## Notes

- **It runs in the browser too, unchanged.** The processor is a hosted service and the store is your EF Core
  database, and both of those now exist on the WASM host: `AddHostedService` starts what it registers (see
  [lifecycle.md](lifecycle.md#hosted-services)), and [`Rask.SQLite.Browser`](sqlite.md#sqlite-in-the-browser-wasm)
  gives a browser app a real SQLite database that survives a reload. So `AddRaskJobs<AppDbContext>()` is the
  same line on both hosts, and a job plus its handler compiles and runs on either:

  ```csharp
  host.Services.AddRaskBrowserSqlite("app");                       // the only browser-specific line
  host.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite(BrowserSqlite.ConnectionString("app")));
  host.Services.AddRaskCqrs();
  host.Services.AddRaskJobs<AppDbContext>();
  ```

  What differs is what the guarantees are worth. The lease still makes a job run on exactly one *runner*, but
  in a browser that means one tab: `Rask.SQLite.Browser` elects a single owner, and the other tabs are not
  peers sharing a queue — they have their own, unpersisted database. Durability is bounded by the snapshot
  interval rather than by `SaveChangesAsync`, so a force-closed tab loses jobs queued since the last one.
  And shutdown is best-effort: the browser does not wait for `pagehide`, so `StopAsync`'s lease hand-back
  often does not land and the lease expiry is what recovers the batch. Working app:
  [`samples/Rask.Example.Wasm.Jobs`](../samples/Rask.Example.Wasm.Jobs).
- **A job type must be a concrete, non-generic type the generated registry can name** — that is how a stored
  job is rehydrated without reflection. Skipped shapes: generic (or nested inside a generic), `file`-local,
  and `private`/`protected` at any level of its containing chain. Each is reported at build time as
  [RASK035](diagnostics.md#rask035), so a job that could never be dispatched fails the build instead of
  dead-lettering in production. An abstract base carrying `IBackgroundJob` is skipped silently; its concrete
  derivatives register as usual. Nesting inside a plain `static class` is fine, and the usual way to group
  a feature's jobs.
- **Running more than one instance is safe.** Each processor *leases* the batch it claims, so a job goes to
  exactly one instance. See [running more than one instance](scaling.md#running-more-than-one-instance) for
  what the lease does and does not guarantee. On SQLite you will still usually run one instance for the
  unrelated reason that it is single-writer; because `EnqueueAsync` writes while the processor may also be
  writing, use [`UseRaskSqlite`](sqlite.md) (WAL + a `busy_timeout`) on your context so a concurrent enqueue
  waits for the write lock instead of failing with `SQLITE_BUSY`.
- **`Attempts` counts attempts *started*, not failures.** The claim increments it, so a job that takes the
  process down with it still counts toward `MaxAttempts` instead of being retried forever. A job that
  succeeds first time shows `Attempts = 1`.
- **Jobs vs. the outbox.** A job is *explicitly enqueued*, so its write is its own transaction — it does not
  commit atomically with an unrelated business change. When you need an event delivered atomically *with* a
  data change, raise a domain event and use [`Rask.Outbox`](outbox.md) instead. The two pillars are
  complementary: the outbox delivers events derived from a transaction; jobs run work you schedule.
