# Rask.Data — models, queries, and the database you don't write

> **In practice:** [Tutorial Ch 2](tutorial/02-first-feature.md) · recipe [add a feature to an existing database](recipes.md#add-a-feature-to-an-existing-database) · [cheat sheet](cheatsheet.md).

`Rask.Data` is a layer over **Entity Framework Core** with one goal: **you declare models, and that is
all**. No `DbContext` class, no `DbSet` property, no `IEntityTypeConfiguration`, no registration — and
no `IDbContextFactory` injected into every page that reads a row or saves a form.

Underneath it is ordinary EF Core, and nothing is hidden from you. Work richer than a read or a form's
worth of write — a domain operation, a transaction across two aggregates — is EF Core exactly as you know
it ([below](#domain-operations-and-transactions-plain-ef-core)), and an app that outgrows the conventions
writes its own context and Rask steps aside ([below](#using-ef-core-the-usual-way)).

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
}
```

That compiles into a mapped table, and into everything a screen needs to use it:

```csharp
var cheap = await Product.Where(p => p.Price < 10).OrderBy(p => p.Name).ToListAsync();

var anvil = await Product.CreateAsync(new ProductModel { Name = "Anvil", Price = 9.99m });

var edit = anvil.ToModel();          // a mutable copy, for a form
edit.Price = 12.50m;
await Product.UpdateAsync(anvil.Id, edit);   // writes Price; throws if someone saved since

await Product.DeleteAsync(anvil.Id);
```

A source generator finds every `Model` at build time, hands it to `RaskAppDbContext`, and writes a
`ProductModel` beside it with the writes that take one; the host points the model surface at the
database. There is nothing else to write and nothing to register.

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
Optimistic concurrency exists to round-trip the token through an edit form — `ProductModel` carries it
there and back — and a value the application cannot read is one it cannot send back. A model that marks
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
and expecting a save does nothing. A change goes back through [the generated
writes](#writing-the-generated-model), or through [a context you
inject](#domain-operations-and-transactions-plain-ef-core) when it is a domain operation.

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

## Writing: the generated model

A form edits something mutable, and a well-kept entity is not: its setters are private so that it only
changes through its own rules. So for every model the build generates a companion that *is* mutable —
`ProductModel` — and the writes that take it. For the `Product` above:

```csharp
// generated, in Product's namespace (abridged)
public sealed partial class ProductModel
{
    [Required, MaxLength(200)]
    public string Name { get; set; }

    public decimal Price { get; set; }

    public int Version { get; set; }
}
```

| Generated | What it does |
| --- | --- |
| `ProductModel` | A settable copy of every mapped property except the key. `Version` is in it; `Id`, `CreatedAt`, `UpdatedAt`, `DeletedAt` and navigations are not. DataAnnotations attributes are copied, so a form bound to it validates by the entity's own rules. |
| `Product.CreateAsync(model, ct)` | Constructs a `Product` from the model and inserts it. A `Guid` key is assigned (`Guid.CreateVersion7()`); an integer key comes from the database. Returns the entity. |
| `Product.CreateAsync(id, model, ct)` | The same, with the key you give — an imported id, one a client chose. The only create generated for a key Rask cannot produce, such as a strongly-typed id over an `int`. |
| `Product.UpdateAsync(id, model, ct)` | Loads the row with `id`, applies the values and saves — only the columns whose values changed are written. Returns the entity. |
| `Product.DeleteAsync(id, version, ct)` | Loads the row and deletes it. `version` is optional. |
| `product.ToModel()` | The entity's current values as a `ProductModel`, for an edit form. |

Each write opens a context, makes its one change **through the change tracker**, saves and disposes. So
the interceptors see it exactly as they see any other save: `CreatedAt`/`UpdatedAt` are stamped,
`Version` is bumped, an `ISoftDeletable` is stamped rather than removed, and the entity's domain events
are published after the commit — the difference from [`ExecuteDeleteAsync`](#batch-update-and-delete),
which the interceptors never see.

**The entity needs no ceremony for this.** A private parameterless constructor and private setters are
fine, and the class does not have to be `partial`: the writes are generated as static extension members,
the same way the reads are, so `Product` stays closed to everyone but its own methods. They live in
`Product`'s namespace, so code that can name `Product` has them.

### Keeping state inside the entity

Two build warnings hold an entity to that shape, because a generated model is only a safe way to edit an
entity whose own members cannot be written from outside it:

- **No public setters** ([RASK080](diagnostics.md#rask080)). A property of a `Model` — or of an abstract
  base the app puts between `Model` and its entities, or of an `IValueObject` — may not have a public `set`
  or `init`, and a public field must be `readonly`. State changes through the type's own methods and
  constructor; the lightbulb makes the accessor `private`. A positional record's parameters are exempt, so
  `record Money(decimal Amount, string Currency) : IValueObject` stays the idiomatic value object.
- **No mutable collections of entities** ([RASK081](diagnostics.md#rask081)). A navigation to many
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

### What each write promises

- **`CreateAsync` inserts, and the entity owns its key.** `CreateAsync(model)` assigns a `Guid` key itself
  — `Guid.CreateVersion7()`, unless the constructor already set one — and leaves an integer key to the
  database; `CreateAsync(id, model)` uses yours. Either way the returned entity carries it. A key that is
  not an integer is never generated by EF Core, which is what lets a child entity with an id of its own be
  added to a loaded aggregate and saved as an insert — so a factory that forgets its id is refused at the
  save, by name, rather than inserting an empty key. A key you configured yourself in a context of your own
  (`ValueGeneratedOnAdd()`, a database default) is left as you set it.
- **`UpdateAsync` takes the id from you, not from the model.** The row it writes is the one its `id`
  argument names — a route value the page already has, or one a handler has checked the caller may edit —
  so a model that arrives over a wire cannot pick the row it lands on. No row with that id — never
  created, or soft-deleted since the form was loaded — is a `KeyNotFoundException`. For an `IVersioned` model it also checks
  `model.Version` against the row: someone saved in between, and it throws
  `DbUpdateConcurrencyException` and writes nothing.
- **`DeleteAsync` takes the version when you have one.** `Product.DeleteAsync(id, version)` is
  concurrency-checked like an update; `Product.DeleteAsync(id)` deletes whatever the current version is.
  A model that is not `IVersioned` gets `DeleteAsync(id)` alone. A missing or already-deleted row is a
  `KeyNotFoundException`.

### Overriding a write

A write the entity declares itself is the one every call site gets: `Product.CreateAsync(model)` binds to
a static member on `Product` before it considers the generated one. So to change what creating a product
means, declare it, with the generated signature:

```csharp
public sealed class Product : Model<Guid>
{
    public static Task<Product> CreateAsync(ProductModel model, CancellationToken cancellationToken = default)
    {
        model.Name = model.Name.Trim();
        return ProductModelExtensions.CreateAsync(model, cancellationToken);   // the generated write
    }
}
```

The generated write stays reachable on `ProductModelExtensions` for an override that only adds to it; one
that replaces it simply does not call it. The writes you do not declare stay generated. A rule about the
entity's own state — raising a domain event, refusing a transition — is a domain method saved through
[plain EF Core](#domain-operations-and-transactions-plain-ef-core), not an override.

### A create and an edit form

The generated model is what `Form.Model(…)` binds. A create page starts from an empty one:

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
        await Product.CreateAsync(product, CancellationToken);
        nav.NavigateTo(Routes.ProductsPage());
    }
}
```

An edit page starts from the row, through `ToModel()`:

```csharp
[Route("/products/{id:guid}/edit")]
public sealed partial class EditProductPage(Navigator nav) : Component
{
    [RouteParam] public Guid Id { get; set; }

    private ProductModel? _product;
    private string? _conflict;

    protected override async Task OnMountAsync() =>
        _product = (await Product.FindAsync(Id, CancellationToken))?.ToModel();

    protected override Component? Render() =>
        _product is null ? P["Loading…"] :
        Form.Model(_product).OnValidSubmit(SaveAsync)[
            _conflict is null ? null : UiAlert.Tone(UiTone.Warning)[_conflict],
            UiInput.Bind(() => _product.Name).Label("Name"),
            UiInput.Bind(() => _product.Price).Label("Price"),
            UiButton.Type(UiButtonType.Submit)["Save"],
        ];

    private async Task SaveAsync(ProductModel product)
    {
        try
        {
            await Product.UpdateAsync(Id, product, CancellationToken);
            nav.NavigateTo(Routes.ProductsPage());
        }
        catch (DbUpdateConcurrencyException)
        {
            _conflict = "Someone saved this product while you were editing it.";
        }
    }
}
```

The `[Required, MaxLength(200)]` on `Name` is the entity's, copied to the model, so both forms refuse an
empty or overlong name before either write runs — and EF Core reads the same attributes for the column.
The version needs no input of its own: the form edits the very `ProductModel` that `ToModel()` returned,
so `Version` comes back with the submit and a lost race lands in the `catch`, with the reader's edits still
on screen.

### What the model leaves out

**A property the form should not carry** — a status only `Ship()` moves, a total the entity computes —
is marked `[SkipModel]` (from `Rask.Data`). It is left out of `ProductModel`, so `CreateAsync` leaves it
at whatever the constructor gives it and `UpdateAsync` never writes it:

```csharp
public sealed class Order : Model<Guid>
{
    public string Reference { get; private set; } = "";

    [SkipModel]
    public OrderStatus Status { get; private set; }
}
```

**A value object becomes a nested generated model.** `Money Total` on `Order` is `MoneyModel Total` on
`OrderModel`, so a form binds `() => _order.Total.Amount` like any [nested model](forms-advanced.md).

**The build says when it cannot generate.** Each of these is a build diagnostic rather than a surprise
at the first save:

- A model with **no parameterless constructor** is a warning, and it gets no `CreateAsync(model)` — there
  is nothing to construct the entity through ([RASK077](diagnostics.md#rask077)). A private one is enough.
- A **hand-written `ProductModel`** beside a `Product` entity is an error: the generated class would
  collide with yours ([RASK078](diagnostics.md#rask078)). Rename yours — or declare it `partial`, which
  merges it into the generated one and is the way to add members, `IValidatableObject` or display helpers.
- A **nested entity** — a model declared inside another class — is a warning, and it gets no model
  ([RASK079](diagnostics.md#rask079)).

`[SkipModel]` on the **class** generates no model at all, which silences the last two when that is the
intent. And an entity deriving from the non-generic `Model` (a composite key) has no id to address a row
by, so it gets `ProductModel`, `ToModel()` and `CreateAsync` but no `UpdateAsync` or `DeleteAsync` — write
those through an injected context.

**A model crosses a wire like any other type.** A CQRS command or query, an API endpoint or an island prop
can carry a `ProductModel` (or return one), and the generators that build those codecs recognise it even
though it is itself generated. On the wire its properties are **camelCase**, and it carries no id: the handler that calls `UpdateAsync`
passes one it has authorized, so a posted model cannot pick the row it lands on. Only validation attributes are
copied from the entity, so a `[JsonPropertyName]` on the entity does not rename the model's property; a
property you declare yourself on a `partial ProductModel` keeps its own pin.

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

**A batch soft delete is an update, not `ExecuteDeleteAsync`.** `Product.DeleteAsync(id)` on an
`ISoftDeletable` stamps `DeletedAt`, but `ExecuteDeleteAsync` is a `DELETE` the interceptors never see —
the rows are gone, not hidden. Stamp them instead:

```csharp
await Product.Where(p => p.Discontinued)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.DeletedAt, DateTime.UtcNow));
```

The rule of thumb: reach for these when the work is a statement the database can do on its own, and
load-then-save when the conventions and the domain events are the point.

## Domain operations and transactions: plain EF Core

The generated writes cover a form's worth of change: one row, the values somebody typed. Behaviour is
different — `order.Cancel()` decides something, raises an event and changes what it must — and so is work
that has to land together, such as placing an order and reserving its stock. **That is ordinary EF Core,
and Rask adds nothing to learn:** load the entity from a context, call the method, save.

Where the context comes from depends on how long the caller lives.

**On a live page, inject the factory and make a context per operation.** A page outlives any DI scope —
it lives as long as the browser keeps its socket open — so a context injected into it would be shared by
every handler for the whole session. `IDbContextFactory<RaskAppDbContext>` is registered for you:

```csharp
[Route("/orders/{id:guid}")]
public sealed partial class OrderPage(IDbContextFactory<RaskAppDbContext> contexts) : Component
{
    [RouteParam] public Guid Id { get; set; }

    private async Task CancelAsync()
    {
        await using var db = await contexts.CreateDbContextAsync(CancellationToken);

        var order = await db.Set<Order>().FirstAsync(o => o.Id == Id, CancellationToken);
        order.Cancel(DateTime.UtcNow);                   // the decision, and the event it raises
        await db.SaveChangesAsync(CancellationToken);    // stamped, versioned, published
    }
}
```

**In a CQRS handler or an endpoint, inject the context itself.** Those run inside a DI scope that ends
with the request, which is exactly the lifetime a `DbContext` wants:

```csharp
public sealed class PlaceOrderHandler(RaskAppDbContext db) : ICommandHandler<PlaceOrder, Guid>
{
    public async Task<Guid> HandleAsync(PlaceOrder command, CancellationToken ct)
    {
        var stock = await db.Set<StockItem>().FirstAsync(s => s.Sku == command.Sku, ct);
        stock.Reserve(command.Quantity);

        var order = Order.Place(command.Sku, command.Quantity);
        db.Add(order);

        await db.SaveChangesAsync(ct);   // both rows in one transaction, or neither
        return order.Id;
    }
}
```

`RaskAppDbContext` (namespace `Rask`) is the context the host builds: every model you declared, plus
every battery's tables. It has no `DbSet` properties, so an entity is reached with `db.Set<Order>()`. An
app that [registered its own context](#using-ef-core-the-usual-way) injects that one the same way, and the
model surface reads from it too — which includes every app `rask new` scaffolds: it writes
`Features/Shared/AppDbContext.cs`, so there a page takes `IDbContextFactory<AppDbContext>` and a handler
takes `AppDbContext`.

Every convention still holds, because this is the same change tracker the generated writes use: the save
stamps `UpdatedAt`, bumps `Version`, turns a `Remove` of an `ISoftDeletable` into a `DeletedAt` stamp, and
publishes the domain events after the commit. The one thing to remember is the one from
[Reading](#reading-the-model-type-is-its-own-query): rows from `Product.Where(…)` are untracked, so load
the entity you are about to change from the context that is going to save it.

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

Behaviour that touches the database gets a real one in a line, rather than a mocked `DbContext`. Seed
through `database.Context`, then exercise the model surface exactly as the app does:

```csharp
await using var database = await TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={path}"));

var anvil = await Product.CreateAsync(new ProductModel { Name = "Anvil", Price = 9.99m });

var mine = anvil.ToModel();
var theirs = anvil.ToModel();
theirs.Price = 10m;
await Product.UpdateAsync(theirs);

mine.Price = 11m;
await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Product.UpdateAsync(mine));
```

```csharp
database.Context.AddRange(Order.Place("B-2"), Order.Place("B-3"));
await database.Context.SaveChangesAsync();

Assert.Equal(2, await Order.CountAsync());
```

`TestDatabase.StartAsync` builds the generated model — so no fixture has to list entities — creates the
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
EF Core and the generated model need:

```csharp
// holds only scalars — a positional record is fine
public sealed record Money(decimal Amount, string Currency) : IValueObject;

// contains a value object — needs a parameterless ctor, and its setters stay private (RASK080)
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

A strongly-typed id over a `Guid` is created like a `Guid` key: `CreateAsync(model)` assigns one. Over any
other value — `record struct OrderId(int Value)` — there is nothing to generate it from, for Rask or for EF
Core through a converter, so only `Order.CreateAsync(id, model)` is generated: the id is always yours to give.

## Using EF Core the usual way

None of the above is compulsory, and opting out is not deriving from `Model`. A class that does not
derive from it is an ordinary EF Core entity: write your own `DbContext`, your own
`IEntityTypeConfiguration`, your own `DbSet` properties, and use them exactly as you do today.

Registering an `IDbContextFactory<YourContext>` is the whole of opting out at the app level. Rask binds
the model surface and every database-backed battery to the context you registered, and
`RaskAppDbContext` is never constructed. Call `modelBuilder.ApplyRaskConventions()` from its
`OnModelCreating` to keep the soft-delete filters and concurrency tokens, or
`ModelRegistry.Apply(modelBuilder)` to keep the generated model as well.

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
`Product.Where(…)` or `Product.CreateAsync(…)` throws saying the type is not part of the model. If you
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

`IVersioned` makes `Version` an EF Core concurrency token, and the generated model carries it through an
edit: `ToModel()` copies the version the screen read, and `UpdateAsync` checks the row still has it. When
two edits race, the second throws `DbUpdateConcurrencyException` and writes nothing — [the edit form
above](#a-create-and-an-edit-form) catches it with the reader's changes still on screen.
`Product.DeleteAsync(id, version)` takes the same token, for a delete that should lose to an edit it has
not seen.

In a handler that loads and saves through the context itself, the check is EF Core's own. A freshly loaded
row's original `Version` is whatever the database holds *now*, which would make every check pass — so pin
the version the caller read as the tracked original value before saving:

```csharp
var product = await db.Set<Product>().FirstAsync(p => p.Id == command.Id, ct);
db.Entry(product).Property(p => p.Version).OriginalValue = command.Version;
product.Reprice(command.Price);
await db.SaveChangesAsync(ct); // throws if someone else changed it since `command.Version`
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
