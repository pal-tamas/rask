# Writing many rows — bulk insert, batch updates and data migrations

> **The rest of Rask.Data:** [domain modelling](domain.md) · [storage — queries, conventions and the database](data.md).

Every change a person makes goes through an aggregate's own method ([operations](domain.md#operations-what-the-aggregate-does)),
so its rules hold. This page is for the other kind of write: a load of thousands of rows a seed or an import makes, and
a statement the database runs over every matching row at once. Each says what it skips, so it is a choice you make
knowing it.

## Writes over many rows

Five writes on every aggregate type, for when one row at a time is the wrong shape:

```csharp
await Pairing.DeleteWhere(p => p.Pump == pumpId);                                        // one DELETE
await Period.UpdateWhere(p => p.Id == id, s => s.SetProperty(p => p.Closed, true));      // one UPDATE
await Fueling.UpdateEach(f => codes.Contains(f.DriverChip), f => f.Match(plan));         // each loaded, changed, saved
var kept = await Fueling.Upsert(f => f.Pump == r.Pump && f.Number == r.Number,
    () => Taken(r), f => f.AsThePumpHoldsIt(r.Moment));                                  // the one, or a new one
var loaded = await Transaction.Import(lines, line => Loaded(file, line), o =>
{
    o.OnInvalid = ImportFailure.Collect;                                                 // set aside, not stopped
    o.Upsert(t => t.SourceId, (t, line) => t.Follow(line));                              // met again: changed
});
```

**A set write — `UpdateWhere`, `DeleteWhere` — is one statement**, so no row is loaded and the aggregate's methods
and `Validate()` do not run: it is for what no rule is about — a flag, a stamp, an infrastructure row (a login
session, a pairing a pump re-sends). What a save keeps it still keeps:

