# Rask.Jobs — durable background jobs on your database

> **In practice:** [Tutorial Ch 4](tutorial/04-background-jobs.md) · recipe [run work off the request thread](recipes.md#run-work-off-the-request-thread) · [cheat sheet](cheatsheet.md).

`Rask.Jobs` runs **background work off the request thread**, stored in the app's own database — no message
broker, no Redis. Enqueue a job and a hosted worker runs it later, **at-least-once**, with exponential-backoff
retries; it also runs **delayed** and durable **interval-recurring** jobs. [Tutorial chapter
4](tutorial/04-background-jobs.md) builds one end to end.

> Included in [`Rask.Server`](../README.md) — nothing to install. It is **on**; an app that does without it says so:
>
> ```csharp
> app.Configure(c => c.Jobs.Off());
> ```

## Why background jobs

Some work shouldn't happen inline with a request — sending a welcome email, generating a report, calling a
slow third-party API. You want to return to the user immediately and do the work in the background, **durably**:
if the process restarts, the work isn't lost, and a transient failure is retried rather than dropped.

`Rask.Jobs` persists each job to a table in your database and a hosted worker polls it, so there's nothing else
to run. A job is a record marked `IJob`, and the work is an `IJobHandler<TJob>` — one per job type, found at
build time, nothing to register. The worker hands each job it takes off the table to its handler.

> **Under the hood a job travels as a [Rask.Cqrs](cqrs.md) command.** `IJob` derives from `ICommand` and
> `IJobHandler<TJob>` from `ICommandHandler<TJob>`, so a job gets the dispatcher's pipeline (validation,
> behaviors, `[Authorize]`) and its one-handler-per-type rule ([RASK028](diagnostics.md#rask028)). That is
> also why a handler written as `ICommandHandler<TJob>` keeps working — it is the same registration under
> the older name. Write `IJobHandler`; it says what the class is for. A job is never reachable over HTTP:
> `IJob` is `[LocalOnly]`.

## Use

```csharp
public sealed record SendWelcomeEmail(string Email, string Name) : IJob;

public sealed class SendWelcomeEmailHandler : IJobHandler<SendWelcomeEmail>
{
    public async Task Handle(SendWelcomeEmail job) =>
        await Mail.Send(Email.To(job.Email).Subject("Welcome").Body(new WelcomeEmail(job.Name)));
}

// Program.cs
builder.Services.AddRaskCqrs();
builder.Services.AddRaskJobs<AppDbContext>(o =>
{
    o.Run<PurgeStaleCarts>().Every(1.Hour);
    o.Run<NightlyBackup>().Daily.At(3, 0);
    o.Run<WeeklyDigest>().Weekly.On(DayOfWeek.Monday).At(9, 0);
    o.Run<CloseBooks>().Monthly.On(1).At(6, 0);
});

builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite("Data Source=app.db"));
```

With `RaskApp` the same steps go straight on the battery — `app.Configure(c => c.Jobs.Run<PurgeStaleCarts>().Every(1.Hour))`.
A schedule is code, so `Run<TJob>()` stays in the callback. The plain values live in `Rask:Jobs`, which the
callback runs after:

```jsonc
// appsettings.json
{
  "Rask": {
    "Jobs": {
      "PollInterval": "00:00:05",
      "MaxAttempts": 25,
      "TimeZone": "Europe/Budapest"   // what ".Daily.At(3, 0)" means; UTC unless you say otherwise
    }
  }
}
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    modelBuilder.AddRaskJobs();   // maps the Job + RecurringJobState tables
}
```

Add a migration for the new tables before running — `rask db add AddJobs && rask db update`
(or `dotnet ef migrations add AddJobs` directly). Then enqueue from anywhere — a handler, a render, a
request, another job — with nothing injected:

```csharp
await Jobs.Enqueue(new SendWelcomeEmail(user.Email, user.Name));   // as soon as the processor polls
await Jobs.Enqueue(new SendReminder(order.Id)).In(24.Hours);       // tomorrow
await Jobs.Enqueue(new CloseBooks()).At(monthEnd);                 // at a moment you name
```

## Lanes

A job normally waits its turn behind the others. Name a **lane** and it waits only behind its own:

```csharp
await Jobs.Enqueue(new SendChips(pump.Id)).InLane(pump.Id);   // ten minutes at the pump
await Jobs.Enqueue(new AskStatus(pump.Id)).InLane(pump.Id);   // waits for the chips - this pump only
await Jobs.Enqueue(new AskStatus(other.Id)).InLane(other.Id); // runs now, beside them
```

- Jobs of **one lane** run one at a time, in the order they are due (a retry goes by its new `RunAt`). Across
  instances too: a lane with a job in flight anywhere is not claimed again until that job lets go.
- **Different lanes** run side by side, and the poll goes on while they do.
- A lane is a name on the row, not something to set up: one per pump, per customer, per mailbox.
- A job with **no lane** runs as before - a batch, one after the other, one batch at a time per instance.
- The lease is what keeps a lane's place; the running instance renews it while the job runs, so a long job keeps it.

A recurring job has no lane of its own; let it enqueue the laned jobs (`foreach pump -> InLane(pump.Id)`).
Upgrading adds the `Lane` column: `rask db add AddJobLane && rask db update`.

### Unless the same work already waits

A round that asks every pump for its status should not pile a second question behind one that has not been
asked yet. Add `.UnlessWaiting()` and the await says whether a row was written:

```csharp
var asked = await Jobs.Enqueue(new AskStatus(pump.Id)).InLane(pump.Id).UnlessWaiting(); // false: one waits already
```

- **The same work** is the same lane, the same tenant, the same job kind and the same serialized payload — the
  job's own properties, nothing else. When it was enqueued, when it is due and who enqueued it do not count, and
  Rask adds no clock or id of its own; a job that carries a timestamp is only the same as one with that timestamp.
- **Only a job nobody has claimed yet counts.** Once a processor takes it - running, or back to wait for a retry
  after a failure or a shutdown - the same work may be enqueued again, and waits in the lane behind it. So a new
  round always queues behind one that is running, and never behind a second copy of one that is not.
- **The job table decides, not the host's memory.** The waiting row carries a key (`Job.WaitingKey`, a SHA-256
  of the above) under a unique index, and the claim clears it. Two hosts enqueueing at once - a blue/green switch,
  a scaled-out app - still leave one waiting row; the loser's await answers `false`.
- **No sweeper, and no key outlives its work.** A key left on a row that was claimed or finished - which is
  what a host still on an older Rask.Jobs leaves during a blue/green switch, since it claims without knowing the
  column - is cleared by the next `.UnlessWaiting()` that meets it, and that enqueue writes its row. The clear
  is conditional and the index still decides, so two hosts meeting the same stale key leave one new waiting row.
- A plain `Enqueue` (no `.UnlessWaiting()`) always writes a row, and its await answers `true`.
- `Jobs.Fake()` keeps the same promise: a copy recorded and not yet `Run()` makes the next one answer `false`.
- Inside [`Db.Transaction`](data.md#a-check-and-the-writes-it-guards-dbtransaction) a collision answers
  `false` and the rest of the transaction still commits — on SQL Server too, where `XACT_ABORT` would doom the
  whole transaction on a refused insert: there the key is locked for the transaction (`sp_getapplock` on
  `rask-job:{key}`) before the look, so the insert never meets the index.

Upgrading adds the `WaitingKey` column and its unique index: `rask db add AddJobWaitingKey && rask db update`.
The column is nullable and every existing row leaves it empty, so adding it rewrites nothing; on SQL Server the
index is filtered to rows that carry a key, so it starts empty, and on SQLite and PostgreSQL the build is one
pass over the table. Until the migration runs, the processor logs exactly that command.

## Inside `Db.Transaction`

A job enqueued inside [`Db.Transaction`](data.md#a-check-and-the-writes-it-guards-dbtransaction) is a row in
that transaction: the processor cannot see it before the commit, and a rollback leaves no job to run.

```csharp
await Db.Transaction(async () =>
{
    var order = await Order.Create(model);
    await Jobs.Enqueue(new ChargeCard(order.Id));   // runs only if the order commits
});
```

The jobs table must live in the same database as the transaction; otherwise the enqueue throws rather than
write a job for work that may roll back.

## How it works

- **`IJobs`** — writes one `Job` row (type name + JSON payload + `RunAt`) through your
  `IDbContextFactory<TContext>`.
- **`JobProcessor<TContext>`** — a hosted `BackgroundService` that polls on `PollInterval` for **due** jobs
  (`RunAt <= now`, oldest first), dispatches each through `IDispatcher` to its `IJobHandler`, and stamps
  `ProcessedAt`. On failure it records the error, increments the attempt count, and pushes `RunAt` out by an
  **exponential backoff** (`BaseRetryDelay × 2^(attempts-1)`, capped at `MaxRetryDelay`), retrying until
  `MaxAttempts` — after which the job is left as a **dead letter** for inspection. A failing job never crashes
  the app, and neither does a failing poll — a transient database error is logged and retried on the next one.
  Each job's outcome is saved on its own, so a row edited or deleted underneath the drain costs that one row
  rather than re-running everything the batch had already executed. Completed jobs are purged after
  `Retention` (default 7 days; `TimeSpan.Zero` keeps them).
- **Recurring** — `o.Run<T>()` plus a cadence (`.Every(1.Hour)`, `.Daily.At(3, 0)`,
  `.Weekly.On(DayOfWeek.Monday).At(9, 0)`, `.Monthly.On(1).At(6, 0)`) enqueues a fresh job on each tick,
  tracked durably in `RecurringJobState` under the job's type name — `.Named("purge-carts")` overrides that,
  and two schedules for one job need it. A restart never double-runs a tick, and an interval job that fell
  more than one interval behind restarts from now rather than bursting catch-up runs. A calendar time is read
  in `o.TimeZone` (UTC by default, so a deploy cannot move it), follows daylight saving, and
  `.Monthly.On(31)` runs on a short month's last day. Read the registered schedule back from
  `JobsOptions.RecurringJobs` — join it to the `RecurringJobState` row of the same name to show when each one
  last fired, or call an entry's `Factory()` and enqueue the result to run one off-schedule. A `Run<T>()` left
  without a cadence fails the host's start rather than silently never running.
- **The `Rask.Jobs` source generator** registers every `IJob` type (name → CLR type) at module load, so the
  processor rehydrates a stored job with no runtime `Type.GetType` or assembly scanning.

## The user and tenant a job runs for

A job runs later, on another thread, with nobody signed in — but its handler usually needs to know whom the
work is for. So the `Job` row records the user in flight when it was enqueued (`Current.UserId`, into
`Job.UserId`) and, in an app with [multi-tenancy](multi-tenancy.md), the tenant. The processor re-enters both
before it calls the handler, so the handler reads [`Current.UserId`](data.md#the-current-user--current) and
filters tenant-scoped tables exactly as the page that enqueued it would have. A job enqueued by the host
itself — a recurring job, work at startup — records nobody, and runs for nobody.

**Upgrading adds a column.** The `UserId` column is new; generate and apply its migration —
`rask db add AddJobUser && rask db update`. Until then the processor logs exactly that, instead of a generic
failure, once it loads a pending job.

### Signed in as that user, never with more rights

The handler also runs **signed in** as that user, so `Current.Principal`, an `[Authorize]` check and a page
rendered to print ([`Pdf.From`](pdf.md)) all see them — with the rights they had when they enqueued the job and
still have now, never one more:

- At enqueue the row records the user's rights — their roles and every claim that is not who they are — in
  `Job.UserClaims`.
- At run time their principal is rebuilt from their account as it is **now** (the auth battery supplies it), and
  only the rights present in both remain. A role granted since is not the job's; a role revoked since is gone.
- A user whose account no longer exists runs signed in as nobody. A job enqueued under `Current.UseUser(other)`
  carries none of the signed-in user's rights to that other user.
- An app without the auth battery has no account to ask; its jobs run with the recorded rights alone.

**Upgrading adds a column.** `UserClaims` is new and nullable: `rask db add AddJobUserClaims && rask db update`.
A job enqueued before the migration runs signed in with no rights beyond being that user. Until the migration
runs, the processor logs exactly that command.

`Job` and `RecurringJobState` are Rask.Data entities with read faces, so the queue can be queried with no
context of your own — `Job.Where(j => j.ProcessedAt == null).Count()` for the backlog. The
[dashboard](dashboard.md) shows the same table, with retry.

## Shutdown

On `SIGTERM` — a redeploy, a container recycle, `Ctrl+C` — the processor stops claiming **new** jobs at once,
and does not start the next job of a batch either. The job already inside your handler is **not** cancelled:
its token stays live until the host's own shutdown budget (`HostOptions.ShutdownTimeout`) runs out.
`rask deploy` sizes that budget from the app's `stopTimeout` in `.rask/deploy.json` (default `30m`), and
waits that long for the old container to exit — so a job that is sending 1282 settings to a fuel pump
finishes on the container that started it, while the new container already serves every request.

While it waits the processor logs, every 10 seconds, which jobs it is waiting for —
`Stopping: no new job is claimed; waiting for 1 running job(s) to finish: AtPump (3m12s)` — and `rask deploy`
shows that line as it waits. It also keeps renewing the lease of a running job, so the new container never
picks the same job up a second time, however long it runs.

**The job author's contract:**

- **Honour the token.** `Current.Cancellation` (or the token your handler is given) is cancelled only when the
  stop budget ends. Pass it to every awaited call, and stop cleanly when it fires.
- **Make the job idempotent.** A job cancelled at the end of the budget — or killed with the container — re-runs
  *whole* on the next boot, not from where it stopped. It does **not** count a failed attempt (a redeploy is not
  a failure); `rask.jobs.interrupted` counts these, and a warning is logged.
- **Size the budget to the longest job.** A job that can run for 20 minutes needs a `stopTimeout` above that.
  The deploy then waits that long at most; a job that finishes sooner lets the old container go sooner.

`JobsOptions.ShutdownGracePeriod` (default `Timeout.InfiniteTimeSpan`) cancels a running job that long after
`SIGTERM` instead, for an app that would rather cut its jobs short; it cannot outlast the host's budget, and
`TimeSpan.Zero` cancels immediately.

> The processor [leases](scaling.md#running-more-than-one-instance) the jobs it claims, renews the lease while a
> job runs, and hands it back when it stops, which is why an interrupted job is immediately eligible again
> rather than waiting out its lease after a redeploy; a processor that dies mid-job releases it when the lease
> runs out.

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
  often does not land and the lease expiry is what recovers the batch.
- **A job type must be a concrete, non-generic type the generated registry can name** — that is how a stored
  job is rehydrated without reflection. Skipped shapes: generic (or nested inside a generic), `file`-local,
  and `private`/`protected` at any level of its containing chain. Each is reported at build time as
  [RASK035](diagnostics.md#rask035), so a job that could never be dispatched fails the build instead of
  dead-lettering in production. An abstract base carrying `IJob` is skipped silently; its concrete
  derivatives register as usual. Nesting inside a plain `static class` is fine, and the usual way to group
  a feature's jobs.
- **Running more than one instance is safe.** Each processor *leases* the batch it claims, so a job goes to
  exactly one instance. See [running more than one instance](scaling.md#running-more-than-one-instance) for
  what the lease does and does not guarantee. On SQLite you will still usually run one instance for the
  unrelated reason that it is single-writer; because an enqueue writes while the processor may also be
  writing, use [`UseRaskSqlite`](sqlite.md) (WAL + a `busy_timeout`) on your context so a concurrent enqueue
  waits for the write lock instead of failing with `SQLITE_BUSY`.
- **`Attempts` counts attempts *started*, not failures.** The claim increments it, so a job that takes the
  process down with it still counts toward `MaxAttempts` instead of being retried forever. A job that
  succeeds first time shows `Attempts = 1`.
- **Jobs vs. the outbox.** A job is *explicitly enqueued*, so its write is its own transaction — it does not
  commit atomically with an unrelated business change. When you need an event delivered atomically *with* a
  data change, raise a domain event and use [`Rask.Outbox`](outbox.md) instead. The two pillars are
  complementary: the outbox delivers events derived from a transaction; jobs run work you schedule.
