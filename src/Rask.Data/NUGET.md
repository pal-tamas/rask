# Rask.Data

A data layer for **Entity Framework Core** apps with one goal: **you declare aggregates, and that is all**.
No `DbContext` class, no `DbSet` property, no `IEntityTypeConfiguration`, no registration, and no
`IDbContextFactory` injected to read a row or save a form. Underneath it is ordinary EF Core, and anything
richer than a create, an update or a delete is EF Core exactly as you know it.

- **`Aggregate<TId>` and `Entity<TId>`**: domain-driven design's building blocks as base classes. An entity carries
  `Id`, `CreatedAt` and `UpdatedAt`; an aggregate adds a `Version` concurrency token and domain events, and
  `DeletedAt` soft delete when it declares `Deletes = Deletion.Soft`. A source generator finds every one and builds the model, so nothing is scanned or reflected and
  a trimmed publish cannot quietly drop a table.
- **Value objects with no marker**: any record, struct or class an entity holds that is not an entity maps as an
  EF **complex type**, columns on the owner's row. **Strongly-typed ids** get a generated value converter with
  nothing declared; mapping rules live in a plain `public static void Configure(EntityTypeBuilder<T>)` on the type.
- **Reads through a generated read face**: `Product.Read.Where(...)`, `Product.Read.CountAsync()`,
  `Product.Read.Search(text)`, `Product.Read.AsQueryable()` — one by id is
  `Product.Read.Where(p => p.Id == id).FirstOrDefaultAsync()`, since EF Core's `Find` would bypass the query
  filters. A C# 14 static extension member, so an aggregate that compiles today has it.
  **Every read is untracked and opens and disposes its own context**, which is what makes them safe on a
  page that lives as long as a browser's socket. `AsQueryable()` is a standard `IQueryable<T>` that opens a
  context per execution: hand it to a data grid and it sorts and pages in the database.
- **A generated `ProductModel` for forms**: a nullable, settable copy of each aggregate's mapped properties, with
  its DataAnnotations and `Version` carried and the key left out, so `Form.Model(model)` validates by the
  aggregate's own rules. `new ProductModel()` holds the aggregate's defaults and `product.ToModel()` fills an edit
  form.
- **Writes off the type**: creates read like their updates: `Product.CreateAsync(model)` /
  `Product.UpdateAsync(id, model)`, `Product.CreateAsync(p => …)` / `Product.UpdateAsync(id, p => …)`, plus
  `Product.CreateAsync(entity)` for one built by a factory and `Product.DeleteAsync(id)`, which removes the row (or stamps `DeletedAt` under
  `Deletion.Soft`; `Deletion.None` generates no delete at all). A save
  writes what the form holds; values that do not come from the form go in an optional `p => …`; the id is always
  the caller's, never the form's; a stale `Version` is refused. Each takes an optional `DbContext` to join a
  caller's transaction.
- **State stays inside**: a public setter or mutable field on an aggregate, entity or value object is a build
  error with a lightbulb fix (RASK084), and an entity exposing a mutable collection of entities is a warning
  (RASK085).
- **`TestDatabase.StartAsync`**: a real database for a test in one line, so behaviour on an aggregate is
  tested against the database it ships on rather than a mocked `DbContext`.
- **Three `ISaveChangesInterceptor`s**: auditing timestamps and versions, **opt-in soft delete** (for an aggregate
  that declares it, a delete becomes a `DeletedAt` stamp behind a global query filter), and **after-commit domain-event publication**
  through [Rask.Cqrs](https://www.nuget.org/packages/Rask.Cqrs).
- **`Current` and multi-tenancy**: `Current.UserId`, `Current.Principal` and `Current.Tenant` answer from a
  static factory or anywhere else with no constructor to inject into. `public const Tenancy Scope =
  Tenancy.PerTenant;` gives a table a `TenantId`, a query filter no read can compose away and a tenant prefix on
  every index; the tenant comes from the signed-in user.
- **`BulkInsertAsync`**: the bulk insert EF Core leaves out (`ExecuteUpdate`/`ExecuteDelete` exist; inserts
  are out of its scope). Batched, with the change tracker cleared as it goes so memory stays flat.

## Use

```csharp
public sealed class Product : Aggregate<Guid>
{
    [Required, MaxLength(200)]
    public string Name { get; private set; } = "";
    public Money Price { get; private set; } = new(0m, "EUR");

    public static Product Create(string name, Money price) =>
        new() { Id = Guid.CreateVersion7(), Name = name, Price = price };
}

public sealed record Money(decimal Amount, string Currency);

// read: no context in scope, nothing left open, nothing tracked
var products = await Product.Read.OrderBy(p => p.Name).ToListAsync();

// write: off the type too; the form model carries the values, the id is yours
var product = await Product.CreateAsync(model);
await Product.UpdateAsync(product.Id, edit);   // only changed columns; a stale Version throws
await Product.DeleteAsync(product.Id);         // removes the row

// anything richer: plain EF Core, one context, one transaction (the writes above join it with db: db)
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

In a WebAssembly app `AddRaskData<AppDbContext>()` is the whole of it: the package's browser build points the model
surface at the context itself, and — with `AddRaskQuery()` — a save refreshes the page's Rask.Query queries about
what it wrote, as it does in a Rask server session.

A class that does **not** derive from `Entity<TId>` stays an ordinary EF Core entity: write your own context
and configurations and use them exactly as before. Registering an `IDbContextFactory<YourContext>` is
the whole of opting out at the app level.

A delete, `db.Remove(product)`, removes the row — or, for an aggregate that declares
`Deletes = Deletion.Soft`, stamps `DeletedAt` so the row drops out of queries (`IgnoreQueryFilters()` brings it
back); a save against a stale `Version` throws
`DbUpdateConcurrencyException`; and any `INotification` raised on the aggregate is published after the change
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

var hits = await Post.Read.Search(query).Where(p => p.Published).Take(20).ToListAsync();
var marked = await Post.Read.Search(query).Select(p => FullText.Snippet(p.Body)).ToListAsync();
```

`Search(text)` works on a read face, on a `ModelQuery` and on any EF Core `IQueryable`; it returns best
matches first and keeps composing. Typed text is always words, never query syntax. The index arrives through
a migration and the database keeps it current:

- **SQLite** — `UseRaskSqlite(...)` from `Rask.SQLite.EntityFrameworkCore`, or a plain `UseSqlite` plus
  `UseRaskFullTextSearch()`: an FTS5 index kept current by triggers; adding it to an existing table fills it.
- **In the browser** — `Rask.SQLite.Browser`'s `UseSqlite(BrowserSqlite.ConnectionString("app"))` plus the same
  `UseRaskFullTextSearch()`: the same FTS5 index in the WebAssembly app's local database.
- **PostgreSQL** — `UseRaskPostgres(...)` from `Rask.Postgres`: a stored generated `tsvector` column with a GIN
  index, no triggers.

On SQL Server, or a plain `UseSqlite` without `UseRaskFullTextSearch()`, `AddRaskData<TContext>()` refuses to
boot a context that declares an index.

A value inside a JSON column gets its own index the same way: `HasJsonIndex(o => o.Meta.Status)` adds an
expression index spelled exactly as EF Core writes the filter, so the filter is an index search. SQLite only
for now, and refused at boot elsewhere. See the
[full-text search guide](https://github.com/pal-tamas/rask/blob/main/docs/full-text-search.md).

Part of the [Rask](https://github.com/pal-tamas/rask) framework. MIT licensed.
