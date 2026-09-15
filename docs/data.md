# Rask.Data — models, queries, and the database you don't write

> **In practice:** [Tutorial Ch 2](tutorial/02-first-feature.md) · recipe [add a feature to an existing database](recipes.md#add-a-feature-to-an-existing-database) · [cheat sheet](cheatsheet.md).

`Rask.Data` is a layer over **Entity Framework Core** with one goal: **you declare models, and that is
all**. No `DbContext` class, no `DbSet` property, no `IEntityTypeConfiguration`, no registration — and
no `IDbContextFactory` injected into every page that reads a row.

Underneath it is ordinary EF Core, and nothing is hidden from you. **The model type reads and writes** —
`Product.Where(…)`, `Product.CreateAsync(model)`, `Product.UpdateAsync(id, model)`, `Product.DeleteAsync(id)`
([below](#writing-create-update-delete)) — anything richer is EF Core exactly as you know it, a domain method
saved through a context ([below](#writing-plain-ef-core)), and an app that outgrows the conventions writes its
own context and Rask steps aside ([below](#using-ef-core-the-usual-way)).

> Included in the [`Rask`](../README.md) package — nothing to install. It is **on**; an app that does without it says so:
>
> ```csharp
> app.Configure(c => c.Data.Off());
> ```

## The whole of it

```csharp
public sealed class Product : Model<Guid>, ITimestamped, IVersioned
{
    private Product() { }                                  // EF materialization

    [Required, MaxLength(200)]
    public string Name   { get; private set; } = "";
    public decimal Price { get; private set; }
    public int Version   { get; private set; }             // IVersioned's token — see below

    public static Product Create(string name, decimal price) =>
        new() { Id = Guid.CreateVersion7(), Name = name, Price = price };

    public void Rename(string name) => Name = name;

    public void Reprice(decimal price)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(price);
        Price = price;
    }
}
```

That compiles into a mapped table, a generated `ProductModel` [for its forms](#a-create-and-an-edit-form),
and everything a screen needs to read it:

```csharp
var cheap = await Product.Where(p => p.Price < 10).OrderBy(p => p.Name).ToListAsync();
var anvil = await Product.FindAsync(id);
var grid  = Product.OrderBy(p => p.Name).AsQueryable();   // for UiDataGrid — sorted and paged in SQL
```

And to write it — from a form, from code, or inside a transaction you already hold:

```csharp
var product = await Product.CreateAsync(model);                        // ProductModel from a form
await Product.UpdateAsync(product.Id, edit);                           // stale Version throws

var other = await Product.CreateAsync(p => p.Reprice(9.90m));          // no form at all …
await Product.UpdateAsync(other.Id, p => p.Reprice(12.50m));           // … and the same shape to change it

await Product.DeleteAsync(product.Id);
```

Anything richer is EF Core, through a context that saves what the entity's own methods changed:

```csharp
await using var db = await contexts.CreateDbContextAsync(ct);   // IDbContextFactory<RaskAppDbContext>

var product = await db.Set<Product>().FindAsync([id], ct);
product!.Reprice(12.50m);
await db.SaveChangesAsync(ct);   // stamped, versioned, events published
```

A source generator finds every `Model` at build time and hands it to `RaskAppDbContext`; the host points
the model surface at the database. There is nothing else to write and nothing to register.

**Generated, never reflected.** No assembly is scanned and no method is found by name, so a trimmed
publish cannot quietly drop an entity and leave you a missing table with a green build.

`Model<TId>` carries `Id` and a domain-events buffer (`Raise` / `DomainEvents` / `ClearDomainEvents`).
**Everything else is opt-in, and opting in does not put anything on your class** — the marker alone is
enough, and the column is added as an EF *shadow property*:

| Interface | Column | Effect |
|-----------|--------|--------|
| `ITimestamped` | `CreatedAt`, `UpdatedAt` | Stamped on insert and on every update. |
| `ISoftDeletable` | `DeletedAt` | A delete becomes a `DeletedAt` stamp; a global query filter hides it. |
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
Optimistic concurrency exists to round-trip the token through an edit form — the command the form binds
carries it there and back — and a value the application cannot read is one it cannot send back. A model that marks
itself versioned without the property is refused while the model is built, by name, rather than failing
later as an update that matched no row.

## Reading: the model type is its own query

Every `Model` gains the read half of `DbSet` as static members, so a query needs no context in scope:

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
derived from a second base. A member declared on the model itself always wins, so your own `Find` is
untouched.

**Every read is untracked, and every read opens and disposes its own context.** Composing holds nothing
open: the terminal call opens a context, runs, and disposes it before it returns — so this is a complete
statement anywhere, including a component's `OnMountAsync`:

```csharp
protected override async Task OnMountAsync() =>
    _products = await Product.Where(p => p.Active).OrderBy(p => p.Name).ToListAsync(CancellationToken);
```

That is the shape a live page needs, not a default to tune. A Rask page lives as long as the browser
keeps its socket open, and a `DbContext` is neither thread-safe nor meant to accumulate a session's worth
of entities — so nothing here holds one between calls, and there is no `AsTracking()` to ask for one.
Most reads are rendered and never written back anyway, and tracking them would cost a graph walk and an
identity-map entry to buy nothing.

**The consequence to know:** a row that comes back is a plain object nothing is watching, so changing it
and expecting a save does nothing. A change goes back through [a write on the type](#writing-create-update-delete)
or [a context](#writing-plain-ef-core), and both load the entity they are about to change.

`FindAsync(id)` is a read like the others: an untracked query by primary key, with the global query
filters applied, so a soft-deleted row is not found. Its key is typed `object`, as EF Core's is, because
a key may be composite — an overload takes the values of one.

For a shape this does not wrap — a group-by, a join, an aggregate — `QueryAsync` hands you the live
`IQueryable` inside a managed context:

```csharp
var byMonth = await Product.All.QueryAsync((q, ct) =>
    q.GroupBy(p => p.CreatedAt.Month)
     .Select(g => new { Month = g.Key, Total = g.Sum(p => p.Price) })
     .ToListAsync(ct));
```

### Handing a query to a component: `AsQueryable()`

A data grid composes its own LINQ — it sorts with `OrderBy`, pages with `Skip`/`Take`, counts with
`Count()` — so what it wants is a standard `IQueryable<T>`, not a query somebody has to run.
`Product.AsQueryable()` is that, and it holds no context either: **each time it is executed, a context is
opened for that one execution and disposed after it.** So it is safe to keep in a field for as long as
the page lives:

```csharp
[Route("/products")]
public sealed partial class ProductsPage : Component
{
    private readonly IQueryable<Product> _products = Product.Where(p => p.Price > 0).AsQueryable();

    protected override Component Render() =>
        UiDataGrid.Data(_products).RowKey(p => p.Id).PageSize(25)[c => [
            c.Field(p => p.Name).Title("Product").Sortable(true),
            c.Field(p => p.Price).Title("Price").Sortable(true),
        ]];
}
```

The grid's sort becomes `ORDER BY` and its page becomes `LIMIT`/`OFFSET`, in the database — the table
never reaches memory, however large it is. A synchronous `Count()` and an awaited `ToListAsync()` both
work against it; each is its own short-lived context. See [the data grid](data-grid.md) for the rest of
what the grid does with a query.

**EF Core's own operators go on first.** `Include`, `IgnoreQueryFilters` and `AsSplitQuery` are EF Core
extension methods, and EF applies them only to its own query provider — called on the queryable
`AsQueryable()` returns, they do nothing, silently. Put them on the model query, before the hand-off:

```csharp
Product.Include(p => p.Reviews).AsQueryable();        // ✓ travels with the queryable
Product.IgnoreQueryFilters().AsQueryable();           // ✓ soft-deleted rows too

Product.AsQueryable().Include(p => p.Reviews);        // ✗ compiles, loads no reviews
```

## Writing: create, update, delete

The writes live on the type, beside the reads, and a create reads like the update it pairs with — the update just
names the row first:

| Create | Update | From |
| --- | --- | --- |
| `Product.CreateAsync(model)` | `Product.UpdateAsync(id, model)` | a form: the generated `ProductModel` |
| `Product.CreateAsync(model, p => …)` | `Product.UpdateAsync(id, model, p => …)` | a form, plus values it does not carry |
| `Product.CreateAsync(p => …)` | `Product.UpdateAsync(id, p => …)` | code, with no form behind it |

- **A create** builds a `Product` from its parameterless constructor, applies the model and then the lambda, and
  inserts it. The key is a new version-7 `Guid` (or the store's identity for an integer key).
- **`Product.CreateAsync(id, model)` / `Product.CreateAsync(id, p => …)`** do the same under a key you give — the
  only creates for a key nothing can produce, such as a strongly-typed id over a `string`. They are not generated
  for an integer key the database produces: an explicit identity value fails on SQL Server and leaves
  PostgreSQL's sequence behind.
- **`Product.CreateAsync(entity)`** inserts an entity you built with its own factory —
  `Product.CreateAsync(Product.Create("Anvil", 30m))`.
- **An update** loads the row, applies the model and then the lambda, and saves **only the columns that changed**.
- **`Product.DeleteAsync(id)`** loads the row and deletes it — a `DeletedAt` stamp for an `ISoftDeletable`.

Each one goes through EF Core's change tracker, so the interceptors run exactly as for a hand-written save:
`CreatedAt`/`UpdatedAt` are stamped, `Version` is bumped, a delete of an `ISoftDeletable` becomes a stamp, and the
entity's domain events are published after the commit.

- **The form model is the whitelist.** A create or update writes the model's properties and nothing else — the
  entity's private setters included, through generated `[UnsafeAccessor]`s, with no reflection. A property marked
  `[SkipModel]` is not on the model, so no form can write it (Laravel's `$guarded`, Rails' unpermitted param).
- **The id is always yours.** The model carries none; the row is addressed by the `id` you pass, never by anything
  a form posted.
- **Values that do not come from the form** go in an optional lambda, which runs after the model's values are
  written, so it has the last word: `Product.CreateAsync(model, p => p.AssignTo(user.Id))`. A lambda that
  assigns a property — `p => p.PublishedAt = DateTime.UtcNow` — needs that setter to be reachable from your code;
  with private setters, call the entity's method.
- **A stale edit is refused.** On an `IVersioned` entity, `UpdateAsync(id, model)` checks the `Version` the model
  carries, and throws `DbUpdateConcurrencyException` when someone saved since. `UpdateAsync(id, p => …)` and
  `DeleteAsync(id)` take an optional `version:` for the same check.
- **A missing row** — never created, or soft-deleted — is `KeyNotFoundException`, naming the entity and the key.

A create or update needs a parameterless constructor to start from (a private one is fine — EF Core needs it
too). An entity without one still gets `UpdateAsync` and `DeleteAsync`, and the build says why it has no
`CreateAsync` ([RASK086](diagnostics.md#rask086)); insert one built by its own factory with
`Product.CreateAsync(entity)` instead.

### Joining a context you already have

Without a context, each write opens one, saves and disposes it — the same as a read. Pass `db:` and it works in
**that** context instead: it saves it — with anything else pending there — and leaves it open, so several writes
share one transaction:

```csharp
await using var db = await contexts.CreateDbContextAsync(ct);
await using var transaction = await db.Database.BeginTransactionAsync(ct);

var order = await Order.CreateAsync(orderModel, db: db, cancellationToken: ct);
await StockItem.UpdateAsync(stockId, s => s.Reserve(orderModel.Quantity), db: db, cancellationToken: ct);

await transaction.CommitAsync(ct);   // both rows, or — on an exception before this line — neither
```

A row that context already tracks is the one updated, not a second copy.

### A create and an edit form

A form edits something mutable, so every model gets a **form shape generated beside it**: `ProductModel` for
`Product`. It is a plain class with a settable copy of every mapped property, and the entity's validation
attributes are copied onto it, so `Form.Model(…)` checks input by the entity's own rules as the user types.
It carries `Version` when the entity is `IVersioned`, and never the `Id`. A create page starts from an empty one:

```csharp
[Route("/products/new")]
public sealed partial class NewProductPage(Navigator nav) : Component
{
    private readonly ProductModel _product = new();

    protected override Component Render() =>
        Form.Model(_product).OnValidSubmit(CreateAsync)[submitting => [
            UiInput.Bind(() => _product.Name).Label("Name"),
            UiInput.Bind(() => _product.Price).Label("Price"),
            UiButton.Type(UiButtonType.Submit).Disabled(submitting)["Create"],
        ]];

    private async Task CreateAsync(ProductModel product)
    {
        await Product.CreateAsync(product, cancellationToken: CancellationToken);
        nav.NavigateTo(Routes.ProductsPage());
    }
}
```

An edit page fills one from the row it read, and hands it back with the route's id:

```csharp
[Route("/products/{id:guid}/edit")]
public sealed partial class EditProductPage(Navigator nav) : Component
{
    [RouteParam] public Guid Id { get; set; }

    private ProductModel? _product;
    private string? _conflict;

    protected override async Task OnMountAsync() =>
        _product = await Product.FindAsync(Id, CancellationToken) is { } p
            ? new ProductModel { Name = p.Name, Price = p.Price, Version = p.Version }
            : null;

    protected override Component? Render() =>
        _product is null ? P["Loading…"] :
        Form.Model(_product).OnValidSubmit(SaveAsync)[
            _conflict is null ? null : UiAlert.Tone(UiTone.Warning)[_conflict],
            UiInput.Bind(() => _product.Name).Label("Name"),
            UiInput.Bind(() => _product.Price).Label("Price"),
            UiButton.Type(UiButtonType.Submit)["Save"],
        ];

    private async Task SaveAsync(ProductModel edit)
    {
        try
        {
            await Product.UpdateAsync(Id, edit, cancellationToken: CancellationToken);   // Version checked
            nav.NavigateTo(Routes.ProductsPage());
        }
        catch (DbUpdateConcurrencyException)
        {
            _conflict = "Someone saved this product while you were editing it.";
        }
    }
}
```

The `[Required, MaxLength(200)]` on `Name` is the entity's, copied to the model, so both forms refuse an empty
or overlong name before anything is saved — and EF Core reads the same attributes for the column.

**Or send it in a command.** A command, a query, an API endpoint or an island prop can carry a `ProductModel`
(`public sealed record AddProduct(ProductModel Product) : ICommand<Guid>`), and the generators that build their
codecs recognise it although it is itself generated; the handler calls `Product.CreateAsync(command.Product)`. A
hand-written command class with its own attributes works exactly the same way — it is what the
[tutorial](tutorial/02-first-feature.md) builds.

**Your own write wins.** A static `CreateAsync(ProductModel, …)` or `DeleteAsync(Guid, …)` declared on `Product`
replaces the generated one at every call site — a member on the type beats an extension member — and the
generated one stays reachable as `ProductModelExtensions.CreateAsync(…)` for an override that only adds to it.

### What the model leaves out

- **The `Id`**, the framework's columns (`CreatedAt`, `UpdatedAt`, `DeletedAt`, the domain events), navigations,
  collections and computed properties.
- **A property marked `[SkipModel]`** (from `Rask.Data`) — a status only `Ship()` moves, a total the entity
  computes. `[SkipModel]` on the **class** generates no model at all.
- **Nothing about a value object's shape:** `Money Total` on `Order` is `MoneyModel Total` on `OrderModel`, so a
  form binds `() => _order.Total.Amount` like any [nested model](forms-advanced.md), however `Money` is declared.

A `partial class ProductModel` of your own merges into the generated one — the way to add members,
`IValidatableObject` or display helpers. The build says when it cannot generate: a **hand-written, non-`partial`
`ProductModel`** beside a `Product` is an error ([RASK082](diagnostics.md#rask082)), and a **nested entity** — a
model declared inside another class — is a warning and gets no model ([RASK083](diagnostics.md#rask083)).

On the wire a model's properties are **camelCase**. Only validation attributes are copied from the entity, so a
`[JsonPropertyName]` on the entity does not rename the model's property; a property you declare yourself on a
`partial ProductModel` keeps its own pin.

## Writing: plain EF Core

The writes on the type cover a create, an update and a delete. **Everything past that is ordinary EF Core** — a
change several entities make together, a query-then-decide, a bulk statement: load the entities into a context,
call their methods, save — so the entity's own rules run on every write, and so do the interceptors:
`CreatedAt`/`UpdatedAt` are stamped, `Version` is bumped, a delete of an `ISoftDeletable` becomes a
`DeletedAt` stamp, and the entity's domain events are published after the commit. There is no Rask-owned
unit of work to learn.

`RaskAppDbContext` (namespace `Rask`) is the context the host builds: every model you declared, plus every
battery's tables. It has no `DbSet` properties, so an entity is reached with `db.Set<Order>()`. An app that
[registered its own context](#using-ef-core-the-usual-way) uses that one the same way — which includes every
app `rask new` scaffolds: it writes `Features/Shared/AppDbContext.cs`, so there the factory below is
`IDbContextFactory<AppDbContext>`.

**Take the factory, and make one context per change.** `IDbContextFactory<RaskAppDbContext>` is registered
for you. A context is short-lived and not thread-safe, and the places a write runs from are not: a live page
lives as long as the browser keeps its socket open, and a command it dispatches in-process resolves its
handler from that page's session, not from a fresh scope. A context injected directly would be shared by
every write of the session.

### In a command handler

The shape the [tutorial](tutorial/02-first-feature.md) builds: a command says what to change, and its
handler does it.

```csharp
public sealed record CancelOrder(Guid Id, int Version) : ICommand;

public sealed class CancelOrderHandler(IDbContextFactory<RaskAppDbContext> contexts, TimeProvider clock)
    : ICommandHandler<CancelOrder>
{
    public async Task HandleAsync(CancelOrder command, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        var order = await db.Set<Order>().FindAsync([command.Id], ct)
                    ?? throw new KeyNotFoundException($"There is no order {command.Id}.");

        db.Entry(order).Property(o => o.Version).OriginalValue = command.Version;   // see Optimistic concurrency
        order.Cancel(clock.GetUtcNow().UtcDateTime);                                 // the decision, and its event

        await db.SaveChangesAsync(ct);                                               // stamped, versioned, published
    }
}
```

Work that has to land together — placing an order and reserving its stock — is one context and one
`SaveChangesAsync`, which is one transaction:

```csharp
public sealed class PlaceOrderHandler(IDbContextFactory<RaskAppDbContext> contexts)
    : ICommandHandler<PlaceOrder, Guid>
{
    public async Task<Guid> HandleAsync(PlaceOrder command, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        var stock = await db.Set<StockItem>().FirstAsync(s => s.Sku == command.Sku, ct);
        stock.Reserve(command.Quantity);

        var order = Order.Place(command.Sku, command.Quantity);
        db.Add(order);

        await db.SaveChangesAsync(ct);   // both rows in one transaction, or neither
        return order.Id;
    }
}
```

### On a page

A change too small to deserve a command — one button, one save — takes the factory straight into the page:

```csharp
[Route("/orders/{id:guid}")]
public sealed partial class OrderPage(IDbContextFactory<RaskAppDbContext> contexts) : Component
{
    [RouteParam] public Guid Id { get; set; }

    private async Task ShipAsync()
    {
        await using var db = await contexts.CreateDbContextAsync(CancellationToken);

        var order = await db.Set<Order>().FirstAsync(o => o.Id == Id, CancellationToken);
        order.Ship();
        await db.SaveChangesAsync(CancellationToken);
    }
}
```

The one thing to remember is the one from [Reading](#reading-the-model-type-is-its-own-query): rows from
`Product.Where(…)` are untracked, so load the entity you are about to change from the context that is going
to save it.

### Keeping state inside the entity

Two build warnings point at the places an entity's state can be changed from outside — hints, never errors.
Keeping state behind the entity's own constructor and methods keeps its invariants and its domain events in one
place, but an app that prefers open entities is free to:

- **Public setters are pointed out** ([RASK084](diagnostics.md#rask084)). A public `set` or `init` on a
  `Model` — or on an abstract base the app puts between `Model` and its entities, or on an `IValueObject` — and
  a public mutable field are reported as a warning, with a lightbulb that makes the accessor `private`. They
  compile, map and save like any other property; EF Core simply does not need them public. Silence it with
  `dotnet_diagnostic.RASK084.severity = none` when open entities are the style you want. A positional record's
  parameters are exempt, so `record Money(decimal Amount, string Currency) : IValueObject` is never reported.
- **No mutable collections of entities** ([RASK085](diagnostics.md#rask085)). A navigation to many
  entities is exposed read-only, over a private field EF Core maps, and changed through a method. The
  lightbulb rewrites it:

```csharp
public sealed class Order : Model<Guid>
{
    private readonly List<OrderLine> _lines = [];

    public IReadOnlyCollection<OrderLine> Lines => _lines;

    public void AddLine(Guid productId, int quantity) => _lines.Add(OrderLine.For(productId, quantity));
}
```

**The entity owns its key.** Rask.Data's key convention leaves an integer key to the database's identity and
marks every other key never generated, so a `Guid` or a strongly-typed id is assigned where the entity is
built — `Id = Guid.CreateVersion7()` in its factory. That is what lets a child with an id of its own be added
to a loaded aggregate and saved as an insert. An entity added with its key still at the default is refused at
the save, by name, rather than inserted with an empty key. A key you configured yourself
(`ValueGeneratedOnAdd()`, a database default) is left as you set it.

### Batch update and delete

For work the database can do on its own, EF Core's `ExecuteUpdateAsync` and `ExecuteDeleteAsync` are one
statement over every matching row — nothing is loaded and nothing is tracked, so a million rows cost one
round trip rather than a million objects. They run on a context, like every other write:

```csharp
await using var db = await contexts.CreateDbContextAsync(ct);

await db.Set<Product>().Where(p => p.Discontinued)
    .ExecuteUpdateAsync(s => s
        .SetProperty(p => p.Active, false)
        .SetProperty(p => p.Price, p => p.Price * 0.9m), ct);   // the arithmetic happens in SQL

await db.Set<Order>().Where(o => o.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
```

**They bypass the interceptors**, and what they skip is the conventions this package otherwise maintains: no
`UpdatedAt` stamp, no `Version` bump, and no domain events — nothing was loaded to raise any. Set what you
need explicitly:

```csharp
await db.Set<Product>().Where(p => p.Discontinued)
    .ExecuteUpdateAsync(s => s
        .SetProperty(p => p.Active, false)
        .SetProperty(p => p.UpdatedAt, DateTime.UtcNow)
        .SetProperty(p => p.Version, p => p.Version + 1), ct);
```

**A batch soft delete is an update, not `ExecuteDeleteAsync`.** `db.Remove(product)` on an `ISoftDeletable`
stamps `DeletedAt`, but `ExecuteDeleteAsync` is a `DELETE` the interceptors never see — the rows are gone, not
hidden. Stamp them instead:

```csharp
await db.Set<Product>().Where(p => p.Discontinued)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.DeletedAt, DateTime.UtcNow), ct);
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

Behaviour that touches the database gets a real one in a line, rather than a mocked `DbContext`. Save
through `database.Context`, then read through the model surface exactly as the app does:

```csharp
await using var database = await TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={path}"));

var anvil = Product.Create("Anvil", 9.99m);
database.Context.Add(anvil);
await database.Context.SaveChangesAsync();

anvil.Reprice(12.50m);
await database.Context.SaveChangesAsync();   // stamped and versioned, exactly as in production

Assert.Equal(12.50m, (await Product.FindAsync(anvil.Id))!.Price);
```

```csharp
database.Context.AddRange(Order.Place("B-2"), Order.Place("B-3"));
await database.Context.SaveChangesAsync();

Assert.Equal(2, await Order.CountAsync());
```

`TestDatabase.StartAsync` maps every `Model` the build found — so no fixture has to list entities — creates the
schema, wires the auditing and soft-delete interceptors so the conventions behave as they do in
production, and points the model surface at it. Disposing clears it, so one test cannot leak its
database into the next. It takes a `TimeProvider`, so audit stamps are assertable.

It is provider-agnostic on purpose: the options callback is yours, so `Rask.Data` gains no provider
dependency and a test runs against the database the app actually uses. SQLite over a temporary file is
the usual choice; `:memory:` lives only as long as its connection, and every read opens its own.

Domain-event *publication* is deliberately not wired — that needs a service provider to resolve handlers
through, which is more than a database fixture should invent. Assert on `DomainEvents` instead.

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
value object* needs a parameterless constructor and settable properties — private ones, which is all
EF Core needs:

```csharp
// holds only scalars — a positional record is fine
public sealed record Money(decimal Amount, string Currency) : IValueObject;

// contains a value object — needs a parameterless ctor and settable properties (private ones are enough)
public sealed class Packaging : IValueObject
{
    private Packaging() { }

    public Packaging(Money cost, string material) => (Cost, Material) = (cost, material);

    public Money Cost { get; private set; } = new(0m, "EUR");

    public string Material { get; private set; } = "card";
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

Like any key that is not an integer, a strongly-typed id is the entity's to assign — `new ProductId(Guid.CreateVersion7())`
in its factory — because nothing generates one through a converter.

## Using EF Core the usual way

None of the above is compulsory, and opting out is not deriving from `Model`. A class that does not
derive from it is an ordinary EF Core entity: write your own `DbContext`, your own
`IEntityTypeConfiguration`, your own `DbSet` properties, and use them exactly as you do today.

Registering an `IDbContextFactory<YourContext>` is the whole of opting out at the app level. Rask binds
the model surface and every database-backed battery to the context you registered, and
`RaskAppDbContext` is never constructed. Call `modelBuilder.ApplyRaskConventions()` from its
`OnModelCreating` to keep the soft-delete filters and concurrency tokens, or
`ModelRegistry.Apply(modelBuilder)` to keep every declared `Model` mapped as well.

## Wiring, when Rask is not hosting

A Rask app needs none of this — the host does it. Anything else registers the context and points the
model surface at it once, after the container is built:

```csharp
builder.Services.AddRaskCqrs();                  // domain-event dispatch
builder.Services.AddRaskData<AppDbContext>();    // interceptors + names the context the models use

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
`Product.Where(…)` or `Product.FindAsync(…)` throws saying the type is not part of the model. If you
cannot change the base type — it is already someone else's — call `ModelRegistry.Apply(modelBuilder)` in
place of `base.OnModelCreating`, and `ModelRegistry.ApplyConventions(configurationBuilder)` from an
overridden `ConfigureConventions`.

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

`IVersioned` makes `Version` an EF Core concurrency token, bumped on every save. `Product.UpdateAsync(id, model)`
does the rest for you — it checks the `Version` the model carries — and `UpdateAsync(id, p => …)` and
`DeleteAsync(id)` take a `version:`. A hand-written save does it itself. The edit form carries the
version it was loaded at, and the handler compares against **that** version rather than the one it just
read — a freshly loaded row's original `Version` is whatever the database holds now, which would make every
check pass. So pin the caller's version as the tracked original value before saving:

```csharp
var product = await db.Set<Product>().FirstAsync(p => p.Id == command.Id, ct);
db.Entry(product).Property(p => p.Version).OriginalValue = command.Version;
product.Reprice(command.Price);
await db.SaveChangesAsync(ct); // throws DbUpdateConcurrencyException if someone saved since command.Version
```

When two edits race, the second throws and writes nothing — [the edit form above](#a-create-and-an-edit-form)
catches it with the reader's changes still on screen. A delete pins the version the same way before
`db.Remove(product)`, so it loses to an edit it has not seen.

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

## Choosing the database

An app picks its database in configuration, not in code. `Rask:Database:Provider` names it — `sqlite` (the
default), `postgres` or `sqlserver` — and `Rask:ConnectionStrings:App` says where it is:

```jsonc
{
  "Rask": {
    "Database": { "Provider": "postgres" },
    "ConnectionStrings": { "App": "Host=db;Database=shop;Username=shop;Password=…" }
  }
}
```

`RaskApp` reads it for you. An app with a context of its own registers it with `UseRaskDatabase(sp)`, which opens
whichever provider the setting names:

```csharp
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskDatabase(sp)
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

Each provider still tunes itself from its own section — `Rask:Sqlite`, `Rask:Postgres`, `Rask:SqlServer` — so
`UseRaskDatabase` takes no options. Call `UseRaskPostgres(sp, o => …)` directly when a callback has to set something
configuration cannot.

What changes with the database, in a `RaskApp`:

| | SQLite | PostgreSQL or SQL Server |
| --- | --- | --- |
| No `Rask:ConnectionStrings:App` | falls back to `app.db` | fails, naming the key |
| The durable log | `logs.db`, a file of its own | the `RaskLog` table in the app database |
| Snapshots | on by default | left out; configuring them refuses the start |
| Litestream | on when `Rask:Litestream:ReplicaUrl` is set | setting it refuses the start |

An app whose own context opens a different database than the setting names — a `Program.cs` still calling
`UseRaskSqlite(sp)` after the setting moved to `postgres` — fails at start, naming the call to use. Migrations are
provider-specific, so moving an existing app means generating its migrations against the new provider.

Moving an existing app is more than the setting:

- **A `RaskApp` whose `appsettings.json` has a `Rask:Snapshots` section** refuses to start on a server database — that
  is a backup the app asked for and cannot have. Delete the section, or turn the battery off with
  `app.Configure(c => c.Snapshots.Off())`. Delete `Rask:Litestream` too if it names a replica.
- **A context of your own** follows the setting through `UseRaskDatabase(sp)`, and on a server database it also maps
  the log table — `modelBuilder.AddRaskLogging()` — because that is where `RaskApp` keeps the log. A context that
  does not is told the line at start.
- **An app scaffolded by `rask new`** wires each battery by hand in `Program.cs`, references the packages one by
  one rather than `Rask`, and does not read the setting yet. Switch it there: reference `Rask.Postgres` or
  `Rask.SqlServer`, and `UseRaskSqlite(sp)` becomes `UseRaskPostgres(sp)` or `UseRaskSqlServer(sp)`.
  `AddRaskLogging()` becomes `AddRaskLogging<AppDbContext>()`, with the model line above. The snapshot and
  Litestream lines go.
- **`rask deploy` does not know about providers yet.** It points `Rask:ConnectionStrings:App` at a SQLite file on its
  volume, so deploy a server-database app another way for now.

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
