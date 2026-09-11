# Rask.Data — models, queries, and the database you don't write

> **In practice:** [Tutorial Ch 2](tutorial/02-first-feature.md) · recipe [add a feature to an existing database](recipes.md#add-a-feature-to-an-existing-database) · [cheat sheet](cheatsheet.md).

`Rask.Data` is a layer over **Entity Framework Core** with one goal: **you declare models, and that is
all**. No `DbContext` class, no `DbSet` property, no `IEntityTypeConfiguration`, no registration — and
no `IDbContextFactory` injected into every page that reads a row.

Underneath it is ordinary EF Core, and nothing is hidden from you: `Db.Current` is the real
`DbContext`, and an app that outgrows this writes its own context and Rask steps aside
([below](#using-ef-core-the-usual-way)).

> Included in the [`Rask`](../README.md) package — nothing to install. It is **on**; an app that does without it says so:
>
> ```csharp
> app.Configure(c => c.Data.Off());
> ```

## The whole of it

```csharp
public sealed class Product : Model<Guid>
{
    private Product() { }                                 // EF materialization

    public string Name  { get; private set; } = "";
    public decimal Price { get; private set; }
    public bool Active  { get; private set; }

    public static Product Create(string name, decimal price) =>
        new() { Id = Guid.CreateVersion7(), Name = name, Price = price, Active = true };

    public void Reprice(decimal price) => Price = price;
}
```

That compiles into a mapped table. A source generator finds every `Model` at build time and hands the
model to `RaskAppDbContext`; the host points the ambient database at it. There is nothing else to write
and nothing to register.

**Generated, never reflected.** No assembly is scanned and no method is found by name, so a trimmed
publish cannot quietly drop an entity and leave you a missing table with a green build.

`Model<TId>` carries `Id` and a domain-events buffer (`Raise` / `DomainEvents` / `ClearDomainEvents`).
**Everything else is opt-in, and opting in does not put anything on your class** — the marker alone is
enough, and the column is added as an EF *shadow property*:

| Interface | Column | Effect |
|-----------|--------|--------|
| `ITimestamped` | `CreatedAt`, `UpdatedAt` | Stamped on insert and on every update. |
| `ISoftDeletable` | `DeletedAt` | `Remove` becomes a `DeletedAt` stamp; a global query filter hides it. |
| `IVersioned` | `Version` | The optimistic-concurrency token; bumped on every update. |

```csharp
public sealed class Product : Model<Guid>, ITimestamped, ISoftDeletable
{
    public string Name { get; private set; } = "";   // and that is the whole class
}
```

That model has `CreatedAt`, `UpdatedAt` and `DeletedAt` columns, is stamped on every write, and
disappears from queries when deleted — with no infrastructure in the domain type at all.

**Declaring a property is how you opt into *reading* one.** When a screen shows "added on", or a query
orders by it, write it out and it becomes an ordinary mapped property:

```csharp
public sealed class Product : Model<Guid>, ITimestamped
{
    public DateTime CreatedAt { get; private set; }   // now selectable, filterable, renderable
}
```

One or both, in any combination — declare only `CreatedAt` and `UpdatedAt` stays a shadow column. A
private setter is enough either way: the framework writes these through EF's change tracker, not through
the CLR setter. What is not declared is still reachable when something genuinely needs it, through
`EF.Property<DateTime>(product, "CreatedAt")`.

**`IVersioned` is the exception and must declare `public int Version { get; private set; }`.**
Optimistic concurrency exists to round-trip the token through an edit form, and a value the application
cannot read is one it cannot send back. A model that marks itself versioned without the property is
refused while the model is built, by name, rather than failing later as an update that matched no row.

## Reading: the model type is its own `DbSet`

Every `Model` gains `DbSet`'s surface as static members, so a query needs no context in scope:

```csharp
await Product.All.ToListAsync();
await Product.Where(p => p.Active).OrderBy(p => p.Name).ToListAsync();
await Product.Where(p => p.Price > 10).OrderByDescending(p => p.Price).Skip(20).Take(20).ToListAsync();
await Product.Include(p => p.Reviews).Where(p => p.Active).ToListAsync();
await Product.OrderBy(p => p.Name).Select(p => p.Name).ToListAsync();   // reads one column
await Product.All.IgnoreQueryFilters().ToListAsync();                   // soft-deleted rows too
await Product.FindAsync(id);
await Product.FirstOrDefaultAsync(p => p.Name == "Anvil");
await Product.CountAsync(p => p.Active);
await Product.AnyAsync();
await foreach (var p in Product.AsAsyncEnumerable()) { }
```

These are C# 14 static extension members over `Model`, which is why nothing has to be declared or
derived from a second base. A member declared on the model itself always wins, so your own `Create` or
`Find` is untouched.

Composing does **not** hold a context open. The terminal call opens one, runs, and disposes it before
it returns — so this is a complete statement anywhere, including a component's `OnMountAsync`:

```csharp
protected override async Task OnMountAsync() =>
    _products = await Product.Where(p => p.Active).OrderBy(p => p.Name).ToListAsync(CancellationToken);
```

For a shape this does not wrap — a group-by, a join, an aggregate — `QueryAsync` hands you the live
`IQueryable` inside a managed context:

```csharp
var byMonth = await Product.All.QueryAsync((q, ct) =>
    q.GroupBy(p => p.CreatedAt.Month)
     .Select(g => new { Month = g.Key, Total = g.Sum(p => p.Price) })
     .ToListAsync(ct));
```

### Queries are no-tracking by default

Most reads are rendered and never written back, and outside a unit of work the context is discarded as
the call returns — so tracking would cost a graph walk to buy nothing.

**The consequence to know:** mutating a row that came back untracked and then saving does *nothing*.
Write it back explicitly with `Product.Update(entity)`, which is the ordinary way round here, or ask
for tracking with `.AsTracking()` and mutate in place.

## Writing: a unit of work

`Add`, `Update` and `Remove` are EF Core's own verbs and mean exactly what they mean in EF — they tell
the change tracker what happened and write nothing until a save. So they need a unit of work, and say
so plainly when there is none rather than appearing to work:

```csharp
await using var uow = Db.Begin();

Product.Add(Product.Create("Anvil", 9.99m));
Product.Remove(discontinued);
Order.Update(order);

await uow.SaveChangesAsync();          // one transaction across all three
```

Disposing without saving writes nothing, so a unit of work that throws part-way leaves the database
untouched. The context is created lazily, so wrapping a method that turns out to touch no data costs
one `AsyncLocal` write.

**Nesting joins rather than nests.** `Db.Begin()` inside an open unit of work returns a handle onto the
same context, and disposing it neither saves nor disposes the outer one — so a helper can open a unit
of work unconditionally and still take part in its caller's transaction.

### A model can save itself

Behaviour on the model can finish the job, so the caller has nothing to remember:

```csharp
public async Task<bool> TryCancelAsync(DateTime when, CancellationToken ct = default)
{
    if (await Shipment.AnyAsync(s => s.OrderId == Id && s.Dispatched, ct))
        return false;

    Cancel(when);                  // the decision
    await this.SaveAsync(ct);      // now make it true
    return true;
}
```

Outside a unit of work `SaveAsync` *is* one — a context is opened, the row is written, the context is
disposed — so `await order.TryCancelAsync(now)` is a complete operation with no `Db.Begin()` around it.

**It inserts a model that has never been persisted and updates one that has**, and `DeleteAsync` is its
counterpart, so a single write of any kind is a one-liner:

```csharp
var order = Order.Place("A-1");
await order.SaveAsync();        // insert
order.Cancel(now);
await order.SaveAsync();        // update
await order.DeleteAsync();      // soft delete, through the interceptor
```

That leaves `Db.Begin()` for the one thing it is actually needed for: **putting several models in one
transaction**.

An `ITimestamped` model answers the insert-or-update question for free: the auditing interceptor stamps
`CreatedAt` on insert and nothing else ever writes it, so a default value means "never persisted" — no
extra `SELECT`, and no guessing from whether the key is set, which for a client-assigned `Guid` it
always is. A model **without** the stamps is asked instead: one `SELECT` by primary key, and only on a
detached save — a tracked model never reaches that path. A row whose `CreatedAt` was never populated (a
table predating these conventions) reads as new and fails loudly on the duplicate key rather than
writing the wrong thing; `Update` is the explicit way to save one of those.

`DeleteAsync` goes through the change tracker, so an `ISoftDeletable` is stamped rather than removed and
domain events are still announced — the difference from `ExecuteDeleteAsync`, which the interceptors
never see.

**Inside a unit of work it joins rather than commits.** The change is tracked and written when the
*caller's* unit of work commits, so wrapping two orders in one transaction still gives one transaction,
and abandoning it abandons both. A model's own method can never commit half of its caller's work:

```csharp
await using var uow = Db.Begin();

await first.TryCancelAsync(now);    // tracked, not written
await second.TryCancelAsync(now);   // tracked, not written

await uow.SaveChangesAsync();       // both, in one transaction
```

`this.` is required inside the model itself: `SaveAsync` is an extension member, so it needs a receiver.
It writes an existing row — a new model is inserted with `Add`, which says so at the call site rather
than leaving insert-or-update to be inferred from whether a key happens to be set.

### Batch update and delete

For work the database can do on its own, `ExecuteUpdateAsync` and `ExecuteDeleteAsync` are one
statement over every matching row — nothing is loaded and nothing is tracked, so a million rows cost
one round trip rather than a million objects:

```csharp
await Product.Where(p => p.Discontinued)
    .ExecuteUpdateAsync(s => s
        .SetProperty(p => p.Active, false)
        .SetProperty(p => p.Price, p => p.Price * 0.9m));   // the arithmetic happens in SQL

await Order.Where(o => o.CreatedAt < cutoff).ExecuteDeleteAsync();
```

**They bypass the interceptors**, exactly as EF Core's own do, and what they skip is the conventions
this package otherwise maintains: no `UpdatedAt` stamp, no `Version` bump, and no domain events —
nothing was loaded to raise any. Set what you need explicitly:

```csharp
await Product.Where(p => p.Discontinued)
    .ExecuteUpdateAsync(s => s
        .SetProperty(p => p.Active, false)
        .SetProperty(p => p.UpdatedAt, DateTime.UtcNow)
        .SetProperty(p => p.Version, p => p.Version + 1));
```

**A batch soft delete is an update, not `ExecuteDeleteAsync`.** `Remove` on an `ISoftDeletable` stamps
`DeletedAt`, but `ExecuteDeleteAsync` is a `DELETE` the interceptors never see — the rows are gone, not
hidden. Stamp them instead:

```csharp
await Product.Where(p => p.Discontinued)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.DeletedAt, DateTime.UtcNow));
```

The rule of thumb: reach for these when the work is a statement the database can do on its own, and
load-then-save when the conventions and the domain events are the point.

## Testing a model

Behaviour that only changes the model needs no database, no fixture and no mock — it is a plain object:

```csharp
[Fact]
public void A_shipped_order_refuses_to_be_cancelled()
{
    var order = Order.Place("A-2");
    order.Ship();

    Assert.Throws<InvalidOperationException>(() => order.Cancel(Now));
    Assert.Empty(order.DomainEvents);       // the model's own record of what happened
}
```

Behaviour that asks the database something gets a real one in a line, rather than a mocked `DbContext`:

```csharp
await using var database = await TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={path}"));

var order = Order.Place("B-2");
await SeedAsync(order, Shipment.For(order.Id, dispatched: true));

Assert.False(await order.TryCancelAsync(Now));
```

`TestDatabase.StartAsync` builds the generated model — so no fixture has to list entities — creates the
schema, wires the auditing and soft-delete interceptors so the conventions behave as they do in
production, and points the ambient `Db` at it. Disposing clears it, so one test cannot leak its database
into the next. It takes a `TimeProvider`, so audit stamps are assertable.

It is provider-agnostic on purpose: the options callback is yours, so `Rask.Data` gains no provider
dependency and a test runs against the database the app actually uses.

Domain-event *publication* is deliberately not wired — that needs a service provider to resolve handlers
through, which is more than a database fixture should invent. Assert on `DomainEvents` instead.

## Reaching the `DbContext`

`Db` is the ambient database, reachable from anywhere — including from inside a model:

```csharp
public sealed class Product : Model<Guid>
{
    public async Task<int> OpenOrdersAsync() =>
        await Db.Set<Order>().CountAsync(o => o.ProductId == Id && !o.Closed);
}
```

`Db.Current` is the raw `DbContext`; `Db.SaveChangesAsync()` commits the ambient unit of work;
`Db.HasCurrent` says whether one is open.

## Mapping rules the conventions don't cover

A length, an index, a relationship, a converter — put them in a **static `Configure`** on the model
itself. No attribute, no interface, no separate class:

```csharp
public sealed class Product : Model<Guid>
{
    public string Sku { get; private set; } = "";

    public static void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(p => p.Sku).IsUnique();
    }
}
```

It runs **last** — after Rask's conventions — so it can extend them or overrule them, replacing the
soft-delete query filter or dropping the concurrency token. It is optional; a model without one is
mapped by convention.

The method is matched by signature, so a near miss (an instance method, a private one, the wrong
builder type) is reported as [RASK072](diagnostics.md#rask072) rather than silently not called.

## Value objects

Mark a value object with `IValueObject` and it is mapped as an EF Core **complex type** — its
properties become columns on the owning row:

```csharp
public sealed record Money(decimal Amount, string Currency) : IValueObject;

public sealed class Order : Model<Guid>
{
    public Money Total { get; private set; } = new(0m, "EUR");   // Total_Amount, Total_Currency
}
```

**A complex type, not an owned entity**, and the distinction is the point. An owned entity is a row
with hidden identity: tracked separately, nullable in ways a value has no business being, and quietly
producing a join. A complex type is part of the row — which is what a value object *is*, so it is the
default here, and it is why `Money` can be shared by two models without either owning it.

Nesting works: a value object made of value objects is mapped all the way down. **One EF Core
constraint applies to the outer one** — a complex type is materialised through its constructor, and EF
cannot bind a nested complex type to a constructor parameter. So a value object that *contains another
value object* needs a parameterless constructor and settable properties:

```csharp
// holds only scalars — a positional record is fine
public sealed record Money(decimal Amount, string Currency) : IValueObject;

// contains a value object — needs a parameterless ctor
public sealed class Packaging : IValueObject
{
    public Money Cost { get; set; } = new(0m, "EUR");
    public string Material { get; set; } = "card";
}
```

A *collection* of value objects is not mapped automatically; configure it in the model's own
`Configure`.

## Strongly-typed ids

Use one as the model's key and it is converted to its underlying value automatically — nothing declares
it as an id:

```csharp
public readonly record struct ProductId(Guid Value);

public sealed class Product : Model<ProductId> { }
```

The converter is registered once for the type, in `ConfigureConventions`, so **every** property of that
type is converted — the key, a foreign key on another model, a nullable one — without any of them being
named. An id the generator cannot build a converter for is reported as
[RASK073](diagnostics.md#rask073) rather than left to fail at model build.

Ids that need no converter — `Guid`, `int`, `long`, `string` — are left alone.

## Using EF Core the usual way

None of the above is compulsory, and opting out is not deriving from `Model`. A class that does not
derive from it is an ordinary EF Core entity: write your own `DbContext`, your own
`IEntityTypeConfiguration`, your own `DbSet` properties, and use them exactly as you do today.

Registering an `IDbContextFactory<YourContext>` is the whole of opting out at the app level. Rask binds
the ambient database and every database-backed battery to the context you registered, and
`RaskAppDbContext` is never constructed. Call `modelBuilder.ApplyRaskConventions()` from its
`OnModelCreating` to keep the soft-delete filters and concurrency tokens, or
`ModelRegistry.Apply(modelBuilder)` to keep the generated model as well.

## Wiring, when Rask is not hosting

A Rask app needs none of this — the host does it. Anything else registers the context and points the
ambient database at it once, after the container is built:

```csharp
builder.Services.AddRaskCqrs();                  // domain-event dispatch
builder.Services.AddRaskData<AppDbContext>();    // interceptors + the ambient binding

builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseSqlite("Data Source=app.db")
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

var app = builder.Build();
Db.Configure(app.Services);
```

Derive that context from `RaskDbContext` rather than `DbContext`. That is what maps the classes you
declared — and it also brings `ConfigureConventions`, where the value converters for strongly-typed ids
are registered, which EF reads *before* it builds the model:

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);  // every Model<TId> you declared
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.ApplyRaskConventions(); // query filters + concurrency tokens — always last
    }
}
```

Over plain `DbContext` this compiles, boots and migrates with every model silently absent, so the first
`Product.Add(…)` throws saying the entity type was not found. If you cannot change the base type — it is
already someone else's — call `ModelRegistry.Apply(modelBuilder)` in place of `base.OnModelCreating`, and
`ModelRegistry.ApplyConventions(configurationBuilder)` from an overridden `ConfigureConventions`.

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

Under a retrying execution strategy (`Rask:Sqlite:Retry:Enabled`, or `UseRaskSqlite(sp, o => o.Retry.Enabled = true)`), a `SingleTransaction`
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

Requires `UseRaskSqlite(...)`, which registers the generator and the exception translation. On any other
provider — including a plain `UseSqlite` — the rule would be silently ignored, so `AddRaskData<TContext>`
**refuses to boot** instead, naming the entity and the call that enforces it. Both are inert
until an entity declares a rule, and the rule composes with
[`Rask:Sqlite:StrictTables`](sqlite.md#strict-tables--making-the-store-enforce-your-types) — a table can be both
`STRICT` and range-constrained. See [Rask.SQLite](sqlite.md).

## PostgreSQL

SQLite is the default and, for most single-developer products, the right answer for a long time. When one box
is no longer enough — a managed database, several app instances — `Rask.Postgres` is the provider package:

```csharp
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskPostgres(sp)
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

The connection string is `Rask:ConnectionStrings:App` — in production usually the whole string, password
included, as `Rask__ConnectionStrings__App` in the environment — and a missing one is an error naming that key.

`UseRaskPostgres` is a drop-in for `UseNpgsql` that gives every session production timeouts —
`StatementTimeout` (30s), `LockTimeout` (10s, and it must stay below the statement timeout so lock contention
is not reported as a slow query), `IdleInTransactionSessionTimeout` (1m) — and turns on Npgsql's
transient-failure retrying (`Retry`). Each is `Rask:Postgres` in `appsettings.json` (`"StatementTimeout":
"00:00:10"`, `"Retry": { "MaxCount": 3 }`); a callback — `UseRaskPostgres(sp, p => …)` — runs after the
section and wins. The timeouts travel as startup parameters in the connection string, so
they are the session's defaults: they survive the pool resetting a returned connection, add no round trip per
query, and reach code that opens the `DbConnection` itself. Behind PgBouncer in transaction mode, add `options`
to its `ignore_startup_parameters`, or set the timeouts to `TimeSpan.Zero` and configure them on the role.

Everything in this guide works unchanged: the interceptors, the ambient `Db`, bulk insert (which spells its
SQL through the provider), and the jobs, mail, outbox and cache batteries, whose leased claim is proven
against a real server. Three things change:

- **The bulk-insert fast path pays one round trip per row.** `SkipChangeTracking` rebinds one prepared
  single-row `INSERT` per row — the winning shape on a local file. Against a server each row is a network
  round trip, so its cost grows with the latency to the database: 10,000 rows took about a second against a
  PostgreSQL container on the same machine, and a remote server multiplies that by its round-trip time.
  Measure it against the batched default before choosing it for a remote database.
- **Retrying refuses a transaction you open yourself** outside the execution strategy. Wrap a hand-written
  `BeginTransaction` in `context.Database.CreateExecutionStrategy().ExecuteAsync(...)`, or set
  `Rask:Postgres:Retry:Enabled` to `false`.
- **The file-shaped batteries do not apply.** Litestream and snapshots replicate or copy a SQLite file, and
  there is no file — back up with your provider's snapshots or `pg_dump`.

## SQL Server

When SQL Server is already the house database, `Rask.SqlServer` is the provider package:

```csharp
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskSqlServer(sp)
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

The connection string is `Rask:ConnectionStrings:App` (`Rask__ConnectionStrings__App` in the environment), and a
missing one is an error naming that key.

`UseRaskSqlServer` is a drop-in for `UseSqlServer`. SQL Server has no server-side statement timeout, so the
ceiling on a runaway query is the client `CommandTimeout` (30s). On every connection EF opens it sends
`SET XACT_ABORT ON` — so a run-time error rolls the whole transaction back instead of leaving it open with its
locks — and `SET LOCK_TIMEOUT` (10s, below the command timeout, so lock contention is not reported as a slow
query). Those go as one batch per open: SQL Server takes no session settings in the connection string, and
SqlClient resets them on every pooled open. Retrying (`Retry`) is SQL Server's own strategy. Each is
`Rask:SqlServer` in `appsettings.json` (`"LockTimeout": "00:00:03"`, `"Retry": { "MaxCount": 3 }`); a callback —
`UseRaskSqlServer(sp, s => …)` — runs after the section and wins.

Everything in this guide works unchanged, with the same three things to know as on PostgreSQL:

- **Retrying refuses a transaction you open yourself** outside the execution strategy — wrap it in
  `context.Database.CreateExecutionStrategy().ExecuteAsync(...)`, or set `Rask:SqlServer:Retry:Enabled` to `false`.
- **Litestream and snapshots do not apply.** Back up with `BACKUP DATABASE` or your provider's snapshots.
- **Open connections through EF** (`context.Database.OpenConnectionAsync()`) in hand-written ADO code, or the
  session settings are not sent.

One model detail is handled for you: a SQL Server index key holds 450 `nvarchar` characters, and `Rask.Cache`
configures its key at 512 for the other providers. `UseRaskSqlServer` caps that key — and only that key — at 450.
A longer cache key cannot be stored there; `ICache` rejects it with an error naming the limit, so hash long keys.

## Notes

- **Server-side.** These interceptors run against a real EF Core provider (SQLite by default in Rask);
  they are not used on the WASM client.
- **Trim/AOT-safe.** The model convention runs at startup (not the hot path) and uses no runtime handler
  reflection; domain-event dispatch goes through `Rask.Cqrs`' source-generated registry.
- **Durable delivery.** For at-least-once, crash-safe events, pair the entity with
  [`Rask.Outbox`](outbox.md), which persists events in the same transaction and drains them from a
  background worker (and disables the in-process dispatcher to avoid double delivery).
