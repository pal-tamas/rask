# Rask.Example.Shop — every One Person Framework battery, working together

This is the app [`docs/one-person-framework.md`](../../docs/one-person-framework.md) describes: UI, data,
auth, background work, email, cache, durable events, backups and push — one C# codebase, one SQLite file,
one server.

It is **the CLI's output, not a hand-written lookalike.** Every file here was produced by running:

```bash
rask new Rask.Example.Shop --all-batteries --auth --docker
rask generate feature Product Name:string Price:decimal InStock:bool --context AppDbContext --soft-delete --concurrency
rask generate feature Order Customer:string Total:decimal --context AppDbContext --outbox
rask generate job PurgeStaleCarts --feature Orders
rask generate email OrderConfirmation --feature Orders
rask generate cache PopularProducts --feature Products
```

That matters: if the CLI ever stops producing something that works, this sample stops building, and
`tests/Rask.Example.Shop.Tests` re-runs the generators to check the committed files still match what the
CLI writes today.

## What was written by hand

Four things, each of them what the tutorial tells a reader to type:

| File | Why it isn't generated |
|------|------------------------|
| `Features/Shared/DbInitializer.cs` | A real app runs `rask db add Init` / `rask db update`. This one uses `EnsureCreated` so it can be cloned, run, and E2E-tested with no migration step. |
| `Features/Orders/OrderCreatedHandler.cs` | The generator scaffolds a handler that logs. This is the body — queue the confirmation email, schedule the follow-up job. |
| `Features/Orders/OrderConfirmation.cs` | The generator scaffolds an empty email body; this is the actual content. |
| `Features/Ops/OpsPage.cs` | A dashboard over every pillar's table, so the batteries are visible rather than merely wired. |

Plus one line in `Program.cs` calling `DbInitializer`, and `SnapshotOnStartup = true` so the Ops page has a
backup to show without waiting six hours.

## The chain worth watching

Create an order at `/orders/new` and watch `/ops`:

1. **Rask.Data** raises `OrderCreated` on the aggregate, and **Rask.Outbox**'s interceptor writes it to the
   outbox table *in the same transaction* as the order. Either both committed or neither did.
2. The outbox processor relays it to `OrderCreatedHandler` through **Rask.Cqrs**.
3. That handler queues the confirmation through **Rask.Mail** (an `.eml` lands in `mail-pickup/`, because no
   SMTP is configured) and schedules a delayed **Rask.Jobs** job.
4. `/ops` counts all of it, polling the one SQLite file that holds every pillar's state.

The outbox/jobs split is the point of step 3: the confirmation is *derived from* the transaction, so it goes
through the outbox; the cleanup is *scheduled*, so it goes to jobs.

## Running it

```bash
dotnet run --project samples/Rask.Example.Shop
```

Then sign in at `/login` (`alice` / `password`), and visit `/products`, `/orders` and `/ops`.

Configuration (all optional — the defaults keep it self-contained):

| Key | Default | What it does |
|-----|---------|--------------|
| `ConnectionStrings:App` | `Data Source=app.db` | Where the database lives. |
| `Mail:PickupDirectory` | `mail-pickup` | Where sent mail is written as `.eml`, since no SMTP is set. |
| `Sqlite:SnapshotDirectory` | `snapshots` | Where scheduled backups go. |
| `WebPush:PublicKey` / `:PrivateKey` | unset | Web Push stays off until a VAPID pair is configured. |
| `Litestream:ReplicaUrl` | unset | Continuous off-box backup stays off until set. |

## Two traps this sample is wired to avoid

- **The outbox needs the in-process publisher off.** `AddRaskData(o => o.DispatchDomainEventsInProcess = false)`
  is not a preference. With it on, `DomainEventInterceptor` drains and clears every entity's events before
  `OutboxInterceptor` can copy them — the outbox table stays empty, delivery quietly stops being durable, and
  nothing fails, because the handlers still run.
- **The pillars' tables must exist before the app starts.** Their processors are hosted services, and a
  faulted `BackgroundService` stops the host by default. Starting against a database missing one of those
  tables doesn't produce a friendly error; the app exits.

## What it doesn't show

Litestream replication and real Web Push delivery both need outside resources (object storage, a browser
push service), so they're wired and configuration-gated rather than demonstrated. The
[`Rask.SQLite.Litestream`](../../docs/sqlite.md) and [`Rask.WebPush`](../../docs/webpush.md) docs cover both.
