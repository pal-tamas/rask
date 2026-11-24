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
so old rows open, and are put right as they are next edited.
