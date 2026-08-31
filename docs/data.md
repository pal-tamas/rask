# Rask.Data — base entity + EF Core interceptors

> **In practice:** [Tutorial Ch 2](tutorial/02-first-feature.md) · recipe [add a feature to an existing database](recipes.md#add-a-feature-to-an-existing-database) · [cheat sheet](cheatsheet.md).

`Rask.Data` is a tiny, provider-agnostic data layer for **Entity Framework Core** applications. It gives
your entities a shared base with identity, audit stamps, and a domain-events buffer, and drives four
production concerns — auditing, soft delete, optimistic concurrency, and domain-event publication —
through EF Core interceptors and a one-line model convention, so no feature has to re-implement them.

It is the foundation the [tutorial](tutorial/02-first-feature.md) builds its slices on — its
`--soft-delete` / `--concurrency` / `--events` flags, packaged for direct use.

> Included in the [`Rask`](../README.md) package — nothing to install. It is **on**; an app that does without it says so:
>
> ```csharp
> app.Configure(c => c.Data.Off());
> ```

## The base entity

```csharp
public sealed class Product : Entity<Guid>, ISoftDeletable, IVersioned
{
    private Product() { } // EF materialization

    public string Name { get; private set; } = "";
    public DateTime? DeletedAt { get; private set; }  // ISoftDeletable
    public int Version { get; private set; }          // IVersioned

    public static Product Create(string name)
    {
        var product = new Product { Id = Guid.NewGuid(), Name = name };
        product.Raise(new ProductCreated(product.Id)); // a Rask.Cqrs INotification
        return product;
    }

    public void Rename(string name)
    {
        Name = name;
        Raise(new ProductRenamed(Id));
    }
}
```

`Entity<TId>` always carries `Id`, `CreatedAt`, `UpdatedAt`, and the domain-events collection
(`Raise` / `DomainEvents` / `ClearDomainEvents`). The two behaviors are **opt-in** — implement a marker
interface to turn one on:

| Interface | Adds | Effect |
|-----------|------|--------|
| `ISoftDeletable` | `DateTime? DeletedAt` | `Remove` becomes a `DeletedAt` stamp; a global query filter hides it. |
| `IVersioned` | `int Version` | Marked as the optimistic-concurrency token; bumped on every update. |

## Wiring

```csharp
builder.Services.AddRaskCqrs();   // domain-event dispatch
builder.Services.AddRaskData();   // the interceptors + a TimeProvider

builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseSqlite("Data Source=app.db")
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    modelBuilder.ApplyRaskConventions(); // query filters + concurrency tokens
}
```

## What the interceptors do

- **`AuditingInterceptor`** — stamps `CreatedAt`/`UpdatedAt` (UTC, from an injectable `TimeProvider`) and
  increments each `IVersioned.Version` on update, so the stored token changes (SQLite has no rowversion).
- **`SoftDeleteInterceptor`** — rewrites a `Deleted` `ISoftDeletable` to `Modified` + sets `DeletedAt`.
  Your handler just calls `db.Remove(entity)`; to restore, load with `IgnoreQueryFilters()` and clear
  `DeletedAt`.
- **`DomainEventInterceptor`** — after the change commits, publishes each entity's `DomainEvents`
  through `IDispatcher.PublishAsync` (in a fresh scope) and clears them. Any
  `INotificationHandler<T>` registered by `AddRaskCqrs()` reacts automatically.

  It **stands down on its own** when something else owns delivery — [`Rask.Outbox`](outbox.md) claims it by
  registering an `IDomainEventDeliveryOwner`. The handover is resolved when the container is built, not when
  either `Add` call runs, so `AddRaskData()` needs no argument and the two calls work in either order.
  That matters more than it looks: this interceptor *drains and clears* the events in `SavingChanges`, so
  running it alongside an outbox would empty each entity before `OutboxInterceptor` could copy it — the
  outbox table stays empty and delivery silently stops being durable, while every handler still runs and
  nothing reports an error. `RaskDataOptions.DispatchDomainEventsInProcess` (a `bool?`, default `null` =
  automatic) overrides the decision in both directions.

## Optimistic concurrency

`IVersioned` makes `Version` an EF Core concurrency token. When two edits race, the second `SaveChanges`
throws `DbUpdateConcurrencyException`. In a web form, round-trip the original `Version` (a hidden field)
and set it as the tracked original value before saving:

```csharp
var product = await db.Products.FirstAsync(x => x.Id == id);
db.Entry(product).Property(x => x.Version).OriginalValue = form.Version;
product.Rename(form.Name);
await db.SaveChangesAsync(); // throws if someone else changed it since `form.Version`
```

## Bulk insert

EF Core answers the bulk *update* and *delete* shapes with `ExecuteUpdate`/`ExecuteDelete`, but [its own
plan](https://learn.microsoft.com/ef/core/what-is-new/ef-core-7.0/plan) puts bulk **inserts** out of scope —
so seeding, importing and migrating data is left to every application to hand-roll. `BulkInsertAsync` is that
code, written once:

```csharp
await db.BulkInsertAsync(products);                          // on the context
await db.Products.BulkInsertAsync(products);                 // or the set
await db.BulkInsertAsync(products, o => o.BatchSize = 10_000);
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
| `BulkInsertAsync` | 976 ms | 1,105 MB |
| `BulkInsertAsync`, `SkipChangeTracking` | **406 ms** | **141 MB** |

The last row is [the fast path](#the-fast-path) below; the rest is what batching alone buys.

### Where the transaction sits

Each batch commits on its own by default. That is deliberate: SQLite has exactly one write lock, so wrapping
a long import in a single transaction makes every other writer in the application wait for the whole thing,
and the WAL has to hold every uncommitted page until the end. Committing per batch hands the lock back
between batches. The cost is that a failure part-way leaves the batches that already committed — for a seed
or an import, usually the retryable outcome you want.

When the load really must be all-or-nothing, ask for it:

```csharp
await db.BulkInsertAsync(products, o => o.SingleTransaction = true);
```

**Entities carrying domain events are rejected in that mode** — and inside an ambient transaction, for the
same reason. `DomainEventInterceptor` publishes in `SavedChanges`, which inside a transaction runs *before*
the commit, so a load that failed later would already have announced rows that no longer exist. Clear the
events, drop the transaction, or use [`Rask.Outbox`](outbox.md): its messages are written in the same
transaction and drained after it commits, which is exactly the durable-delivery shape this needs.

### The fast path

Most of what is left after the batching is the change tracker itself — materialising an entry per row,
walking them on save, then throwing them away. `SkipChangeTracking` writes the rows straight to the provider
instead, with one prepared `INSERT` whose parameters are rebound per row:

```csharp
await db.BulkInsertAsync(products, o => o.SkipChangeTracking = true);
```

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

Under a retrying execution strategy (`UseRaskSqlite(..., o => o.Retry.Enabled = true)`), a `SingleTransaction`
load is one retryable unit and a lazy sequence is buffered so the retry can re-enumerate it; the default
per-batch mode lets EF retry each batch on its own, which is both cheaper and free of replay.

## Non-overlapping ranges

A booking, a lease, a price valid for a period — the rule is always the same: **two rows may not cover the
same point in time (or in a number line)**. PostgreSQL spells this as an exclusion constraint. SQLite has
nothing, and a `UNIQUE` index does not help — it only stops *identical* rows, so `100–200` and `150–250`
both sail through.

Declare it on the model and the migration carries the enforcement:

```csharp
modelBuilder.Entity<Booking>()
    .HasNonOverlappingRange(x => x.StartsAt, x => x.EndsAt, partitionBy: x => x.RoomId);
```

That is the whole API. `dotnet ef migrations add` then emits an index and a `BEFORE INSERT` / `BEFORE
UPDATE` trigger pair that `RAISE(ABORT)`s on a conflict, and a violating `SaveChanges` throws
`RangeOverlapException` naming the table — catch it and tell the user the slot
is taken.

```csharp
try
{
    await context.SaveChangesAsync();
}
catch (RangeOverlapException)
{
    return Results.Conflict("That slot is already booked.");
}
```

**Ranges are half-open — `[lo, hi)`.** This is the part to get right: it is what makes `100–200` and
`200–300` neighbours rather than a conflict. Store bounds in a type the database orders correctly — a
number, a date, or a `yyyy-MM-dd` string — never a localized date string. Pair the rule with a check
constraint keeping `lo < hi`; it assumes well-formed ranges and says nothing about inverted ones.

| Option | Effect |
| --- | --- |
| `partitionBy` | Scopes the rule: `x => x.RoomId`, or `x => new { x.Sku, x.Region }`. Omit for table-wide. |
| `ignoreSoftDeleted` | Lets a soft-deleted row free its slot. Defaults to on for `ISoftDeletable` entities, ignored otherwise. |

Three things worth knowing:

- **Enforcement is in the database, not the `DbContext`.** Raw SQL, a second process and a background job
  are all bound by it. That is the point — an application-level check is bypassable, and a check-then-insert
  in your own code has a race between the check and the insert.
- **It arrives via migrations.** An existing table gains the rule from the next migration; a database
  created with `EnsureCreated` does not get it at all.
- **It survives table rebuilds.** SQLite cannot `ALTER` most things in place, so EF rebuilds the table and
  drops the original — taking its triggers with it. Rask re-emits them at the end of every migration that
  touches the table, so the constraint cannot silently disappear.

Requires `UseRaskSqlite(...)`, which registers the generator and the exception translation. Both are inert
until an entity declares a rule, and the rule composes with
[`o => o.StrictTables = true`](sqlite.md#strict-tables--making-the-store-enforce-your-types) — a table can be both
`STRICT` and range-constrained. See [Rask.SQLite](sqlite.md).

## Notes

- **Server-side.** These interceptors run against a real EF Core provider (SQLite by default in Rask);
  they are not used on the WASM client.
- **Trim/AOT-safe.** The model convention runs at startup (not the hot path) and uses no runtime handler
  reflection; domain-event dispatch goes through `Rask.Cqrs`' source-generated registry.
- **Durable delivery.** For at-least-once, crash-safe events, pair the entity with
  [`Rask.Outbox`](outbox.md), which persists events in the same transaction and drains them from a
  background worker (and disables the in-process dispatcher to avoid double delivery).
