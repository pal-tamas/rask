# Rask.Data

A data layer for **Entity Framework Core** apps with one goal: **you declare models, and that is all**.
No `DbContext` class, no `DbSet` property, no `IEntityTypeConfiguration`, no registration — and no
`IDbContextFactory` injected into everything that reads a row. Underneath it is ordinary EF Core, and
`Db.Current` is the real `DbContext` whenever you want it.

- **`Model<TId>`** — a base entity with `Id`, audit stamps (`CreatedAt`/`UpdatedAt`), and a
  domain-events buffer. A source generator finds every one of them and builds the model, so nothing is
  scanned or reflected and a trimmed publish cannot quietly drop a table.
- **The model type is its own `DbSet`** — `Product.Where(...)`, `Product.FindAsync(id)`,
  `Product.Add/Update/Remove(...)`, `Product.CountAsync()`. C# 14 static extension members, so an entity
  that compiles today has them. Reads are no-tracking by default and open no context until they run.
- **`Db.Begin()`** — one short-lived ambient `DbContext` per unit of work, committed by a single
  `SaveChangesAsync`. Nesting joins rather than nests, so `await entity.SaveAsync()` inside a caller's
  transaction takes part in it instead of committing half of it.
- **Value objects** (`IValueObject`) map as EF **complex types**, not owned entities; **strongly-typed
  ids** get a generated value converter with nothing declared; mapping rules live in a plain
  `public static void Configure(EntityTypeBuilder<T>)` on the model.
- **`TestDatabase.StartAsync`** — a real database for a test in one line, so behaviour on a model is
  tested against the database it ships on rather than a mocked `DbContext`.
- **Opt-in markers** — implement `ISoftDeletable` (adds `DeletedAt`) or `IVersioned` (adds a `Version`
  concurrency token) on your entity to turn on the behavior.
- **Three `ISaveChangesInterceptor`s** — auditing timestamps, **transparent soft delete** (a `Remove`
  becomes a `DeletedAt` stamp behind a global query filter), and **after-commit domain-event publication**
  through [Rask.Cqrs](https://www.nuget.org/packages/Rask.Cqrs).
- **`BulkInsertAsync`** — the bulk insert EF Core leaves out (`ExecuteUpdate`/`ExecuteDelete` exist; inserts
  are out of its scope). Batched, with the change tracker cleared as it goes so memory stays flat.

## Use

```csharp
public sealed class Product : Model<Guid>, ISoftDeletable, IVersioned
{
    public string Name { get; private set; } = "";
    public DateTime? DeletedAt { get; private set; }
    public int Version { get; private set; }

    public static Product Create(string name) => new() { Id = Guid.NewGuid(), Name = name };
}

// read — no context in scope, nothing left open
var active = await Product.Where(p => p.DeletedAt == null).OrderBy(p => p.Name).ToListAsync();

// write — one transaction over everything it touches
await using var uow = Db.Begin();
Product.Add(Product.Create("Anvil"));
Product.Remove(discontinued);
await uow.SaveChangesAsync();
```

In a Rask app that is the whole of it — the host builds the model and points the ambient database at it.
Elsewhere, name the context once and hand it over after the container is built:

```csharp
builder.Services.AddRaskCqrs();
builder.Services.AddRaskData<AppDbContext>();
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseSqlite("Data Source=app.db")
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

var app = builder.Build();
Db.Configure(app.Services);
```

A class that does **not** derive from `Model` stays an ordinary EF Core entity: write your own context
and configurations and use them exactly as before. Registering an `IDbContextFactory<YourContext>` is
the whole of opting out at the app level.

`db.Remove(product)` now soft-deletes; deleted rows drop out of queries (use `IgnoreQueryFilters()` to
restore); a save against a stale `Version` throws `DbUpdateConcurrencyException`; and any
`INotification` raised on the entity is published after the change commits.

To load many rows at once — seeding, an import, a migration — `await db.BulkInsertAsync(products)` (or
`db.Products.BulkInsertAsync(...)`) saves them in batches, clearing the change tracker between each so memory
stays flat. The interceptors above still run for every row. Each batch commits on its own so a long import
does not hold SQLite's only write lock end to end; `o.SingleTransaction = true` makes it all-or-nothing.

For the fastest load, `o.SkipChangeTracking = true` writes the rows with one prepared `INSERT` and no entity
entries at all. It is opt-in because it runs **no** `ISaveChangesInterceptor` — the writer stamps the audit
columns itself, but entities carrying domain events are rejected rather than silently losing them, and
anything it cannot map faithfully throws and names the reason.

## Non-overlapping ranges

A booking, a lease, a price valid for a period: two rows must not cover the same point. SQLite has no
`EXCLUDE` constraint and a `UNIQUE` index only stops *identical* rows, so declare the rule on the model
instead:

```csharp
modelBuilder.Entity<Booking>()
    .HasNonOverlappingRange(x => x.StartsAt, x => x.EndsAt, partitionBy: x => x.RoomId);
```

Ranges are half-open (`[lo, hi)`), so `100-200` and `200-300` are neighbours rather than a conflict. With
`Rask.SQLite.EntityFrameworkCore`'s `UseRaskSqlite(...)`, migrations emit the triggers that enforce it and a
violating save throws `RangeOverlapException`. Enforcement lives in the database, so raw SQL is bound by it
too.

Part of the [Rask](https://github.com/pal-tamas/rask) framework. MIT licensed.
