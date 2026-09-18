# Rask.Data — aggregates, queries, and the database you don't write

> **In practice:** [Tutorial Ch 2](tutorial/02-first-feature.md) · recipe [add a feature to an existing database](recipes.md#add-a-feature-to-an-existing-database) · [cheat sheet](cheatsheet.md).

`Rask.Data` is a layer over **Entity Framework Core** with one goal: **you declare aggregates, and that is
all**. No `DbContext` class, no `DbSet` property, no `IEntityTypeConfiguration`, no registration, and
no `IDbContextFactory` injected into every page that reads a row.

Underneath it is ordinary EF Core, and nothing is hidden from you. **The aggregate type writes**:
`Product.CreateAsync(model)`, `Product.UpdateAsync(id, model)`, `Product.DeleteAsync(id)`
([below](#writing-create-update-delete)). **Its read face queries**: `Product.Read.Where(…)`
([below](#reading-the-read-face)). Anything richer is EF Core exactly as you know it, a domain method
saved through a context ([below](#writing-plain-ef-core)), and an app that outgrows the conventions writes its
own context and Rask steps aside ([below](#using-ef-core-the-usual-way)).

**Why two faces and not one.** An aggregate is a consistency boundary, and it holds another aggregate's *id*
and never a navigation to it — that is what stops a write crossing a boundary by accident, and
[RASK087](diagnostics.md#rask087) enforces it. A query that reached across a boundary from the write side
would be that border failing. So reading moves to a generated **read face** — primitives, no behaviour, not in
the write context — which carries the navigations the aggregate is not allowed to have. **The border is on
the write side only; the read side has none.**

> Included in the [`Rask`](../README.md) package, so there is nothing to install. It is **on**; an app that does without it says so:
>
> ```csharp
> app.Configure(c => c.Data.Off());
> ```

## The whole of it

```csharp
public sealed record Money(decimal Amount, string Currency);   // a value object: no marker

public sealed class Product : Aggregate<Guid>
{
    [Required, MaxLength(200)]
    public string Name   { get; private set; } = "";
    public Money Price   { get; private set; } = new(0m, "EUR");
    public string? Notes { get; private set; }

    public static Product Create(string name, Money price) =>
        new() { Id = Guid.CreateVersion7(), Name = name, Price = price };

    public void Reprice(Money price)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(price.Amount);
        Price = price;
    }
}
```

That compiles into a mapped table, a generated `ProductModel` [for its forms](#a-create-and-an-edit-form),
a `db.Products` accessor on any `DbContext`, and a read face — `ProductRead`, reached as `Product.Read` —
which is everything a screen needs:

```csharp
var cheap = await Product.Read.Where(p => p.PriceAmount < 10).OrderBy(p => p.Name).ToListAsync();
var anvil = await Product.Read.Where(p => p.Id == id).FirstOrDefaultAsync();
var grid  = Product.Read.OrderBy(p => p.Name).AsQueryable();   // for UiDataGrid, sorted and paged in SQL
```

Note `p.PriceAmount`: the read face is **primitives**, so the `Money Price` value object arrives as the two
columns it is stored in. That is the whole of the translation — [the read face](#reading-the-read-face) has
the rules.

And to write it, from a form, from code, or inside a transaction you already hold:

```csharp
var product = await Product.CreateAsync(model);                                // ProductModel from a form
await Product.UpdateAsync(product.Id, edit);                                   // stale Version throws

var other = await Product.CreateAsync(p => p.Reprice(new(9.90m, "EUR")));      // no form at all …
await Product.UpdateAsync(other.Id, p => p.Reprice(new(12.50m, "EUR")));       // … and the same shape to change it

await Product.DeleteAsync(product.Id);                                         // a DeletedAt stamp
```

A source generator finds every aggregate at build time and hands it to `RaskAppDbContext`; the host points
the model surface at the database. There is nothing else to write and nothing to register.

**Generated, never reflected.** No assembly is scanned and no method is found by name, so a trimmed
publish cannot quietly drop an aggregate and leave you a missing table with a green build.

### Aggregates, entities and value objects

Rask.Data speaks domain-driven design, and the base class you derive from says which of the three a type is:

| You declare | What it is | What it gets |
| --- | --- | --- |
| `Aggregate<TId>` | A consistency boundary: the thing you load, change and save as one | Its table, the static reads and writes, a generated form model, `Version`, soft delete, domain events |
| `Entity<TId>` | Something with identity inside an aggregate, such as an order's line | Its table, its timestamps, and a form model its parent's carries ([below](#children)); it is loaded, saved and deleted with its aggregate |
| anything else it holds | A value object: `Money`, `Address`, `Email`. No base class, no marker | Columns on the owner's row ([below](#value-objects)) |

`Aggregate<TId>` derives from `Entity<TId>`, and the framework's columns come with the base class. They are
real properties with private setters, so a screen can show them and a query can sort by them, while only the
framework writes them:

| Property | Declared on | Effect |
|-----------|--------|--------|
| `Id` | `Entity<TId>` | The key. A `Guid` or strongly-typed id is assigned in the factory; an integer is the store's identity. |
| `CreatedAt`, `UpdatedAt` | `Entity<TId>` | Stamped (UTC) on insert and on every update. |
| `Version` | `Aggregate<TId>` | The optimistic-concurrency token, bumped on every update. |
| `DeletedAt` | `Aggregate<TId>` | A delete becomes a `DeletedAt` stamp, and a global query filter hides the row. |

```csharp
var recent = await Product.OrderByDescending(p => p.CreatedAt).Take(10).ToListAsync();
```

`Aggregate<TId>` also carries the domain-events buffer: `Raise(…)` inside a method, and the events are
published after the save commits ([below](#what-the-interceptors-do)).

**Aggregates declare no constructor.** The implicit parameterless one is what EF Core materialises rows
through, what `Product.CreateAsync(model)` starts from, and what gives `new ProductModel()` the aggregate's
defaults. Domain creation goes in a static factory, as `Product.Create` does above; a constructor that takes
arguments turns off the generated creates ([RASK086](diagnostics.md#rask086)).

**State changes through the type's own methods.** A public `set`, a hand-written public `init` or a public
mutable field on an aggregate, an entity or a value object one of them holds is a build error
([RASK084](diagnostics.md#rask084)). EF Core and the generated writes both work through private setters, so
nothing needs a public one.

### Children

An aggregate holds its parts. Declare them as `Entity<TId>`, keep them in a field, and hand out a read-only
view — nothing else is configured, and no `HasMany`, no foreign key and no key generation is written anywhere:

```csharp
public sealed class Order : Aggregate<Guid>
{
    private readonly List<OrderLine> _lines = [];

    private Order() { }                                  // EF materialization

    public string Reference { get; private set; } = "";

    public IReadOnlyCollection<OrderLine> Lines => _lines;

    public static Order Place(string reference) =>
        new() { Id = Guid.CreateVersion7(), Reference = reference };

    public OrderLine Add(string product, int quantity)   // the aggregate guards its own invariants
    {
        var line = OrderLine.For(product, quantity);
        _lines.Add(line);
        return line;
    }
}

public sealed class OrderLine : Entity<Guid>
{
    private OrderLine() { }

    public string Product { get; private set; } = "";

    public int Quantity { get; private set; }

    public void SetQuantity(int quantity) => Quantity = quantity;

    internal static OrderLine For(string product, int quantity) =>
        new() { Id = Guid.CreateVersion7(), Product = product, Quantity = quantity };
}
```

**Loading one root loads it whole.** Every write that starts from an id brings the children with it, and so
does `db.Orders.FindAsync(id)`. On the read face the children are an ordinary navigation, so you ask for them
when you want them — listing a thousand orders should not drag in every line each of them holds:

```csharp
await Order.UpdateAsync(id, o => o.Add("anvil", 1));                 // loaded whole, changed, saved

var open = await Order.Read.Where(o => o.Reference.StartsWith("2026")).ToListAsync();   // Lines EMPTY
var withLines = await Order.Read.QueryAsync((q, ct) => q.Include(o => o.Lines).ToListAsync(ct));

// …and a child is queryable on its own, because the read side has no borders:
var heavy = await OrderLine.Read.Where(l => l.Quantity > 10 && l.Order.Reference.StartsWith("2026"))
                               .ToListAsync();
```

**A change to any part is a change to the whole.** A line's quantity moving stamps the order's `UpdatedAt` and
bumps its `Version`, so the version a caller read stops being current — which is what makes the aggregate, and
not the row, the unit of concurrency:

```csharp
await Order.UpdateAsync(id, o => o.Lines.First().SetQuantity(5));
// UPDATE OrderLine SET Quantity = 5 …
// UPDATE Order     SET Version = Version + 1, UpdatedAt = @now WHERE Id = @id AND Version = @read
```

**The form model carries them, and a save syncs them.** `OrderModel` gets a `List<OrderLineModel>`, and each
child model carries an `Id` so a save knows which stored line each row is:

```csharp
var model = order.ToModel();

model.Lines.Single(l => l.Product == "anvil").Quantity = 9;    // edits that line
model.Lines.Add(new OrderLineModel { Product = "rope", Quantity = 2 });   // no Id: a new line
model.Lines.RemoveAll(l => l.Product == "spring");             // not posted: that line is DELETED

await Order.UpdateAsync(id, model);
```

> **What the posted list holds is what the aggregate holds afterwards.** A row with no `Id` is added, a row
> whose `Id` matches a stored child updates it, and **a stored child whose id is in none of the posted rows is
> removed** — so a form that renders only some of the lines deletes the rest, and an empty list deletes them
> all. Bind the whole collection, or apply the change through a domain method (`Order.UpdateAsync(id, o =>
> o.Add(…))`) instead of a partial model.

An `Id` that matches nothing in *this* aggregate is never followed: it lands as a new child with an id of its
own. A posted id therefore cannot reach — or delete — another aggregate's line.

**A child cannot outlive its parent.** Rask makes the relationship required and its delete a cascade, so taking a
line out of the collection deletes the row. Left to EF Core's own convention the foreign key would be nullable,
and severing a child would set that key to `NULL` and leave the row in the table — invisible through the
navigation, unreachable through the aggregate, and impossible to delete through it either.

Soft-deleting the aggregate is different, and deliberately so: `Order.DeleteAsync(id)` stamps `DeletedAt` rather
than removing the row, so nothing cascades and the lines are still there if the order comes back.

The child gets a table, `CreatedAt` and `UpdatedAt`, a model so its parent's form can carry it, and a read
face of its own — `OrderLine.Read` — because the read side has no borders. It gets no **writes** of its own:
there is no `OrderLine.CreateAsync`, no version and no soft delete, because it is not a thing you save on its
own. A collection of another **aggregate** is not a child at all ([RASK087](diagnostics.md#rask087)), and a
collection Rask cannot write is [RASK088](diagnostics.md#rask088).

## Reading: the read face

An aggregate is not a query surface. Querying goes through its generated **read face** — `Product.Read`,
which returns rows of `ProductRead`:

```csharp
await Product.Read.ToListAsync();
await Product.Read.Where(p => p.Active).OrderBy(p => p.Name).ToListAsync();
await Product.Read.Where(p => p.PriceAmount > 10).OrderByDescending(p => p.PriceAmount).Skip(20).Take(20).ToListAsync();
await Product.Read.OrderBy(p => p.Name).Select(p => p.Name).ToListAsync();   // reads one column
await Product.Read.IgnoreQueryFilters().ToListAsync();                       // soft-deleted rows too
await Product.Read.Where(p => p.Id == id).FirstOrDefaultAsync();             // by id
await Product.Read.FirstOrDefaultAsync(p => p.Name == "Anvil");
await Product.Read.CountAsync(p => p.Active);
await Product.Read.AnyAsync();
await Product.Read.Search("red anvil").Take(20).ToListAsync();               // full-text, best match first
await foreach (var p in Product.Read.AsAsyncEnumerable()) { }
```

`Read` is a C# 14 static extension member, which is why nothing has to be declared or derived from a second
base. A member declared on the aggregate itself always wins, so your own `Read` would be untouched.

### What the read face is

One is generated for **every mapped entity**, children included — the read side has no borders, so a part is
queryable on its own even though it is only writable through its root.

**A read face is generated into the assembly that DECLARES the aggregate**, the same as the form model and
the writes. Your own aggregates have one, and so do the aggregates a Rask package declares —
`Session.Read` and `Passkey.Read` are [Rask.Auth's](authentication.md). A package can ask for the read faces
*alone* with `<RaskReadFacesOnly>true</RaskReadFacesOnly>`, which is what those two do: they are mapped by
`modelBuilder.AddRaskAuth()` rather than by the registry, and nothing should be able to create a passkey
from a form.

| On the aggregate | On the read face |
|---|---|
| `string Name` | `string Name` — unchanged |
| `Money Price` (value object) | `decimal PriceAmount`, `string PriceCurrency` — **flattened to its columns** |
| `OrderStatus Status` (enum) | `OrderStatus Status` — one column, unchanged |
| `Guid CustomerId` | `Guid CustomerId`, **and** `CustomerRead Customer` — the navigation, inferred |
| `IReadOnlyCollection<OrderLine> Lines` | `IReadOnlyList<OrderLineRead> Lines` |
| `bool IsShipped => …` (computed) | *dropped* — no column behind it |
| `[NotMapped] string Display` | *dropped* |
| every method, every domain event | *dropped* — there is nothing here to change or save |

**Member names are C#-shaped; the columns are untouched.** `PriceAmount` maps to the existing `Price_Amount`
column, so no migration is involved in any of this.

**The navigations are inferred from the ids**, which is what gives the read side its freedom: you write the
DDD-correct id on the aggregate and the join appears on the read face, having declared nothing.

| Property on the aggregate | Target | Navigation |
|---|---|---|
| `Guid CustomerId` + a `Customer : Aggregate<Guid>` | exact name match | `Customer` |
| `Guid? ShippedByUserId` + a `User : Aggregate<Guid>` | name *ends with* an aggregate's name | `ShippedByUser` (nullable) |
| `Guid CustomerId`, no `Customer` aggregate | none | none — an ordinary `Guid` column |
| `int CustomerId` + a `Customer : Aggregate<Guid>` | name matches, key type does not | none, **and [RASK089](diagnostics.md#rask089)** |
| `Guid CustomerId` where two aggregates end in `Customer` | ambiguous | none, **and [RASK089](diagnostics.md#rask089)** |

The navigation is named after the **property**, not the target, so two references to the same aggregate never
collide. And it is the one thing the write model cannot do, so this is the join you came for:

```csharp
await Order.Read
    .Where(o => o.Customer.Country == "HU"
             && o.ShippedByUser!.Name == "ada"
             && o.TotalAmount > 100
             && o.Lines.Any(l => l.Product.Sku == "ANVIL"))
    .OrderByDescending(o => o.ShippedAt)
    .Select(o => new { o.Reference, Customer = o.Customer.Name, o.TotalAmount })
    .ToListAsync();
```

Four aggregates in one statement, from a write model that holds nothing but ids.

### There is no `Read.FindAsync`

By id is the narrowest query, and it is spelled as one:

```csharp
var product = await Product.Read.Where(p => p.Id == id).FirstOrDefaultAsync();
```

Deliberately, there is no `FindAsync` beside it. EF Core's `Find` **bypasses query filters**, so it would
return a soft-deleted row that `Where` hides — two spellings of one read, disagreeing about deleted rows. One
spelling that is always right beats two that are usually the same.

**Every read is untracked, and every read opens and disposes its own context.** Composing holds nothing
open: the terminal call opens a context, runs, and disposes it before it returns — so this is a complete
statement anywhere, including a component's `OnMountAsync`:

```csharp
protected override async Task OnMountAsync() =>
    _products = await Product.Read.Where(p => p.Active).OrderBy(p => p.Name).ToListAsync(CancellationToken);
```

That is the shape a live page needs, not a default to tune. A Rask page lives as long as the browser
keeps its socket open, and a `DbContext` is neither thread-safe nor meant to accumulate a session's worth
of entities — so nothing here holds one between calls, and there is no `AsTracking()` to ask for one.
Most reads are rendered and never written back anyway, and tracking them would cost a graph walk and an
identity-map entry to buy nothing.

**The consequence is now in the type system.** A `ProductRead` has no behaviour and is not in the write
context, so there is nothing on it to change and nothing that could save it. A change goes back through
[a write on the type](#writing-create-update-delete) or [a context](#writing-plain-ef-core), and both load
the entity they are about to change.

For a shape this does not wrap — a group-by, a join, an aggregate — `QueryAsync` hands you the live
`IQueryable` inside a managed context:

```csharp
var byMonth = await Product.Read.QueryAsync((q, ct) =>
    q.GroupBy(p => p.CreatedAt.Month)
     .Select(g => new { Month = g.Key, Total = g.Sum(p => p.PriceAmount) })
     .ToListAsync(ct));
```

### Handing a query to a component: `AsQueryable()`

A data grid composes its own LINQ — it sorts with `OrderBy`, pages with `Skip`/`Take`, counts with
`Count()` — so what it wants is a standard `IQueryable<T>`, not a query somebody has to run.
`Product.Read.AsQueryable()` is that, and it holds no context either: **each time it is executed, a context is
opened for that one execution and disposed after it.** So it is safe to keep in a field for as long as
the page lives:

```csharp
[Route("/products")]
public sealed partial class ProductsPage : Component
{
    private readonly IQueryable<ProductRead> _products = Product.Read.Where(p => p.PriceAmount > 0).AsQueryable();

    protected override Component Render() =>
        UiDataGrid.Data(_products).RowKey(p => p.Id).PageSize(25)[c => [
            c.Field(p => p.Name).Title("Product").Sortable(true),
            c.Field(p => p.PriceAmount).Title("Price").Sortable(true),
        ]];
}
```

A grid spanning two aggregates is possible for the first time here, because the read face carries the
navigation: `c.Field(o => o.Customer.Name)` needs nothing declared.

The grid's sort becomes `ORDER BY` and its page becomes `LIMIT`/`OFFSET`, in the database — the table
never reaches memory, however large it is. A synchronous `Count()` and an awaited `ToListAsync()` both
work against it; each is its own short-lived context. See [the data grid](data-grid.md) for the rest of
what the grid does with a query.

**EF Core's own operators go on first.** `Include`, `IgnoreQueryFilters` and `AsSplitQuery` are EF Core
extension methods, and EF applies them only to its own query provider — called on the queryable
`AsQueryable()` returns, they do nothing, silently. Put them on the model query, before the hand-off:

```csharp
Product.Read.Include(p => p.Reviews).AsQueryable();        // ✓ travels with the queryable
Product.Read.IgnoreQueryFilters().AsQueryable();           // ✓ soft-deleted rows too

Product.Read.AsQueryable().Include(p => p.Reviews);        // ✗ compiles, loads no reviews
```

## Writing: create, update, delete

The writes live on the aggregate type, beside the reads, and a create reads like the update it pairs with. The
update just names the row first:

| Create | Update | From |
| --- | --- | --- |
| `Product.CreateAsync(model)` | `Product.UpdateAsync(id, model)` | a form: the generated `ProductModel` |
| `Product.CreateAsync(model, p => …)` | `Product.UpdateAsync(id, model, p => …)` | a form, plus values it does not carry |
| `Product.CreateAsync(p => …)` | `Product.UpdateAsync(id, p => …)` | code, with no form behind it |

- **A create** builds an empty `Product`, applies the model and then the lambda, and inserts it. The key is a new
  version-7 `Guid` (or the store's identity for an integer key).
- **`Product.CreateAsync(id, model)` / `Product.CreateAsync(id, p => …)`** do the same under a key you give. They
  are the only creates for a key nothing can produce, such as a strongly-typed id over a `string`. They are not
  generated for an integer key the database produces: an explicit identity value fails on SQL Server and leaves
  PostgreSQL's sequence behind.
- **`Product.CreateAsync(entity)`** inserts an aggregate you built with its own factory:
  `Product.CreateAsync(Product.Create("Anvil", new(30m, "EUR")))`.
- **An update** loads the row, applies the model and then the lambda, and saves **only the columns that changed**.
- **`Product.DeleteAsync(id)`** loads the row and soft-deletes it: `DeletedAt` is stamped and every read stops
  seeing it.

Each write is one unit of work for one aggregate, and each goes through EF Core's change tracker, so the
interceptors run exactly as for a hand-written save: `CreatedAt`/`UpdatedAt` are stamped, `Version` is bumped, a
delete becomes a stamp, and the aggregate's domain events are published after the commit.

- **The id is always yours.** The model carries none; the row is addressed by the `id` you pass, never by anything
  a form posted.
- **Values that do not come from the form** go in an optional lambda, which runs after the model's values are
  written, so it has the last word: `Product.CreateAsync(model, p => p.AssignTo(user.Id))`. With private setters
  the lambda calls the aggregate's methods, and so the rules in those methods run.
- **A stale edit is refused.** `UpdateAsync(id, model)` checks the `Version` the model carries and throws
  `DbUpdateConcurrencyException` when someone saved since; a model whose `Version` is `null` skips the check.
  `UpdateAsync(id, p => …)` and `DeleteAsync(id)` take an optional `version:` for the same check.
- **A missing row**, never created or soft-deleted, is `KeyNotFoundException`, naming the aggregate and the key.

A create needs the parameterless constructor an aggregate has when it declares none. One that declares a
constructor with arguments still gets `UpdateAsync` and `DeleteAsync`, and the build says why it has no
`CreateAsync` ([RASK086](diagnostics.md#rask086)).

### Changing two aggregates together: `db:`

**No `db:` means the write owns its unit of work; `db:` means you do.** Without a context, a write opens one,
saves and disposes it. Pass `db:` and it **stages** its change in that context and returns — you save:

```csharp
await using var db = await contexts.CreateDbContextAsync(ct);

var order = await Order.CreateAsync(orderModel, db: db, cancellationToken: ct);          // staged
await StockItem.UpdateAsync(stockId, s => s.Reserve(quantity), db: db, cancellationToken: ct);   // staged

await db.SaveChangesAsync(ct);   // both rows, or neither
```

There is no `BeginTransactionAsync` here, and there should not be: **one `SaveChangesAsync` is already one
transaction.** Several writes staged on one context commit together by construction.

This is also why a Rask app can change two aggregates at once without eventual consistency. Having had to
load each root by its own id, the write is deliberate — and a local transaction is a better answer than a
message for an app on one box. [Domain events](#keeping-state-inside-the-aggregate) remain there for when
eventual consistency is genuinely what you want.

Two things to know:

- **A staged `CreateAsync` returns an entity that is not yet persisted.** A `Guid` key is already set (the
  factory assigned it); a store-generated `int` key is `0` until you save.
- **The concurrency check still works and is still automatic.** `UpdateAsync(id, model)` pins the original
  `Version` from `model.Version`, and EF compares it at *your* save.

A row that context already tracks is the one updated, not a second copy.

### Named sets: `db.Orders`

Every mapped entity also gets an accessor on `DbContext`, beside the `Set<T>()` that always worked:

```csharp
await using var db = await contexts.CreateDbContextAsync(ct);

var order = await db.Orders.FindAsync([id], ct);   // children come with it
if (order!.TotalAmount > limit) { order.Hold(); }

await db.SaveChangesAsync(ct);
```

`db.Orders`, `db.OrderLines` — children too, since they are in the write context even though they are only
*writable* through their root. These are extension members on `DbContext` itself, so an app that brings its
own context gets them without the context having to be `partial`.

The name comes from one documented rule — `s`, `es` after `s`/`x`/`z`/`ch`/`sh`, and `y` → `ies` after a
consonant — and nothing cleverer, because an irregular guess is worse than a predictable one. A name two
entities want, or one `DbContext` already declares, is [RASK090](diagnostics.md#rask090) rather than a silent
rename.

### A create and an edit form

A form edits something mutable and an aggregate is not, so every aggregate gets a **form model generated beside
it**: `ProductModel` for `Product`. Forms always bind the model, never the aggregate. It is a plain class with a
settable copy of every mapped property, and:

- **every property is nullable**, because a form field can be empty whatever the column is;
- **`new ProductModel()` holds the aggregate's defaults**: it is filled from an empty `Product`, so a create form
  starts from the same values a `Product` would;
- **`product.ToModel()`** copies a row into a model for an edit form, `Version` included;
- **the aggregate's validation attributes are copied onto it**, so `Form.Model(…)` checks input by the aggregate's
  own rules as the user types, and EF Core reads the same attributes for the column.

A create page starts from a new one:

```csharp
[Route("/products/new")]
public sealed partial class NewProductPage(Navigator nav) : Component
{
    private readonly ProductModel _product = new();

    protected override Component Render() =>
        Form.Model(_product).OnValidSubmit(CreateAsync)[submitting => [
            UiInput.Bind(() => _product.Name).Label("Name"),
            UiInput.Bind(() => _product.Price!.Amount).Label("Price"),
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
        _product = await Product.ModelAsync(Id, cancellationToken: CancellationToken);

    protected override Component? Render() =>
        _product is null ? P["Loading…"] :
        Form.Model(_product).OnValidSubmit(SaveAsync)[
            _conflict is null ? null : UiAlert.Tone(UiTone.Warning)[_conflict],
            UiInput.Bind(() => _product.Name).Label("Name"),
            UiInput.Bind(() => _product.Price!.Amount).Label("Price"),
            UiTextarea.Bind(() => _product.Notes).Label("Notes"),
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

The list those pages link from is a [data grid](data-grid.md) over `Product.Read.AsQueryable()`
([above](#handing-a-query-to-a-component-asqueryable)): the rows are read faces, read-only by construction, and
each links to its edit page.

### Turning the form surface off

Not every aggregate should be creatable from a form. A passkey is minted by a WebAuthn ceremony; a session
is started by signing in. Declare a `Writes` const and the generated **form** surface narrows:

```csharp
public sealed class Passkey : Aggregate<Guid>
{
    public const ModelWrites Writes = ModelWrites.None;

    public string Name { get; private set; } = "";

    public void Rename(string name) => Name = name;
}
```

| `Writes` | What is generated |
|---|---|
| *(no const)* — same as `All` | everything, as always |
| `ModelWrites.Create` | `PasskeyModel`, `CreateAsync(model)`, `ModelAsync(id)`, `ToModel()` |
| `ModelWrites.Update` | `PasskeyModel`, `UpdateAsync(id, model)`, `ModelAsync(id)`, `ToModel()` |
| `ModelWrites.None` | no `PasskeyModel` at all, and nothing that takes one |

**It reaches the form surface and nothing else.** These are always generated, whatever the const says:

```csharp
await Passkey.CreateAsync(p => p.Rename("laptop"));       // behaviour — takes no model
await Passkey.UpdateAsync(id, p => p.Rename("desktop"));  // behaviour
await Passkey.DeleteAsync(id);                            // never took a model
await Passkey.Read.Where(p => p.UserId == me).ToListAsync();   // the read face is not negotiable
```

The read face stays because querying works through read models: an aggregate that could switch its own off
would be an aggregate nothing can read.

**Why a `const` and not an attribute.** C# itself refuses a non-constant initializer, so the value is always
there to be read at compile time — the generator can never quietly fail to find it and emit the whole
surface anyway. A wrong value is a compile error at the declaration, not a surprise at the call site.

A **child** takes its root's answer: its model exists to be an element of the root's list, so a child
declaring its own `Writes` is [RASK091](diagnostics.md#rask091) and is ignored.

### How a form save writes

A save writes **what the form holds**, property by property:

| The model's value | The aggregate's property | What is saved |
| --- | --- | --- |
| a value | any | the value |
| `null` | declared nullable (`string? Notes`) | `null`: the user emptied the field |
| `null` | not nullable (`string Name`) | nothing: the property keeps its value |
| a nested value-object model | a value object | merged over the value object the row holds, by the same rules |
| `Version` is `null` | | no concurrency check |

So a model from `new ProductModel()` or `product.ToModel()` saves exactly what the form shows, and a model a
command built with only some properties set leaves the non-nullable rest alone. To change a few properties from
code, `Product.UpdateAsync(id, p => p.Reprice(price))` says so directly.

A form save writes properties through the aggregate's private setters, with generated `[UnsafeAccessor]`s and no
reflection. **It does not call the aggregate's methods**, so a rule that must hold for form input belongs in a
validation attribute on the aggregate (copied to the model), and a change that is a domain operation goes through
a method in the lambda.

**Or send it in a command.** A command, a query, an API endpoint or an island prop can carry a `ProductModel`
(`public sealed record AddProduct(ProductModel Product) : ICommand<Guid>`), and the generators that build their
codecs recognise it although it is itself generated; the handler calls `Product.CreateAsync(command.Product)`. The
model carries every mapped property, so input from outside your own pages is best a command of its own that says
what may change.

**Your own write wins.** A static `CreateAsync(ProductModel, …)` or `DeleteAsync(Guid, …)` declared on `Product`
replaces the generated one at every call site (a member on the type beats an extension member), and the
generated one stays reachable as `ProductModelExtensions.CreateAsync(…)` for an override that only adds to it.

### What the model carries

- **Every mapped property** of the aggregate, nullable, and its `Version`.
- **Not** the `Id`, `CreatedAt`, `UpdatedAt`, `DeletedAt`, the domain events, navigations, collections or computed
  properties.
- **A value object as a model of its own:** `Money Price` on `Product` is a nested `MoneyModel? Price` on `ProductModel`, filled by `new ProductModel()` and `ToModel()`, so a
  form binds `() => _product.Price!.Amount` like any [nested model](forms-advanced.md), however `Money` is declared.
  A one-value value object (`record Email(string Value)`) is carried as its value: `string? Email`.

A `partial class ProductModel` of your own merges into the generated one, which is the way to add members,
`IValidatableObject` or display helpers. The build says when it cannot generate: a **hand-written, non-`partial`
`ProductModel`** beside a `Product` is an error ([RASK082](diagnostics.md#rask082)), and an aggregate **declared
inside another class** is a warning and gets no model ([RASK083](diagnostics.md#rask083)).

On the wire a model's properties are **camelCase**. Only validation attributes are copied from the aggregate, so a
`[JsonPropertyName]` on the aggregate does not rename the model's property; a property you declare yourself on a
`partial ProductModel` keeps its own pin.

## Writing: plain EF Core

The writes on the type cover a create, an update and a delete. **Everything past that is ordinary EF Core** — a
change several entities make together, a query-then-decide, a bulk statement: load the entities into a context,
call their methods, save — so the entity's own rules run on every write, and so do the interceptors:
`CreatedAt`/`UpdatedAt` are stamped, `Version` is bumped, a delete of an aggregate becomes a
`DeletedAt` stamp, and the aggregate's domain events are published after the commit. There is no Rask-owned
unit of work to learn.

`RaskAppDbContext` (namespace `Rask`) is the context the host builds: every entity you declared, plus every
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

The one thing to remember is the one from [Reading](#reading-the-read-face): a `ProductRead` is not a
`Product` and nothing is tracking it, so load the entity you are about to change from the context that is
going to save it.

### Keeping state inside the aggregate

State changes only through the aggregate's own methods, which keeps its invariants and its domain events in one
place. The build holds that line:

- **No public setters** ([RASK084](diagnostics.md#rask084), an error). A public `set`, a hand-written public
  `init` or a public mutable field on an aggregate, an entity (or an abstract base the app puts between them) or a
  value object one of them holds is refused, with a lightbulb that makes it `private`. EF Core and the generated
  writes both work through private setters. A positional record's parameters are exempt, so
  `record Money(decimal Amount, string Currency)` is never reported.
- **No mutable collections of entities** ([RASK085](diagnostics.md#rask085)). A navigation to many
  entities is exposed read-only, over a private field EF Core maps, and changed through a method. The
  lightbulb rewrites it:

```csharp
public sealed class Order : Aggregate<Guid>
{
    private readonly List<OrderLine> _lines = [];

    public IReadOnlyCollection<OrderLine> Lines => _lines;

    public void AddLine(Guid productId, int quantity) => _lines.Add(OrderLine.For(productId, quantity));
}

public sealed class OrderLine : Entity<Guid>
{
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }

    public static OrderLine For(Guid productId, int quantity) =>
        new() { Id = Guid.CreateVersion7(), ProductId = productId, Quantity = quantity };
}
```

**The entity owns its key.** Rask.Data's key convention leaves an integer key to the database's identity and
marks every other key never generated, so a `Guid` or a strongly-typed id is assigned where the entity is
built: `Id = Guid.CreateVersion7()` in its factory. That is what lets a child with an id of its own be added
to a loaded aggregate and saved as an insert. An entity added with its key still at the default is refused at
the save, by name, rather than inserted with an empty key. A key you configured yourself
(`ValueGeneratedOnAdd()`, a database default) is left as you set it.

**One aggregate refers to another by id.** `Order` holds a `Guid ProductId`, not a `Product`: each aggregate is
loaded and saved on its own, and a change that spans two is [one context, saved once](#changing-two-aggregates-together-db).

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

**A batch soft delete is an update, not `ExecuteDeleteAsync`.** `db.Remove(product)` on an aggregate
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

Assert.Equal(12.50m, (await database.LoadAsync<Product>(anvil.Id))!.Price);
```

```csharp
database.Context.AddRange(Order.Place("B-2"), Order.Place("B-3"));
await database.Context.SaveChangesAsync();

Assert.Equal(2, await Order.Read.CountAsync());
```

`TestDatabase.StartAsync` maps every entity the build found — so no fixture has to list entities — creates the
schema, wires the auditing and soft-delete interceptors so the conventions behave as they do in
production, and points the model surface at it. Disposing clears it, so one test cannot leak its
database into the next. It takes a `TimeProvider`, so audit stamps are assertable.

It is provider-agnostic on purpose: the options callback is yours, so `Rask.Data` gains no provider
dependency and a test runs against the database the app actually uses. SQLite over a temporary file is
the usual choice; `:memory:` lives only as long as its connection, and every read opens its own.

Domain-event *publication* is deliberately not wired — that needs a service provider to resolve handlers
through, which is more than a database fixture should invent. Assert on `DomainEvents` instead.

## Mapping rules the conventions don't cover

A length, an index, a relationship, a converter — put them in a **static `Configure`** on the entity
itself. No attribute, no interface, no separate class:

```csharp
public sealed class Product : Aggregate<Guid>
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
soft-delete query filter or dropping the concurrency token. It is optional; an entity without one is
mapped by convention.

The method is matched by signature, so a near miss (an instance method, a private one, the wrong
builder type) is reported as [RASK072](diagnostics.md#rask072) rather than silently not called.

## Value objects

A value object needs **no marker and no base class**. Any composite an entity holds that is not itself an
entity (a record, a struct or a plain class) is a value object, mapped as an EF Core **complex type**: its
properties become columns on the owning row.

```csharp
public sealed record Money(decimal Amount, string Currency);
public sealed record Email(string Value);

public sealed class Customer : Aggregate<Guid>
{
    public Money CreditLimit { get; private set; } = new(0m, "EUR");   // CreditLimit_Amount, CreditLimit_Currency
    public Email Email { get; private set; } = new("");                 // one column: Email
}
```

**A one-value value object is one column named after the property**, so `Email Email` is stored as `Email`, and a
form model carries it as its value, `string? Email`. A value object with several values is prefixed by the
property, and a form model carries it as a nested model.

What is **not** a value object: a type EF Core maps as a column on its own (`string`, `DateTime`, `Guid`, an
enum, anything from `System` or `Microsoft`), a collection, an abstract or generic type, and an `Entity<TId>`.

**A complex type, not an owned entity**, and the distinction is the point. An owned entity is a row
with hidden identity: tracked separately, nullable in ways a value has no business being, and quietly
producing a join. A complex type is part of the row, which is what a value object *is*, and it is why `Money` can
be held by two aggregates without either owning it.

Nesting works: a value object made of value objects is mapped all the way down. **One EF Core
constraint applies to the outer one**: a complex type is materialised through its constructor, and EF
cannot bind a nested complex type to a constructor parameter. So a value object that *contains another
value object* needs a parameterless constructor and private setters, which is all EF Core needs:

```csharp
// holds only scalars: a positional record is fine
public sealed record Money(decimal Amount, string Currency);

// contains a value object: a parameterless constructor and private setters
public sealed class Packaging
{
    private Packaging() { }

    public Packaging(Money cost, string material) => (Cost, Material) = (cost, material);

    public Money Cost { get; private set; } = new(0m, "EUR");

    public string Material { get; private set; } = "card";
}
```

A *collection* of value objects is not mapped automatically; configure it in the aggregate's own
`Configure`.

## Strongly-typed ids

Use one as the aggregate's key and it is converted to its underlying value automatically — nothing declares
it as an id:

```csharp
public readonly record struct ProductId(Guid Value);

public sealed class Product : Aggregate<ProductId> { }
```

The converter is registered once for the type, in `ConfigureConventions`, so **every** property of that
type is converted — the key, a foreign key on another entity, a nullable one — without any of them being
named. An id the generator cannot build a converter for is reported as
[RASK073](diagnostics.md#rask073) rather than left to fail at model build.

Ids that need no converter — `Guid`, `int`, `long`, `string` — are left alone.

Like any key that is not an integer, a strongly-typed id is the entity's to assign — `new ProductId(Guid.CreateVersion7())`
in its factory — because nothing generates one through a converter.

## Using EF Core the usual way

None of the above is compulsory, and opting out is not deriving from `Entity<TId>`. A class that does not
derive from it is an ordinary EF Core entity: write your own `DbContext`, your own
`IEntityTypeConfiguration`, your own `DbSet` properties, and use them exactly as you do today.

Registering an `IDbContextFactory<YourContext>` is the whole of opting out at the app level. Rask binds
the model surface and every database-backed battery to the context you registered, and
`RaskAppDbContext` is never constructed. Call `modelBuilder.ApplyRaskConventions()` from its
`OnModelCreating` to keep the soft-delete filters and concurrency tokens, or
`ModelRegistry.Apply(modelBuilder)` to keep every declared entity mapped as well.

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
        base.OnModelCreating(modelBuilder);  // every Entity<TId> you declared
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.ApplyRaskConventions(); // query filters + concurrency tokens — always last
    }
}
```

Over plain `DbContext` this compiles, boots and migrates with every entity silently absent, so the first
`Product.Read.Where(…)` throws saying the type is not part of the model. If you
cannot change the base type — it is already someone else's — call `ModelRegistry.Apply(modelBuilder)` in
place of `base.OnModelCreating`, and `ModelRegistry.ApplyConventions(configurationBuilder)` from an
overridden `ConfigureConventions`.

## What the interceptors do

- **`AuditingInterceptor`** — stamps `CreatedAt`/`UpdatedAt` (UTC, from an injectable `TimeProvider`) and
  increments each aggregate's `Version` on update, so the stored token changes (SQLite has no rowversion).
- **`SoftDeleteInterceptor`** — rewrites a `Deleted` aggregate to `Modified` + sets `DeletedAt`.
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

Every aggregate's `Version` is an EF Core concurrency token, bumped on every save. `Product.UpdateAsync(id, model)`
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
instead:

```csharp
await db.BulkInsertAsync(products, o => o.SkipChangeTracking = true);
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
| `ignoreSoftDeleted` | Lets a soft-deleted row free its slot. Defaults to on for aggregates, ignored otherwise. |

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

## Full-text search

A search box over your models wants ranked, word-aware matching, not `Contains` — which is a
`LIKE '%…%'` scan that cannot rank and misses `kérés` for `keres`. Declare which text is searchable, and
the migration creates a real full-text index:

```csharp
modelBuilder.Entity<Product>().HasFullTextSearch(p => new { p.Name, p.Description });
```

Then search from the model type, a context, or a grid:

```csharp
await Product.Read.Search(query).Where(p => p.Active).Take(20).ToListAsync();
await db.Set<Product>().Search(query).CountAsync();
UiDataGrid.Data(Product.Read.Search(query).AsQueryable())
```

`Search(text)` keeps every row containing all of the typed words — any order, any case, diacritics
ignored, the last word as a prefix — **best match first**, and keeps composing; a later `OrderBy`
replaces the rank order. The text is always words, never query syntax, so nothing a user types can break
the query. Blank text filters nothing. `FullText.Highlight(p.Name)` and `FullText.Snippet(p.Description)`
inside a `Select` return the matched terms marked, rendered safely by `UiHighlight.Text(...)`.

The index lives in the database and triggers keep it current, so raw SQL and other processes are searchable
too. Adding the declaration to an existing table is its own migration, which fills the index from the rows
already there. Requires `UseRaskSqlite(...)`; on any other provider `AddRaskData<TContext>` **refuses to
boot** rather than letting the first search fail. How it works, the tokenizers and the costs are in
[Rask.SQLite — Full-text search](sqlite.md#full-text-search--fts5-through-ef-core).

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
against a real server. Two things change:

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
