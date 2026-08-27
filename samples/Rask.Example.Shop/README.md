# Rask.Example.Shop — every One Person Framework battery, working together

This is the app [`docs/one-person-framework.md`](../../docs/one-person-framework.md) describes: UI, data,
auth, background work, email, cache, durable events, backups and push — one C# codebase, one SQLite file,
one server.

The project itself came from the CLI:

```bash
rask new Rask.Example.Shop --auth --bootstrap
```

Everything inside it — the `Product` and `Order` slices, the job, the email, the cache and the ops page —
is ordinary C# written the way the [tutorial](../../docs/tutorial/00-overview.md) teaches it. This app is
the tutorial's finished state, so it is the place to look when a snippet there needs its surroundings.

`tests/Rask.Cli.Tests/ShopProvenanceTests` still checks the **scaffolded** files against what `rask new`
writes today, so the parts the CLI does own can't drift.

## Worth reading first

| File | What it shows |
|------|---------------|
| `Features/Products/` | a complete CRUD slice — entity, request, EF configuration, CQRS handlers, pages ([ch. 2](../../docs/tutorial/02-first-feature.md)) |
| `Features/Orders/OrderCreatedHandler.cs` | reacting to a domain event: queue the confirmation email, schedule the follow-up job ([ch. 7](../../docs/tutorial/07-outbox-events.md)) |
| `Features/Orders/OrderConfirmation.cs` | an email body that is just a Rask component ([ch. 5](../../docs/tutorial/05-email.md)) |
| `Features/Ops/OpsPage.cs` | a dashboard over every pillar's table, so the batteries are visible rather than merely wired |
| `Features/Shared/DbInitializer.cs` | `EnsureCreated` instead of migrations, so the sample can be cloned and run with no `rask db` step |

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
