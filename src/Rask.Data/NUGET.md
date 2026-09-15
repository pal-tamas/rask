# Rask.Data

A data layer for **Entity Framework Core** apps with one goal: **you declare models, and that is all**.
No `DbContext` class, no `DbSet` property, no `IEntityTypeConfiguration`, no registration — and no
`IDbContextFactory` injected to read a row or save a form. Underneath it is ordinary EF Core, and anything
richer than a create, an update or a delete is EF Core exactly as you know it.

- **`Model<TId>`** — a base entity with `Id` and a domain-events buffer. A source generator finds every one
  of them and builds the model, so nothing is scanned or reflected and a trimmed publish cannot quietly drop
  a table.
- **Reads off the type** — `Product.Where(...)`, `Product.FindAsync(id)`, `Product.CountAsync()`,
  `Product.AsQueryable()`. C# 14 static extension members, so an entity that compiles today has them.
  **Every read is untracked and opens and disposes its own context**, which is what makes them safe on a
  page that lives as long as a browser's socket. `AsQueryable()` is a standard `IQueryable<T>` that opens a
  context per execution — hand it to a data grid and it sorts and pages in the database.
- **A generated `ProductModel` for forms** — a settable copy of each entity's mapped properties, with its
  DataAnnotations and `Version` carried and the key left out, so `Form.Model(model)` validates by the entity's
  own rules. It is the whitelist a create or update writes from: `[SkipModel]` keeps a property off it.
- **Hints, not rules** — build warnings with lightbulb fixes point out a public setter or field on a model or
  value object (RASK084) and an entity exposing a mutable collection of entities (RASK085). Public setters
  are allowed; the warnings never fail a build that does not ask them to.
- **Writes off the type** — creates read like their updates: `Product.CreateAsync(model)` /
  `Product.UpdateAsync(id, model)`, `Product.CreateAsync(p => …)` / `Product.UpdateAsync(id, p => …)`, plus
  `Product.CreateAsync(entity)` for a built entity and `Product.DeleteAsync(id)`. A form's values go through the generated
  model; values that do not come from the form go in an optional `p => …`; the id is always the caller's, never
  the form's; an `IVersioned` edit refuses a stale save. Each takes an optional `DbContext` to join a caller's
  transaction. The interceptors stamp, version, soft-delete and publish as for any save — and anything richer
  is plain EF Core through `IDbContextFactory<TContext>`.
- **Value objects** (`IValueObject`) map as EF **complex types**, not owned entities; **strongly-typed
  ids** get a generated value converter with nothing declared; mapping rules live in a plain
  `public static void Configure(EntityTypeBuilder<T>)` on the model.
- **`TestDatabase.StartAsync`** — a real database for a test in one line, so behaviour on a model is
  tested against the database it ships on rather than a mocked `DbContext`.
- **Opt-in markers** — implement `ITimestamped` (adds `CreatedAt`/`UpdatedAt`), `ISoftDeletable` (adds
  `DeletedAt`) or `IVersioned` (a `Version` concurrency token) on your entity to turn on the behavior.
- **Three `ISaveChangesInterceptor`s** — auditing timestamps, **transparent soft delete** (a delete
  becomes a `DeletedAt` stamp behind a global query filter), and **after-commit domain-event publication**
  through [Rask.Cqrs](https://www.nuget.org/packages/Rask.Cqrs).
- **`BulkInsertAsync`** — the bulk insert EF Core leaves out (`ExecuteUpdate`/`ExecuteDelete` exist; inserts
  are out of its scope). Batched, with the change tracker cleared as it goes so memory stays flat.

## Use

```csharp
public sealed class Product : Model<Guid>, ISoftDeletable, IVersioned
{
    private Product() { }

    [Required, MaxLength(200)]
    public string Name { get; private set; } = "";
    public int Version { get; private set; }
}

// read — no context in scope, nothing left open, nothing tracked
var products = await Product.OrderBy(p => p.Name).ToListAsync();

// write — off the type too; the form model is the whitelist, the id is yours
var product = await Product.CreateAsync(model);
await Product.UpdateAsync(product.Id, edit);   // only changed columns; a stale Version throws
await Product.DeleteAsync(product.Id);         // soft delete

// anything richer — plain EF Core, one context, one transaction (the writes above join it with db: db)
await using var db = await contexts.CreateDbContextAsync(ct);
var order = await db.Set<Order>().FirstAsync(o => o.Id == orderId, ct);
order.Cancel(DateTime.UtcNow);
await db.SaveChangesAsync(ct);
```

In a Rask app that is the whole of it — the host builds the model and points the model surface at it.
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

A delete — `db.Remove(product)` — soft-deletes an `ISoftDeletable`; deleted
rows drop out of queries (use `IgnoreQueryFilters()` to restore); a save against a stale `Version` throws
`DbUpdateConcurrencyException`; and any `INotification` raised on the entity is published after the change
commits.

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
too. On a provider that emits no such DDL — a plain `UseSqlite`, or any other — the rule would be silently
ignored, so `AddRaskData<TContext>()` refuses to boot instead, naming the entity and the call that enforces it.

## Full-text search

Declare which text is searchable, and search it from LINQ — ranked, word-aware, diacritic-insensitive:

```csharp
modelBuilder.Entity<Post>().HasFullTextSearch(p => new { p.Title, p.Body });

var hits = await Post.Search(query).Where(p => p.Published).Take(20).ToListAsync();
var marked = await Post.Search(query).Select(p => FullText.Snippet(p.Body)).ToListAsync();
```

`Search(text)` works on `Post`, on a `ModelQuery` and on any EF Core `IQueryable`; it returns best matches
first and keeps composing. Typed text is always words, never FTS query syntax. With
`Rask.SQLite.EntityFrameworkCore`'s `UseRaskSqlite(...)`, migrations create an FTS5 index kept current by
triggers — adding it to an existing table fills it. Other providers are refused at boot: full-text search is
SQLite-only for now.

Part of the [Rask](https://github.com/pal-tamas/rask) framework. MIT licensed.