- the tenant and soft-delete filters (a `SoftDelete` aggregate's `DeleteWhere` stamps `DeletedAt` instead);
- `UpdatedAt` stamped and `Version` bumped by `UpdateWhere`, so a form read before it is refused as stale;
- the declared locks ([`LockedBy`](domain.md#a-row-closed-by-its-parent-lockedby)): a row whose parent is locked is refused — the old parent
  before the statement, the one `UpdateWhere` moved it to after it, still inside the transaction it rolls back —
  every parent the rows hold read under an update lock first (`UPDLOCK, HOLDLOCK` / `FOR UPDATE`), so a close cannot
  slip in between the check and the statement; an `UpdateWhere` that sets only the lock's `Except(…)` columns passes,
  as a save of them alone does;
- the unique and still-referenced messages, under the field a form shows;
- the data-changed notices a live page repaints on, after the commit;
- the [one-aggregate rule](domain.md#one-aggregate-per-transaction-manyaggregates), counted as one write of the type.

What it cannot keep it refuses: a `ValidateDelete()` asks each row in C#, as does a lock declared on a child the
delete would cascade to, so `DeleteWhere` on such an aggregate throws and names `X.Delete(id)`, which asks them.
Both run in a `Db.Transaction` of their own, or join the one in progress, and hand back how many rows they wrote.

**`UpdateEach` goes through the aggregate**: the keys the predicate matches are read first, then each aggregate is
loaded whole, changed by its own method and saved — its rules, events and notices as for one — `batchSize` (500) to a
transaction, each batch saved in one go. A batch says why it writes many aggregates (each is valid on its own), so
`OneAggregatePerTransaction` lets it through. A refused batch rolls back alone and the refusal is thrown; the batches
before it stay committed, so the change should be one that can run twice — which an event handler's is anyway.

**`Upsert` is the static, one-root "make it or change it"**: the one aggregate the predicate matches is changed
through its method, or the factory makes a new one, in one transaction; `Upserted<T>` says which. A predicate that
matches two is refused. Back the predicate with a unique index — two upserts at once would otherwise both make one;
with it, the second is refused under its field.

**`Import` is the large load through the factory** ([`BulkInsert`](#bulk-insert) is the one past it): each row is made
by the aggregate's factory and saved through its rules — `Validate()`, the unique checks, `LockedBy`, events —
`BatchSize` (500) to a transaction, each batch from an empty change tracker, so memory stays flat. A row refused by
its factory or its rules stops the import by default (the batches before stay committed); `ImportFailure.Collect`
sets it aside in `result.Failures` — its `Index` among the rows, the row, the `RaskValidationException` — and saves
the rest: the refused batch is run again a row at a time, each in a transaction of its own, which holds on every
database alike (a unique violation ends PostgreSQL's transaction). `o.Upsert(key, update)` matches each row to the
stored aggregate with the same natural key, and to an earlier row of the same import, and changes that one;
`result.Inserted` and `result.Updated` count them. A factory may be called again for a retried batch: it builds, it
does not write. `Collect` commits a batch at a time, so inside a `Db.Transaction` it is refused; `Stop` there makes
the import part of that transaction.

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

## A running number

`b.Numbered(…)` — beside [`b.Computed(…)`](data.md#computed-columns-defaults-and-filtered-indexes-in-c), which it pairs with.

A number every row has among its group — the fuelings of a pistol, counted by moment — kept by Rask in every save,
with no SQL in the app:

```csharp
static void Configure(EntityTypeBuilder<Fueling> b)
{
    b.Computed(f => f.CountedAt, f => f.ModifiedPistolId ?? f.PistolId);
    b.Numbered(f => f.Sequence, within: f => f.CountedAt, by: f => new { f.Moment, f.Number, f.Id },
            skip: f => f.MergedParentId != null)
        .Same(f => f.ParentId)                                                      // a split part has its whole one's number
        .SkipWhen<Distribution, string>((f, shares) => shares.Any(s => s.FromFuelingId == f.Id), s => s.FromFuelingId);
}
```

- **What it keeps.** Each row of a group is numbered 1, 2, 3 … in the order `by` says, the first member first; a row
  `skip` leaves out has none (null), and the rows after it close up. A row whose group is null has none.
- **When.** A save that adds, deletes or moves a row — changes its group, a member of `by`, `skip` or `Same` — numbers
  each group it touched anew from the earliest place it touched, counting on from the last number before it, and
  writes only the numbers that changed — before the save's transaction commits, so a rollback takes them back too.
  The rows the context holds are told their new numbers and stay unchanged.
- **`Same(f => f.ParentId)`**: a row that names another takes that row's number instead of a place of its own, and
  follows it when it moves.
- **`SkipWhen<TOther, TRow>(when, row)`**: another table's rows leave a row out — a fueling distributed from is not
  counted. A save that adds, changes or deletes a `TOther` row numbers anew the group of the row `row` names.
- **One numbering of a group at a time, on every database.** SQL Server takes `sp_getapplock` owned by the save's
  transaction and PostgreSQL `pg_advisory_xact_lock`, per group, in a sorted order, before the rows go out; SQLite's
  one writer already keeps saves apart. Saves racing into one group leave it numbered without a gap or a twin.
- **A computed group or order.** A `within` or `by` member that is a `b.Computed(…)` column — `CountedAt` above — is
  read as what it is computed from: an added row is numbered in the group its values put it in, and a change to a
  member it is computed from (`ModifiedPistolId`) moves the row, numbering anew both the group it leaves and the one it
  joins. A value C# cannot compute from the row (a division by zero) is read back from the database once the row is
  written, still before the commit.
- `by` is a member, or several as an anonymous type (`new { f.Moment, f.Id }`): plain values, or one-value value objects compared by their value. The
  number is a whole number — nullable when a row can have none — or a one-value value object over one. The entity's
  key is one column. A raw `ExecuteUpdate` passes no save, so it numbers nothing.

## Batch update and delete

For work the database can do on its own — a retention sweep, a flag every matching row gets — the aggregate's own
[set writes](#writes-over-many-rows) are one statement over every matching row, with nothing loaded and nothing
tracked, so a million rows cost one round trip rather than a million objects:

```csharp
await Product.UpdateWhere(p => p.Discontinued, s => s
    .SetProperty(p => p.Active, false)
    .SetProperty(p => p.Price, p => p.Price * 0.9m));        // the arithmetic happens in SQL

await Draft.DeleteWhere(d => d.SavedAt < cutoff);            // a SoftDelete aggregate's rows are stamped instead
```

Unlike EF Core's `ExecuteUpdateAsync` and `ExecuteDeleteAsync` underneath, they keep the conventions: `UpdatedAt` is
stamped and `Version` bumped, a `SoftDelete` aggregate's delete stamps `DeletedAt` rather than removing the row, the
tenant and soft-delete filters apply, the declared locks and the unique and referenced-row messages are asked, and the
pages showing the rows are told. A change a rule decides is never one of these — it goes through the aggregate's
operation, row by row, with [`UpdateEach`](#writes-over-many-rows).

A raw `db.Set<T>().Where(…).ExecuteUpdateAsync(…)` skips all of that — no stamp, no version bump, no events, no audit,
and a `DELETE` where a soft delete was meant — so on an aggregate the build points at it
([RASK187](diagnostics.md#rask187)) and its quick-fix rewrites it into `X.UpdateWhere(…)` / `X.DeleteWhere(…)`. It stays
for a table that is no aggregate: a migration step's, or one an [adopted schema](data.md#mapping-tables-that-already-exist)
keeps beside the model.

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
            await Tank.UpdateEach(_ => true, async t => t.Draw(await Pistol.Where(p => p.TankId == t.Id).Select(p => p.Id))));
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

