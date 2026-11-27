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
        await Mail.Send(Mail.Message.To(job.Email).Subject("Welcome").Body(WelcomeEmail.Name(job.Name)));
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

### One tenant at a time: `.InTenantLane()`

A partner's loads must not run two at once — one may read what the other is writing — but another partner's loads
should not wait for them. Before, an app kept its own per-partner turn beside the queue. Now it is one step:

```csharp
await Jobs.Enqueue(new ImportFile(file.Id)).InTenantLane("loads");   // this tenant's loads, one at a time
await Jobs.Enqueue(new Redistribute(company)).InTenantLane();        // this tenant's one lane
var loads = await Jobs.Lane(Jobs.TenantLane("loads"));               // what runs and waits in it
```

- The lane is named after the **tenant in flight** where the job is enqueued — `loads/{tenant}` (`tenant/{tenant}`
  without a name) — so it is an ordinary lane: one at a time on every host, in the order due, beside every other lane.
- It is on `Enqueue`, a batch (`Jobs.Enqueue(jobs).InTenantLane("loads")`) and `Jobs.Run`. `Jobs.TenantLane("loads")`
  names the same lane for `Jobs.Lane(…)` and `Jobs.Lanes(…)`.
- **With no tenant in flight the step throws**, naming the call — there would be no tenant to take turns for. Name a
  lane of your own with `.InLane(…)` there.
