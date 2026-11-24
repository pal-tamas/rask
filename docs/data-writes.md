# Writing many rows — bulk insert, batch updates and data migrations

> **The rest of Rask.Data:** [domain modelling](domain.md) · [storage — queries, conventions and the database](data.md).

Every change a person makes goes through an aggregate's own method ([operations](domain.md#operations-what-the-aggregate-does)),
so its rules hold. This page is for the other kind of write: a load of thousands of rows a seed or an import makes, and
a statement the database runs over every matching row at once. Each says what it skips, so it is a choice you make
knowing it.

## Bulk insert

EF Core answers the bulk *update* and *delete* shapes with `ExecuteUpdate`/`ExecuteDelete`, but [its own
plan](https://learn.microsoft.com/ef/core/what-is-new/ef-core-7.0/plan) puts bulk **inserts** out of scope —
so seeding, importing and migrating data is left to every application to hand-roll. `BulkInsert` is that
code, written once:

```csharp
await db.BulkInsert(products);                          // on the context
await db.Products.BulkInsert(products);                 // or the set
await db.BulkInsert(products, o => o.BatchSize = 10_000);
```

It runs **through the context**, so nothing above stops being true: `CreatedAt`/`UpdatedAt` are stamped and
each entity's domain events are published, exactly as for an ordinary save. What changes is the shape of the
work — the rows are added and saved in batches (5,000 by default), change detection is off for the duration,
and the change tracker is **cleared between batches**.

That last part is the point. The naive `AddRange` + one `SaveChanges` keeps every entity tracked until the
end, so a large load's memory grows with the row count and each save re-walks what the previous ones already
wrote. Over 100,000 rows on SQLite:

| approach | time | allocated |
|---|---:|---:|
| `SaveChanges` per row | 5.48 s | 2,472 MB |
| `AddRange` + one `SaveChanges` | 1.22 s | 1,307 MB |
| `BulkInsert` | 976 ms | 1,105 MB |
| `BulkInsert`, `SkipChangeTracking` | **406 ms** | **141 MB** |

The last row is [the fast path](#the-fast-path) below; the rest is what batching alone buys.

### Where the transaction sits

Each batch commits on its own by default. That is deliberate: SQLite has exactly one write lock, so wrapping
a long import in a single transaction makes every other writer in the application wait for the whole thing,
and the WAL has to hold every uncommitted page until the end. Committing per batch hands the lock back
between batches. The cost is that a failure part-way leaves the batches that already committed — for a seed
or an import, usually the retryable outcome you want.

When the load really must be all-or-nothing, ask for it:

```csharp
await db.BulkInsert(products, o => o.SingleTransaction = true);
```

**Domain events wait for the commit** — in that mode and inside any transaction around the load (yours, or a
`Db.Transaction`): every batch's events are published once the whole load commits, and none when it rolls
back. A rollback gives each entity its events back (and clears a key the database generated for a row that is
gone), so a retrying strategy that runs the load again inserts and announces each row once. Durable handlers'
[outbox](outbox.md) rows are written in the same transaction as the batches.

### The fast path

Most of what is left after the batching is the change tracker itself — materialising an entry per row,
walking them on save, then throwing them away. `SkipChangeTracking` writes the rows straight to the provider
instead:

```csharp
await db.BulkInsert(products, o => o.SkipChangeTracking = true);
```

The shape it writes in depends on what a statement costs. **On SQLite** it is one prepared `INSERT` whose
parameters are rebound per row: a local file has no round trip, and a statement packed with many rows is quadratic
to bind there. **On PostgreSQL, SQL Server and MySQL** every statement is a round trip, so it packs up to 1,000 rows
into each `INSERT … VALUES (…), (…)` — fewer on SQL Server, whose request carries at most 2,100 parameters. Against
PostgreSQL 17 with 1 ms of added latency, 10,000 rows took 136 ms and allocated 11.6 MB that way, against 225 ms
and 99 MB through the change tracker and 20.1 s one row at a time (`PostgresBulkInsertBenchmarks`, run with
`scripts/run-bulk-insert-benchmarks-local.sh`). Another provider gets the per-row shape, which is correct
everywhere and slow wherever there is a network.

It is opt-in because of what it skips: **no `ISaveChangesInterceptor` runs** — not Rask.Data's, and not any
you registered. The writer stamps `CreatedAt`/`UpdatedAt` itself (from the same `TimeProvider` the auditing
interceptor uses, so a frozen test clock agrees across both paths), but nothing stands in for the rest.
Entities carrying domain events are **rejected** rather than inserted with their events undelivered, and an
outbox never sees the load.

Anything the writer cannot map faithfully throws and names the reason rather than writing wrong rows: a
store-assigned integer key (its value only exists after the insert), a store-computed column, a shadow
property, a navigation (nothing walks the graph here, so related rows would vanish), or an inheritance
hierarchy. A client-assigned key — the `Entity<Guid>` shape Rask entities use, where the factory sets `Id` —
is fine, and left unset it is reported rather than written as `Guid.Empty`.

Two more rules follow from how it works:

- **The context must have no pending changes.** The tracker is cleared as the load runs, so unsaved work
  would be discarded rather than swept into the first batch. It throws rather than lose it — save first.
- **An ambient transaction still owns the commit.** Called inside your own `BeginTransaction`, the load joins
  it and commits nothing itself, so it composes with surrounding work.

Under a retrying execution strategy (`Rask:Sqlite:Retry:Enabled`, or `UseRaskSqlite(sp, o => o.Retry.Enabled = true)`), a `SingleTransaction`
load is one retryable unit and a lazy sequence is buffered so the retry can re-enumerate it; the default
per-batch mode lets EF retry each batch on its own, which is both cheaper and free of replay.

## Batch update and delete

For work the database can do on its own — a retention sweep, a column every row gets the same value in — EF Core's
`ExecuteUpdateAsync` and `ExecuteDeleteAsync` are one statement over every matching row: nothing is loaded and
nothing is tracked, so a million rows cost one round trip rather than a million objects. A change a rule decides is
never one of these — it goes through the aggregate's operation, row by row ([data a migration
moves](#data-a-migration-moves)). They run on a context, in a job or a migration step that takes one:

```csharp
await using var db = await contexts.CreateDbContextAsync(ct);

await db.Set<Product>().Where(p => p.Discontinued)
    .ExecuteUpdateAsync(s => s
        .SetProperty(p => p.Active, false)
        .SetProperty(p => p.Price, p => p.Price * 0.9m), ct);   // the arithmetic happens in SQL

await db.Set<Order>().Where(o => o.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
```

**They bypass the interceptors**, and what they skip is the conventions this package otherwise maintains: no
`UpdatedAt` stamp, no `Version` bump (on a table that has them), and no domain events — nothing was loaded to raise any. Set what you
need explicitly:

```csharp
await db.Set<Product>().Where(p => p.Discontinued)
    .ExecuteUpdateAsync(s => s
        .SetProperty(p => p.Active, false)
        .SetProperty(p => p.UpdatedAt, DateTime.UtcNow)
        .SetProperty(p => p.Version, p => p.Version + 1), ct);
```

**A batch soft delete is an update, not `ExecuteDeleteAsync`.** `db.Remove(product)` on an aggregate
that names `SoftDelete` stamps `DeletedAt`, but `ExecuteDeleteAsync` is a `DELETE` the interceptors never see — the rows are gone, not
hidden. Stamp them instead:

```csharp
await db.Set<Product>().Where(p => p.Discontinued)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.DeletedAt, DateTime.UtcNow), ct);
```

The rule of thumb: reach for these when the work is a statement the database can do on its own and no rule of the
domain is about it; everything else is the aggregate's operation.

## Data a migration moves

A schema change is a migration — `rask db add`, applied as the app starts
([migrations apply themselves on start](data.md#migrations-apply-themselves-on-start)). The rows already in the table
are moved one of two ways, by what the change means:

- **A rule decides the new value** — a status a period's state implies, a value object tightened so an old value no
  longer passes. Run the aggregate's own operation over each row, from a job, so every row is changed by the rule that
  will change it from now on, its version checked and its events raised:

  ```csharp
  public sealed record ReopenPeriods : IJob;

  public sealed class ReopenPeriodsHandler : IJobHandler<ReopenPeriods>
  {
      public async Task Handle(ReopenPeriods job)
      {
          foreach (var id in await Period.Where(p => p.ClosedAt == null).Select(p => p.Id))
          {
              await Period.Reopen(id);   // the operation's own entry: one aggregate, one transaction
          }
      }
  }
  ```

  One aggregate per transaction keeps a long move from holding a lock over the whole table, and a job that stops
  half-way runs again over the rows still to do. A stored row an operation would refuse is told so by name, rather
  than rewritten past its rule.
- **Nothing decides it** — a new column every old row gets the same value in, a column renamed. That is the
  migration's own: a default on the added column, which fills the rows already there, or a
  [batch update](#batch-update-and-delete) the job runs once.

A tightened rule does not make a stored row unreadable: a value object is loaded without being validated, and a save
asks a property's rules only once that property is changed ([what every save asks](domain.md#what-every-save-asks)),
so old rows open, and are put right as they are next edited. To find them before the rule goes in, [audit the stored
data](#auditing-stored-data-with-rask-audit).

### Moving data in C# with `b.Data`

A schema change that needs its data moved — a link table filled from the column it replaces, a value split in two —
moves it through the app's own aggregates, not in SQL. A migration's `Up` adds a **data step**:

```csharp
public partial class TankPistols : Migration
{
    protected override void Up(MigrationBuilder b)
    {
        b.CreateTable(/* …what `rask db add TankPistols` wrote… */);
        b.Data(async () =>
            await Tank.UpdateEach(_ => true, async t => t.Draw(await Pistol.Where(p => p.TankId == t.Id).Ids())));
    }
}
```

- **It runs after the whole schema is migrated**, with the model the app has now — at the app's start (when
  [migrations apply themselves](data.md#migrations-apply-themselves-on-start)), from `dotnet App.dll rask db migrate`, and
  after `rask db update` (which runs `dotnet ef database update`, then the app's `rask db migrate`). Every tenant's
  rows are in reach: the step runs inside `Tenant.Across()`. The rules, events and audit of every save it makes run as
  they do anywhere else.
- **It runs once.** Each migration whose steps have run is recorded in a `__RaskDataSteps` table beside EF Core's
  history, made by the first run that has a step to record — a database whose migrations declare none never gets it.
  A migration applied by plain `dotnet ef database update` (which runs no C#) has its steps run the next time the app
  migrates.
- **Write it so a second run is harmless.** The schema step and its data step are not one transaction: a step that
  fails stops the start with its error, the schema stays migrated, and the step runs again on the next start.
- **The model is today's.** A step names only what the model still has; the change that drops a column a step reads
  deletes the step from its migration (the migration itself stays, as history).
- In the SQL of `dotnet ef migrations script` a step is a comment — it is C#, and the app runs it.

### Auditing stored data with `rask audit`

A rule is asked of what a save writes, and of a stored row only the fields a save changes — so a rule tightened
since an old row was written says nothing about that row until someone edits the field. Before a new rule or a unique
index is turned on, ask the stored rows instead:

```bash
rask audit                          # in the project: runs `dotnet run -- rask audit`
rask audit --aggregate Driver       # one aggregate (repeat, or Driver,Vehicle)
rask audit --json > audit.json      # every id, for a script
rask audit --unique-candidates      # the rows each unique index would refuse, before it is migrated
dotnet App.dll rask audit           # on the server, from the published app
```

The app answers it itself, through its own model: the host is built and never started (no port, no worker), each
aggregate is loaded whole, a page at a time, from every tenant, and asked what a save **adding** it would ask — every
validation attribute of every row of it, the type's own, `IValidatableObject`, `Validate()`, awaited rules included,
each in the aggregate's own tenant — plus:

- **the rules of every value object it holds**, which a load never asks: a `DriverName` stored longer than its
  `MaxLength` allows now, and a value its rules accept but would keep differently — stored with the blanks `Trim`
  cuts off, or in the small letters `Upper` raises (*"Stored differently from how its rules keep it"*);
- **every id kept as text** (a key or a reference whose column is text, as a typed id over a `Guid` in an existing
  schema): read as stored, before any row is loaded, and turned into the id and back — *"Not an id its type can
  read"*, or *"Not written the way the app writes it"* for an upper-case or braced one.

```text
rask audit: 2 aggregate(s), 1530 row(s) read, 2 rule(s) broken.

Driver — 412 read:
  CardNumber: At most 16 characters. — 2
    3f2c…, 9a41…
  Chips[*].Period: The end cannot be before the start. — 1
    77ab…

Vehicle — 1118 read, all pass.
```

**Read only, and the ids alone.** The audit's own context refuses a save, its rules are asked as a dry run (a
`Db.Lock` one takes is not taken), and it prints each rule's words, the field, the count and the aggregates' ids —
never what a row holds, so the report can leave a machine whose data cannot. A child's rows are counted together
(`Chips[*]`). A rule that throws on an old row is reported against that row (*"A rule threw: …"*) and the audit goes
on. The exit code says the answer: `0` every row passes, `1` some do not, `2` the command line was wrong — so a
release step can stop before a migration whose rule the stored data would break.

**Before a unique index goes in — `--unique-candidates`.** Declare the index in the model
(`b.HasIndex(d => new { d.TenantId, d.CardNumber }).IsUnique()`, with `.Where(…)` for a filtered one), and before its
migration runs ask the stored rows instead of the rules:

```bash
rask audit --unique-candidates --aggregate Driver
```

```text
Driver — 412 read:
  TenantId, CardNumber: Would break the unique index on (TenantId, CardNumber): rows that hold the same values. — 1
    3f2c… = 9a41…
```

The audit reads the model, not the database, so an index that exists only in code is asked as if it were there; one the
database already holds answers clean. For every unique index of every row type of the aggregates (children included)
it reads each row's key and id in LINQ — every row, a deleted one and every tenant's included, as the index will count
them, narrowed by the index's `.Where(…)` — and reports each group of rows holding the same key by their ids alone
(`id = id`), never the values. A row with a NULL in its key collides with nothing, as the index's filter on SQL Server
and the NULL rules of PostgreSQL and SQLite have it. Texts that differ only in letter case are reported apart (*"under a
case-insensitive collation (SQL Server's default)"*): they collide there and nowhere else. An index whose filter is
hand-written SQL (`HasFilter`) cannot be asked in LINQ; the report names it under a note instead.