- A job runs in one lane: `.InTenantLane(…)` and `.InLane(…)` replace each other, the last one written wins.

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
- Inside [`Db.Transaction`](domain.md#a-check-and-the-writes-it-guards-dbtransaction) a collision answers
  `false` and the rest of the transaction still commits — on SQL Server too, where `XACT_ABORT` would doom the
  whole transaction on a refused insert: there the key is locked for the transaction (`sp_getapplock` on
  `rask-job:{key}`) before the look, so the insert never meets the index.

#### Named by a key: `.UnlessWaiting(key)`

A job that carries who asked, or when, is never byte-for-byte the same as the one already waiting — yet two presses of
"redistribute" for one company are one piece of work. Name the work yourself:

```csharp
var queued = await Jobs.Enqueue(new Redistribute(company, Current.UserId)).UnlessWaiting(company.ToString());
```

- **The same work** is then the same lane, tenant, job kind and **key** — the payload no longer counts. Two kinds of
  job never share a key, and a keyed job is never the same as an unkeyed copy of itself.
- Everything else is `.UnlessWaiting()`'s: only an unclaimed copy counts, the unique index decides across hosts, and
  `Jobs.Fake()` keeps the same promise. A key is at most 1,024 characters; it is hashed, so it costs the row nothing.

Upgrading adds the `WaitingKey` column and its unique index: `rask db add AddJobWaitingKey && rask db update`.
The column is nullable and every existing row leaves it empty, so adding it rewrites nothing; on SQL Server the
index is filtered to rows that carry a key, so it starts empty, and on SQLite and PostgreSQL the build is one
pass over the table. Until the migration runs, the processor logs exactly that command.

### An aggregate's handler as a job

An aggregate's [`static Task On(E e)`](outbox.md#a-handler-on-the-aggregate) runs where the outbox runs it, right
after the commit. When it runs long — a file import, a pull from a partner — or one tenant's must run one at a time,
declare it a job in the aggregate's `Configure`:

```csharp
public sealed partial class QueuedFile : Aggregate<int, PerTenant>
{
    static Task On(FileQueued e) => QueuedFile.Update(e.File, f => f.Load());

    public static void Configure(EntityTypeBuilder<QueuedFile> b) =>
        b.Job<FileQueued>().InTenantLane("loads").UnlessWaiting(e => e.File.ToString());
}
```

- The outbox's row for the handler then only **enqueues a job**, and the job runs `On` — with everything a job has:
  its lane, its lease renewed while it runs (so a long one is never picked up twice), a dead letter when its host
  died on its last attempt, the dashboard's Retry.
- The steps are the enqueue's, read off the event: **`.InTenantLane(lane?)`** — the lane of the tenant the event was
  raised for — or **`.InLane(e => …)`**, and **`.UnlessWaiting(e => key)`**: a second event for work that already
  waits enqueues nothing. The key is the handler's own, so two aggregates' handlers never share one.
- Without the step the handler is not a job, and `b.Job<E>()` for an event the aggregate has no `On` for is
  [RASK189](diagnostics.md#rask189). The job runs as the tenant and user the event was raised for, as the handler would.

### Await a job's answer: `Jobs.Run`

Some work belongs in a lane **and** has a caller waiting for it — an operator presses "send to pump" and wants to see
the pump's answer, but nothing else may talk to that pump while it is busy. `Jobs.Run` stores the job and runs it like
any lane job, and the await hands back what its handler answered:

```csharp
public sealed record AtPump(Guid Pump, Settings Settings) : IJob<Done>;          // a job that answers a Done

public sealed class AtPumpHandler : IJobHandler<AtPump, Done>
{
    public async Task<Done> Handle(AtPump job) => await PumpLink.Send(job.Pump, job.Settings, Current.Cancellation);
}

var done = await Jobs.Run(new AtPump(pump.Id, settings)).InLane(pump.Id);        // waits for this pump's turn, then for the answer
```

Before, the same thing took a named lock and a queue of your own — `sp_getapplock` on a raw connection, held while the
work ran, and a second mechanism for the work that did not wait. Now it is one lane: the awaited job and the enqueued
ones for that pump take turns, on every host.

- **It is a job.** A row in the `Job` table, in its lane, one at a time with every other job of that lane on every host,
  run as the user and tenant who awaited it. A job of `IJob<TResult>` answers a `TResult`; one of plain `IJob` can be
  awaited too — `await Jobs.Run(new Calibrate(pump.Id)).InLane(pump.Id)` returns once it has run.
- **The answer travels on the row** (`Job.Result`, JSON), so the host that awaits need not be the host that runs it.
  In process the processor is woken the moment the job is written and says the moment it is saved; across hosts the
  caller reads the row every `PollInterval`.
- **Tried once.** A failure throws `JobFailedException` at the caller, its message the error the handler threw, and the
  row is a dead letter at once — never retried later behind the back of a caller who was told it failed and may run it
  again. (A shutdown that interrupts it is not a failure: it runs again on the next boot, as any job does.)
- **Cancelling the wait cancels the wait, not the job.** The caller's token — or the ambient one of the request or
  render it runs in — stops the await with an `OperationCanceledException`; the job still runs, and keeps its answer on
  its row.
- **`.Within(2.Minutes)` bounds the wait the same way** and throws `TimeoutException`; the job still runs. There is no
  default bound: the wait ends with the work it runs in.
- **If the awaiting process dies, the job still runs**, its answer kept on its row until `Retention` purges it. Nobody
  hears it; design the job so that is fine, as you would any job.
- **It cannot wait for itself.** A job awaiting a job of its own lane would wait forever — the lane runs one at a time —
  so it throws `InvalidOperationException` instead; so does `Jobs.Run` inside `Db.Transaction`, whose row nobody could
  see before the commit that waits for it. Enqueue the job there instead.
- `Jobs.Fake()` runs an awaited job at once through its real handler and hands back its answer (or lets its exception
  out); `jobs.Enqueued<AtPump>().InLane(id).Once()` sees it, and `jobs.Run()` does not run it again.

Upgrading adds the `Awaited` and `Result` columns: `rask db add AddJobResult && rask db update`. `Awaited` defaults to
false in the database too, so a host still on an older Rask.Jobs during a blue/green switch keeps enqueuing ordinary
jobs. Until the migration runs, the processor logs exactly that command.

### Stop a job, give it a limit, read a lane

A job that hangs holds its lane for as long as the process lives — a pump that stopped answering keeps every other
job of that pump waiting. Three things end it, and none of them is a failure: the job **ends stopped**, its claim and
its lane are given back at once, the lane's next job runs on the same host, and it is never tried again.

```csharp
// By hand — a button, an admin page, the dashboard's Stop. By the row's id (a Guid), on whichever host runs it:
await Jobs.Stop(lane.Running!.Id);                                    // false: already done, already stopped, or no such job

// By itself, at a time limit and when it reports no progress — Program.cs, beside the schedules:
app.Configure(c => c.Jobs.Limit<AtPump>().After(30.Minutes).IdleFor(5.Minutes));
app.Configure(c => c.Jobs.Limit<AtPump>().After(job => job.Task == PumpTask.Status ? 1.Minute : 15.Minutes));

// Inside the handler, every report is progress, and restarts the IdleFor clock:
Jobs.Report($"{kept} of {total} fuelings");
```

- **Stopping cancels `Current.Cancellation`** — the job's own token, never another job's. Work that passes it on
  stops by itself; there is nothing else to write. A handler that wants to say *why* on its own board reads
  `Jobs.Stopped` (`JobStop.Asked`, `TimeLimit`, `Idle`, `DoneElsewhere`; null while it runs on):
  ```csharp
  catch (Exception) when (Jobs.Stopped is { } why) { board.Say(job.Pump, why); throw; }
  ```
- **Whatever it throws or returns after the stop is the stop.** A database call that gave up on the cancelled token
  is not a failure, and a handler that swallows the cancellation and returns still ends stopped. Nothing is retried,
  nothing is dead-lettered, and no `Error` is written. Work it saved before the stop stays saved.
- **The first reason stands.** A second `Jobs.Stop`, or a limit passed after it, answers false and changes nothing.
- **Who may stop a job is the app's to decide.** `Jobs.Stop` checks no rights and sees every tenant's jobs, as the
  processor does — so put it behind the same check as the button that calls it (an admin role, the job's owner).
- **A waiting job never runs.** `Jobs.Stop` on a job still in its lane ends it at once; the job running in front of it
  runs on. Stopping by id rather than "whatever runs in the lane now" means a click that lands just as one job ends
  never stops the next.
- **An awaited job throws `JobStoppedException`** at its `await Jobs.Run(job)` — an `OperationCanceledException` that
  carries `.Stop`, never a `JobFailedException`.
- **The limits are measured from when the handler starts**, so time waiting in the lane never counts. `.After(…)`
  holds however much the job reports; `.IdleFor(…)` cuts a job at exactly that long without a `Jobs.Report` (not a
  second before). A type with no `IdleFor` is never cut for being quiet. `Jobs.Report` keeps only its latest words,
  writes them to the job's row at most once a poll ([below](#show-a-job-to-the-person-who-started-it)), and does
  nothing outside a job.
- **No job has a time limit unless you give it one.** `JobsOptions.StopAfter` (`Rask:Jobs:StopAfter`, default
  infinite) is the limit of every job whose type names none — opt in; a backup that runs all night is not cut off by a
  default nobody chose. Per type, configuration says the same as code — `Rask:Jobs:Limit:AtPump:After` and
  `Rask:Jobs:Limit:AtPump:IdleFor`, by the short type name — and code wins, part by part.
- **Marked done or deleted from outside, a running job stops** within one `PollInterval`: the host looks at the rows of
  the jobs it runs, by id, on every poll, so the same work enqueued again is never taken for it. The row is left as
  whoever ended it left it (`JobStop.DoneElsewhere` is never written), and the dashboard's Delete of a running row now
  really stops its work. Until that look, the lane's next job waits on this host, so two jobs of a lane never run at
  once here; across hosts the gap is that one poll.
- **A handler that ignores its token holds its lane** until it returns — one at a time is what a lane is for. The
  dashboard shows it as `stopping`, and the processor logs `JobIgnoresStop` once a minute, naming it.
- **Shutdown is not a stop.** A job cut short by the end of the host's budget re-runs on the next boot, as
  [below](#shutdown); `Jobs.Stopped` is null in it.

What runs in a lane, and what waits behind it, is one query for any number of lanes:

```csharp
var lanes = await Jobs.Lanes(pumpIds);                                // every lane asked for is answered, idle or not
var lane = lanes[pump.Id];
if (lane.Running is { Job: AtPump now, Stopping: false } running) { … }  // what runs, its Id, Started, Attempts
foreach (var waiting in lane.Waiting) { … }                           // what waits, in the order the lane takes it
var one = await Jobs.Lane(pump.Id);
```

A job waiting out a retry's backoff is waiting, with its `Due` ahead; a dead letter, a processed job and a stopped one
are left out; a job whose payload no longer reads is listed with `Job == null` and its `Type`, rather than failing the
view.

Logged: `JobStopped` (on request), `JobStoppedAtLimit` and `JobStoppedIdle` (warnings, with the start and the last
progress), `JobDoneElsewhere`, `JobIgnoresStop`, and `JobWatchFailed` when the poll's look cannot reach the database —
it stops nothing then, and looks again on the next poll. The metric `rask.jobs.stopped` counts stops, tagged
`reason` = `asked`, `time-limit`, `idle` or `done-elsewhere`.

Upgrading adds the `Stopped` and `StartedAt` columns: `rask db add AddJobStop && rask db update`. Until the migration
runs, the processor logs exactly that command.

### Show a job to the person who started it

An import, an export, a report — work a person started and waits for — is shown to them with one step, and moved
with one line from its handler:

```csharp
// Where it starts: the person who enqueues it sees it, under this title.
await Jobs.Enqueue(new ImportPrices(file.Id)).Noticed($"Import of {file.Name}");

// In the handler: how far along it is. Call it as often as the work moves.
Jobs.Report(kept, total);                                     // a bar, filled to kept of total
Jobs.Report(kept, total, $"{kept:N0} of {total:N0} rows");    // with words under it
Jobs.Report("Reading the file");                              // words alone: a bar without an end

// In the layout: the bell at the header's end, and a list where the page wants one.
Ui.AppFrame.Brand("Shop").Notices(Ui.WorkNotices)[ … ]
Ui.WorkList.Lane("imports")
```

Before, both apps that needed this wrote it three times over — a progress table, a board, a bell — around a job that
could only say words nobody else saw. Now the job's row carries its title and its latest count, and the kit draws it.

- **Only its enqueuer sees it, in the tenant they enqueued it in** — unless it is
  [shared](#share-it-with-the-tenant-say-how-it-ended-start-several-at-once). `.Noticed(…)` (on `Enqueue` and on `Run`)
  stores the title on the row beside the user and tenant every job records. The bell lists that user's rows; a job
  enqueued by nobody signed in is shown to nobody. Why a job failed is never shown — it says *Failed*, and the reason stays on
  the [dashboard](dashboard.md).
- **The row is the truth.** `Ui.WorkNotices` and `Ui.WorkList` read the user's rows again every `PollInterval`, so work
  that runs on another host, or ended while nobody looked, is on the screen within one poll. Between reads, the
  `WorkReported` events a job publishes move its bar on the page at once — in this process; events reach
  [one process](subscriptions.md#one-process-for-now).
- **In the browser too.** The browser half of a `wasm-hosted` app (a client of its server, `AddRaskCqrsClient()`) shows
  the same bell: its rows are asked of the server, which reads them for whoever is signed in on the request, and its
  live watch opens on the server, under the same rules. Nothing to wire — Rask.Wasm's batteries do it with the kit and
  the query cache on.
- **A report flood costs nothing.** Only the latest report is kept; it is published at once unless the last one was
  under a quarter second ago, and then on the next poll; the row is written at most once a poll, and only when the job
  reported since. A job looping a million rows may call `Jobs.Report` on every one.
- **What the bell counts** is what runs, and what ended since the person last opened it (in this page's session — no
  storage). Lately ended means the last day, the newest ten. A job they saw running that ends is announced politely to
  a screen reader: *Import of prices.csv is done.*
- **`WorkReported` is an ordinary event** — `(Id, Title, Progress, Done, Of, State, At)` with `WorkState` `Running`,
  `Done`, `Failed` or `Stopped` — so an `IEventHandler<WorkReported>` can mail "your import is done". Who it is for
  travels with it and never across the wire. A board of your own reads the count from a lane: `LaneJob.Progress`,
  `.Done` and `.Of`.
- **In a test**, `Jobs.Fake()` runs a noticed job as the processor would: it publishes running, its latest report and
  how it ended, for the user who enqueued it.

Upgrading adds the `Notice`, `Progress`, `Done` and `Of` columns and an index on `(UserId, ProcessedAt)`:
`rask db add AddJobProgress && rask db update`. Until the migration runs, the processor logs exactly that command.

### Share it with the tenant, say how it ended, start several at once

A partner-wide load is one press that starts several files, which the whole office watches, and which can end with
something to look at rather than plainly done. Before, an app wrote its own queue for that — a table of files, its own
events, its own bell, green/amber/red computed by hand:

```csharp
// Before: the app's own queue, events and bell, beside Rask.Jobs.
foreach (var file in files) db.QueuedFiles.Add(new QueuedFile(partner.Id, file.Name, pressId));
await db.SaveChangesAsync();
await dispatcher.Publish(new LoadMoved(partner.Id, pressId));          // and LoadNotices/LoadProgress to draw it
```

Now it is three steps on the enqueue and one line in the handler:

```csharp
// One press: every file in one save (all or none), shown as one group with one bar, to the tenant.
var queued = await Jobs.Enqueue(files.Select(f => new ImportFile(f.Id)))
    .Noticed(job => $"Import of {job.Name}")
    .ByTenant();

// In the handler: how it ended, in words for the person who started it.
Jobs.Warn("No new rows in prices.csv");          // done, amber: something to look at
Jobs.Refuse("prices.csv has no header row");      // failed for good, never retried
Jobs.Note($"{kept:N0} rows kept");                 // a line under a job that is done

// In the layout: a footer under the bell's list.
Ui.WorkNotices.Actions(Ui.Link.Href(Routes.LoadsPage().Url())["All loads"])
```

- **`.ByTenant()`** (on `Enqueue`, `Run` and a batch, after `.Noticed(…)`) shows the job to its tenant's other users —
  those who may run it themselves: who passes its handler's `[Authorize(Roles = …)]` / `[Authorize(Policy = …)]`, the
  same check a dispatch of it makes. Never to another tenant (the reader's tenant is always the one their session is
  in, never `Tenant.Across()`), never to nobody signed in. Unmarked, a job stays its enqueuer's alone. With no tenant in
  flight `.ByTenant()` throws at the enqueue, naming the call — there would be nobody to share it with.
- **`Jobs.Warn(detail)`** ends the job *warned*: done, with an amber badge and the words under its title.
  **`Jobs.Refuse(detail)`** ends it *failed* for good: it is processed, so it is never tried again and is no dead
  letter; an awaited `Jobs.Run` throws `JobFailedException` with the words. **`Jobs.Note(detail)`** adds words to a job
  that is done. The worst said stands — after a refusal a warning changes nothing — and, like `Jobs.Report`, each does
  nothing outside a job. The words are the handler's to choose; the error a failure threw is still never shown.
- **`Jobs.Enqueue(jobs)`** writes every row in one `SaveChanges` — all of them or, when the write fails, none; inside
  `Db.Transaction` it commits with the work — stamps them with one `Job.Batch`, and answers how many it wrote. It takes
  `.Noticed(job => …)`, `.ByTenant()`, `.InLane(…)`, `.In(…)` and `.At(…)`. The bell draws a batch as one group — when
  it was started and how many it holds, the worst way one ended, one bar for all of it — counts it once among the ten
  lately ended, and raises **one toast** for the person who started it when its last job ends, in the tone of its worst
  end. A colleague's shared batch is shown, never toasted.
- **`WorkReported`** carries `Batch` and `Detail`, and `WorkState` gains `Warned`. A browser tab opened before the deploy
  that added it reads a warned job as done, so nothing breaks across the upgrade.

Upgrading adds the `Shared`, `Batch`, `Outcome` and `Detail` columns and an index on `(TenantId, ProcessedAt)`:
`rask db add AddJobOutcomes && rask db update` (SQLite, SQL Server and PostgreSQL alike). Until the migration runs, the
processor logs exactly that command.

## Inside `Db.Transaction`

A job enqueued inside [`Db.Transaction`](domain.md#a-check-and-the-writes-it-guards-dbtransaction) is a row in
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
  `IDbContextFactory<TContext>`. Its key is a version-7 `Guid` Rask makes before the insert — strictly increasing on
  the host that enqueued it, time-ordered across hosts — never a number the database hands out, so "oldest first"
  and the tie between two jobs due at the same instant are the order they were enqueued in.
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
  and two schedules for one job need it. A new schedule waits for its next occurrence — a nightly backup deployed at
  noon first runs that night, an `.Every(1.Hour)` one an hour after it starts — and `.RunOnFirstStart()` makes it
  catch up at once instead, on the occurrence just gone by. A restart never double-runs a tick, and an interval job that fell
  more than one interval behind restarts from now rather than bursting catch-up runs. A calendar time is read
  in `o.TimeZone` (UTC by default, so a deploy cannot move it), follows daylight saving, and
  `.Monthly.On(31)` runs on a short month's last day. Read the registered schedule back from
  `JobsOptions.RecurringJobs` — join it to the `RecurringJobState` row of the same name to show when each one
  last fired, or call an entry's `Factory()` and enqueue the result to run one off-schedule. A `Run<T>()` left
  without a cadence fails the host's start rather than silently never running.
- **The `Rask.Jobs` source generator** registers every `IJob` type (name → CLR type) at module load, so the
  processor rehydrates a stored job with no runtime `Type.GetType` or assembly scanning.

### Upgrading to `Guid` keys

A database made before Rask keyed its own rows by `Guid` has a `Job` table numbered by the database (`bigint`
identity, `INTEGER` rowid). Its next migration (`rask db add RaskGuidKeys && rask db update`) rebuilds the table: an
[exclusive migration](deployment.md), so `rask deploy` stops the old version before the new one migrates. It refuses
to run while a job is pending — waiting, running or a dead letter: let the queue drain, retry or delete the dead
letters on the dashboard, then deploy. Processed jobs are kept, each with the deterministic id of its `CreatedAt` and
old number, so the bell's recently ended work and the dashboard's history survive. See
[docs/data.md](data.md#rebuilding-a-store-numbered-key-as-a-guid).

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

- **Honour the token.** `Current.Cancellation` is cancelled when the stop budget ends — and when the job is
  [stopped](#stop-a-job-give-it-a-limit-read-a-lane), by hand or at a limit. Pass it to every awaited call, and stop
  cleanly when it fires.
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

### A job its host died with on its last attempt

The claim counts an attempt before the handler runs, so a job that takes its host down every time still reaches
`MaxAttempts` instead of looping for ever. But the host that died on the **last** attempt wrote nothing, and nobody
claims the job again — before, it read as running for ever: in the bell, on the dashboard, and to a `Jobs.Run`
awaiting it.

Now every processor sweeps, at most once a minute, for exactly those rows — claimed, out of lease, no attempt left,
never ended — and makes each the dead letter it is: its `Error` says the host stopped while it ran, its claim is let
go, the bell says *Failed*, an awaiting `Jobs.Run` throws `JobFailedException`, and the dashboard's Retry runs it
again. The processor logs `Job {Id} ({Type}) is a dead letter: …` and counts it in `rask.jobs.deadlettered`.

- **Nothing else is swept.** A job with an attempt left is simply claimed again when its lease runs out, and a job
  whose lease is renewed is running, however long.
- **Two hosts sweeping at once** end each row once: the sweep marks the rows with its own token before it reads them.
- Plain conditional writes on the existing columns — the same on SQLite, SQL Server and PostgreSQL, and no migration.

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
