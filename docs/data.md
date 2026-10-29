# Rask.Data — aggregates, queries, and the database you don't write

> **In practice:** [Tutorial Ch 2](tutorial/02-first-feature.md) · recipe [add a feature to an existing database](recipes.md#add-a-feature-to-an-existing-database) · [cheat sheet](cheatsheet.md).

`Rask.Data` is a layer over **Entity Framework Core** with one goal: **you declare aggregates, and that is
all**. No `DbContext` class, no `DbSet` property, no `IEntityTypeConfiguration`, no registration, and
no `IDbContextFactory` injected into every page that reads a row.

Underneath it is ordinary EF Core, and nothing is hidden from you. **The aggregate type writes**:
`Product.Create(model)`, `Product.Update(id, model)`, `Product.Delete(id)`
([below](#writing-create-update-delete)). **Its read face queries**: `Product.Where(…)`
([below](#reading-the-read-face)). Anything richer is EF Core exactly as you know it, a domain method
saved through a context ([below](#writing-plain-ef-core)), and an app that outgrows the conventions writes its
own context and Rask steps aside ([below](#using-ef-core-the-usual-way)).

**Why two faces and not one.** An aggregate is a consistency boundary, and it holds another aggregate's *id*
and never a navigation to it — that is what stops a write crossing a boundary by accident, and
[RASK087](diagnostics.md#rask087) enforces it. A query that reached across a boundary from the write side
would be that border failing. So reading moves to a generated **read face** — primitives, no behaviour, not in
the write context — which carries the navigations the aggregate is not allowed to have. **The border is on
the write side only; the read side has none.**

> Included in [`Rask.Server`](../README.md), so there is nothing to install. It is **on**; an app that does without it says so:
>
> ```csharp
> app.Configure(c => c.Data.Off());
> ```

## The whole of it

```csharp
public sealed record Money(decimal Amount, string Currency);   // a value object: a record needs no base

public sealed class Product : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
{
    [MaxLength(200)]
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

The type arguments after the key are **traits** — what the table carries, in any order: here `CreatedAt` and
`UpdatedAt`, a `Version` check, a form model and a generated `Delete`
([the whole list](#choosing-what-a-table-carries)). A bare `Aggregate<Guid>` asks for none of them.

That compiles into a mapped table, a generated `ProductModel` [for its forms](#a-create-and-an-edit-form),
a `db.Products` accessor on any `DbContext`, a read face — `ProductRead`, queried straight off the type — and
`Product.Find(id)` + `product.Save()` for changing one. That is everything a screen needs:

```csharp
var cheap = await Product.Where(p => p.PriceAmount < 10).OrderBy(p => p.Name);
var anvil = await Product.Where(p => p.Id == id).First();
var page  = await Product.OrderBy(p => p.Name).Skip(40).Take(20);   // one page of a table, in SQL
```

Note `p.PriceAmount`: the read face is **primitives**, so the `Money Price` value object arrives as the two
columns it is stored in. That is the whole of the translation — [the read face](#reading-the-read-face) has
the rules.

And to write it, from a form, from code, or inside a transaction you already hold:

```csharp
var product = await Product.Create(model);                                     // ProductModel from a form
await Product.Update(product.Id, edit);                                        // stale Version throws

var other = Product.Create("Anvil", new(9.90m, "EUR"));                        // no form: the aggregate's own factory …
await other.Save();                                                            // … inserts it
await Product.Update(other.Id, p => p.Reprice(new(12.50m, "EUR")));            // and a lambda changes it

await Product.Delete(product.Id);                                              // removes the row
```

A source generator finds every aggregate at build time and hands it to `RaskAppDbContext`; the host points
the model surface at the database. There is nothing else to write and nothing to register.

**Generated, never reflected.** No assembly is scanned and no method is found by name, so a trimmed
publish cannot quietly drop an aggregate and leave you a missing table with a green build.

### Aggregates, entities and value objects

Rask.Data speaks domain-driven design, and the base class you derive from says which of the three a type is. Two
entities are equal when they are of the same type with the same `Id` — two loads of one row are the same entity —
and one with no id yet (an integer key before its insert) is equal only to itself:

| You declare | What it is | What it gets |
| --- | --- | --- |
| `Aggregate<TId, …>` | A consistency boundary: the thing you load, change and save as one | Its table, the static writes, its read face, domain events — and the [traits](#choosing-what-a-table-carries) it names: timestamps, `Version`, a form model, a `Delete` |
| `Entity<TId>` | Something with identity inside an aggregate, such as an order's line | Its table, and a form model its parent's carries ([below](#children)); it is loaded, saved and deleted with its aggregate |
| a `record`, a `record struct`, or a class on `ValueObject` it holds | A value object: `Money`, `Address`, `Email` | Columns on the owner's row ([below](#value-objects)) |

`Aggregate<TId>` derives from `Entity<TId>`, and the framework's columns are the ones its base names. They are
real properties with private setters, so a screen can show them and a query can sort by them, while only the
framework writes them:

| Property | Declared on | Effect |
|-----------|--------|--------|
| `Id` | `Entity<TId>` | The key. A `Guid` or strongly-typed id is assigned in the factory; an integer is the store's identity. |
| `CreatedAt`, `UpdatedAt` | `Entity<TId>` | **With [`Timestamps`](#choosing-what-a-table-carries)** (or `Created` / `Updated` for one). Stamped (UTC) on insert and on every update. |
| `Version` | `Aggregate<TId>` | **With [`Versioned`](#choosing-what-a-table-carries).** The optimistic-concurrency token, bumped on every update. |
| `DeletedAt` | `Aggregate<TId>` | **With [`SoftDelete`](#choosing-what-a-table-carries).** The generated `Delete` stamps it instead of removing the row. |

```csharp
var recent = await Product.OrderByDescending(p => p.CreatedAt).Take(10);
```

`Aggregate<TId>` also carries the domain-events buffer: `Raise(…)` inside a method. After the save commits, each
`IEventHandler<T>` runs in memory, and each `IDurableHandler<T>` runs from the [outbox](outbox.md), which stored it in
the same transaction ([below](#what-the-interceptors-do)).

**Aggregates declare no constructor.** The implicit parameterless one is what EF Core materialises rows
through, what `Product.Create(model)` starts from, and what gives `new ProductModel()` the aggregate's
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
public sealed class Order : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
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

An auto-property works the same: `public IReadOnlyList<OrderLine> Lines { get; private set; } = [];`, refilled
with `Lines = [.. Lines, line]`. Rask hands EF Core a collection it can add to whenever it loads or saves the lines.

**Loading one root loads it whole.** Every write that starts from an id brings the children with it, and so
does `db.Orders.FindAsync(id)`. On the read face the children are an ordinary navigation, so you ask for them
when you want them — listing a thousand orders should not drag in every line each of them holds:

```csharp
await Order.Update(id, o => o.Add("anvil", 1));                      // loaded whole, changed, saved

var open = await Order.Where(o => o.Reference.StartsWith("2026"));   // Lines EMPTY
var withLines = await Order.Query((q, ct) => q.Include(o => o.Lines).ToListAsync(ct));

// …and a child is queryable on its own, because the read side has no borders:
var heavy = await OrderLine.Where(l => l.Quantity > 10 && l.Order.Reference.StartsWith("2026"));
```

**A change to any part is a change to the whole.** A line's quantity moving stamps the order's `UpdatedAt` and
bumps its `Version` (it names `Timestamps` and `Versioned`), so the version a caller read stops being current — which is what makes the aggregate, and
not the row, the unit of concurrency:

```csharp
await Order.Update(id, o => o.Lines.First().SetQuantity(5));
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

await Order.Update(id, model);
```

> **What the posted list holds is what the aggregate holds afterwards.** A row with no `Id` is added, a row
> whose `Id` matches a stored child updates it, and **a stored child whose id is in none of the posted rows is
> removed** — so a form that renders only some of the lines deletes the rest, and an empty list deletes them
> all. Bind the whole collection, or apply the change through a domain method (`Order.Update(id, o =>
> o.Add(…))`) instead of a partial model.

An `Id` that matches nothing in *this* aggregate is never followed: it lands as a new child with an id of its
own. A posted id therefore cannot reach — or delete — another aggregate's line.

**A child cannot outlive its parent.** Rask makes the relationship required and its delete a cascade, so taking a
line out of the collection deletes the row. Left to EF Core's own convention the foreign key would be nullable,
and severing a child would set that key to `NULL` and leave the row in the table — invisible through the
navigation, unreachable through the aggregate, and impossible to delete through it either.

Deleting the aggregate follows the same rule: `Order.Delete(id)` removes the order and its lines with it.
An order that names [`SoftDelete`](#choosing-what-a-table-carries) instead of `Deletable` is different, and deliberately
so: `DeletedAt` is stamped rather than the row removed, so nothing cascades and the lines are still there if the
order comes back.

The child gets a table, a model so its parent's form can carry it, and a read face of its own —
`OrderLine.Where(…)` — because the read side has no borders. It gets no **writes** of its own: there is no
`OrderLine.Create`, no version and no soft delete, because it is not a thing you save on its own. A collection of
another **aggregate** is not a child at all ([RASK087](diagnostics.md#rask087)), and a collection Rask cannot
write is [RASK088](diagnostics.md#rask088).

The field behind a read-only view can be named anything — `Notes => _items.AsReadOnly()` — and can live on an
abstract base, generic or not: the collection is loaded into and saved from that field, not the view.

**A child is written only through its aggregate.** A save that adds, changes or removes a child whose root the
same context does not hold — a line found through `db.OrderLines` and edited, a `new OrderLine` attached on its
own, a `BulkInsert` of lines — is refused with an `InvalidOperationException` naming the child and its root.
`Tenant.Across()` does not lift it: it is about the aggregate's shape, not whose rows are visible. Load the
order, change it, save the order:

```csharp
await Order.Update(id, o => o.Lines.First().SetQuantity(5));   // ✓ through the aggregate

var line = await db.OrderLines.FirstAsync(ct);
line.SetQuantity(5);
await db.SaveChangesAsync(ct);   // ✗ 'OrderLine' was changed on its own … load its 'Order'
```

**Timestamps are the aggregate's**, like its tenant. A child has no `CreatedAt` or `UpdatedAt` — not in its
table, not on `OrderLineRead`, never stamped — and the root's `UpdatedAt` moves whenever any of its children is
added, changed or removed, so it means "anything in this order changed". A child whose table does carry them
names the trait, and keeps them — a timestamp is the only trait an `Entity` takes:

```csharp
public sealed class OrderLine : Entity<Guid, Timestamps>   // this child table has CreatedAt and UpdatedAt
{
}
```

### A key of several columns

A child that is the meeting of two aggregates (which transactions fuelled which delivery note) is keyed by the pair,
not by an `Id` of its own. Declare the key as a tuple of the **types** it joins, and make the class `partial`:

```csharp
public sealed class DeliveryNote : Aggregate<int, PerTenant>   // no Model: a form does not carry it yet, see below
{
    private readonly List<DeliveryNoteTransaction> _fuellings = [];

    public IReadOnlyList<DeliveryNoteTransaction> Fuellings => _fuellings;

    public void Fill(IEnumerable<(int TransactionId, decimal Quantity)> rows)
    {
        _fuellings.Clear();
        _fuellings.AddRange(rows.Select(r => DeliveryNoteTransaction.For(r.TransactionId, r.Quantity)));
    }
}

public sealed partial class DeliveryNoteTransaction : Entity<(DeliveryNote, Transaction)>
{
    public decimal Quantity { get; private set; }

    internal static DeliveryNoteTransaction For(int transactionId, decimal quantity) =>
        new() { TransactionId = transactionId, Quantity = quantity };
}
```

Rask generates the rest. Each aggregate in the tuple becomes a `{Aggregate}Id` of **that aggregate's own key
type**, so it is never written twice. `Id` becomes the key's values together:

```csharp
// generated
public int DeliveryNoteId { get; private set; }
public int TransactionId { get; private set; }
public new (int DeliveryNoteId, int TransactionId) Id => (DeliveryNoteId, TransactionId);
```

The table's primary key is `(DeliveryNoteId, TransactionId)`, in the order the tuple names them, and nothing
generates either value. A part that is a plain value is named, and the entity declares it:
`Entity<(Order, int LineNo)>` with `public int LineNo { get; private set; }`. The same key reaches a context you
wired yourself, through `ApplyRaskConventions`.

It reads like any child. The read face lists the key's parts where `Id` would be. Each part infers its navigation
by the usual `{X}Id` rule. The tenant comes through the aggregate that holds it, so the row needs no `TenantId`
of its own:

```csharp
var fuellings = await DeliveryNoteTransaction.Where(t => noteIds.Contains(t.DeliveryNoteId))
    .Select(t => new { t.DeliveryNoteId, t.TransactionId, t.Quantity, t.Transaction.ProductId });

var note = await DeliveryNote.Include(n => n.Fuellings).First(n => n.Id == id);
```

**It is written through its aggregate**, like any child. The entity makes itself in a factory of its own, setting
the parts it knows — the generated ones have private setters, so its own type can set them — and Rask fills the
part that names the holding aggregate when the save attaches it. Replacing the whole collection, as `Fill` does,
is the usual shape, and the save matches the new rows to the stored ones **by key**:

```csharp
await DeliveryNote.Update(id, n => n.Fill(rows));
// stored (10, 12.5) (11, 3.25) (13, 2) ← rows (10, 12.5) (11, 4) (12, 1)
// UPDATE DeliveryNoteTransactions SET Quantity = @p0 WHERE DeliveryNoteId = @p1 AND TransactionId = @p2   (11)
// INSERT INTO DeliveryNoteTransactions (DeliveryNoteId, TransactionId, Quantity) VALUES (…)               (12)
// DELETE FROM DeliveryNoteTransactions WHERE DeliveryNoteId = @p0 AND TransactionId = @p1                 (13)
//                                                                                     nothing for (10)

var note = DeliveryNote.Issue("A-1", rows);   // a factory of the note's own, which calls Fill
await note.Save();                           // the note, then its rows under the note's new key
```

The same holds for `Find` + `Save()` and for a shared context (`db:`): every row the aggregate holds afterwards is in
the table, and nothing else is. A row changed or added without its aggregate is refused, as for any child.

**A grandchild names the child that holds it.** A column of a report, keyed by the report and its own number, holds
totals keyed by the column and theirs. The column's whole key becomes the grandchild's, one generated property per
part, named as EF Core names a foreign key to it:

```csharp
public sealed partial class ReportColumn : Entity<(Report, int ColumnId)>
{
    private readonly List<ReportGroupTotal> _totals = [];

    public int ColumnId { get; private set; }

    public IReadOnlyList<ReportGroupTotal> Totals => _totals;
}

public sealed partial class ReportGroupTotal : Entity<(ReportColumn, int TotalColumnId)>
{
    public int TotalColumnId { get; private set; }
}

// generated: ReportColumnReportId, ReportColumnColumnId, and
// public new (int ReportColumnReportId, int ReportColumnColumnId, int TotalColumnId) Id
```

A column that is replaced takes the new instance's totals with it, and they are matched by key the same way.

A class that is not `partial`, a part with no name or a named aggregate or entity part, a name that is already
taken, and a key of several columns on an aggregate **root** are [RASK106](diagnostics.md#rask106).

#### Link tables and child rows on the form

An aggregate whose base names `Model` carries these rows on its form too, in one of two shapes, and saves them in
the aggregate's one save. A table an existing app made — a company, the activities it does, and the columns of its
report:

```csharp
public sealed class Company : Aggregate<int, Model, PerTenant<int>>
{
    private readonly List<CompanyActivity> _activities = [];
    private readonly List<CompanyColumn> _columns = [];

    public string Name { get; private set; } = "";
    public IReadOnlyList<CompanyActivity> Activities => _activities;
    public IReadOnlyList<CompanyColumn> Columns => _columns;
}

public sealed partial class CompanyActivity : Entity<(Company, Teaor)> { }   // CompanyTeaors(CompanyId, TeaorId)

public sealed partial class CompanyColumn : Entity<(Company, int ColumnId)>
{
    public int ColumnId { get; private set; }
    public int Order { get; private set; }
    public bool Sum { get; private set; }
}
```

**A link — the holder's part and one other, and nothing else — is a list of ids.** It is named after the collection,
made singular, plus `Ids` (`Activities` → `ActivityIds`, `Statuses` → `StatusIds`, `Boxes` → `BoxIds`, `People` →
`PersonIds`), and holds the other part's key type. The singular follows Rails' rules — `ies` → `y`, `es` dropped where
it was added (after x/ch/sh/ss/zz, and `Statuses`, `Buses`, `Aliases`), a final `s`, and a short list of irregular
plurals — so `Houses` is `HouseIds`. A name no rule makes singular keeps the collection's own name: `StaffIds`. It is saved as a
**set**: a link is inserted for an id it does not hold yet, deleted for one no longer listed, never updated, and an
id given twice is one link. The same set in another order writes nothing.

```csharp
var model = await Company.Model(id);           // model.ActivityIds = [0111, 0112]
model.ActivityIds = [teaorA.Id, teaorB.Id];
await Company.Update(id, model);               // INSERT the new link, DELETE the dropped one, nothing else
```

Laravel's `$company->teaors()->sync($ids)` and Rails' `company.teaor_ids = ids`, on the form model.

**A row with fields of its own is a list of rows**, keeping the collection's name. Each row is a generated
`CompanyColumnModel` carrying the parts of the key the holder does not give — `ColumnId` here, `TransactionId` for
`Entity<(DeliveryNote, Transaction)>` — and the row's own settable fields. The holder's part is never on the form:
the save fills it. Rows are matched to the stored ones **by key**, as a refill is above:

```csharp
model.Columns = [new() { ColumnId = 3, Order = 1, Sum = true }];
await Company.Update(id, model);               // UPDATE a changed row, INSERT a new key, DELETE a dropped one
```

`Company.Create(model)`, `Company.Update(id, model)` and `Company.Model(id)` carry both; with `db:` or inside
`Db.Transaction` they stage with everything else, so several aggregates saved together are one transaction. A
per-tenant holder keeps its rows to its tenant through itself — the rows carry no `TenantId`.

**What a form is told.** A row whose key part is empty is required, under its own field (`Columns[0].ColumnId`).
Two rows of one key are refused under `Columns` before anything is loaded — the strings catalog's
`ValidationDuplicateRow`. An id the database refuses — no such activity, the foreign key the table declares — is a
field error under `ActivityIds` (or `Columns`) saying `ValidationNotFound`, and the whole save, every aggregate in it,
is rolled back. Both are in the reader's language — the kit carries Hungarian — and an app's own catalog overrides
them ([Localization](localization.md)).

**What a form still cannot carry is [RASK106](diagnostics.md#rask106):** rows that hold rows of their own (a
`ReportColumn` holding `Totals`), a row with no parameterless constructor to add one with, and a key with no part for
the aggregate that holds it. Leave `Model` off the holder's base and fill those through a method of the aggregate.

### Collections of values

A collection whose elements are **values** rather than entities is one column, not a table. Declare it the same
way as a child — a private field and a read-only view — and nothing else is configured:

```csharp
public sealed class Trip : Aggregate<Guid, Model>
{
    private readonly List<string> _tags = [];
    private readonly List<Stop> _stops = [];

    private Trip() { }

    public IReadOnlyList<string> Tags => _tags;        // one column of plain values
    public IReadOnlyList<Stop> Stops => _stops;        // one JSON column of value objects

    public void Tag(string tag) => _tags.Add(tag);
    public void StopAt(string city, int day) => _stops.Add(new Stop(city, day));
}

public sealed record Stop(string City, int Day);
```

`Tags` becomes a **primitive collection** and `Stops` a **JSON column**. Both appear on the form model and on
the read face, and both are **queryable in SQL**:

```csharp
await Trip.Where(t => t.Tags.Contains("urgent"));
await Trip.Where(t => t.Stops.Any(s => s.City == "Vienna" && s.Day >= 2));
```

**Values are replaced wholesale, not reconciled.** A child has an id, so a form post updates the rows it names
and deletes the rest. A value has no id, so there is nothing to match against: what the form posts is what the
aggregate holds afterwards, and an empty list clears the collection.

**The column is NULL only where the model says it may be.** `Tags` and `Stops` above are `NOT NULL`, and an empty
collection is stored as `[]`, never as NULL. A nullable one, `List<Stop>? Detours { get; private set; }`, is a NULL
column. This works the same way as Laravel's `$table->json('stops')` and Rails' `t.json :stops, null: false`. An
adopted `Stops nvarchar(max) NOT NULL` column is mapped as it is, with no migration.

A JSON column is an EF Core *complex collection*, which EF Core writes through a list, so the property needs a
`List<T>` behind it. **A get-only `IReadOnlyList<T>` auto-property keeps a NULL column**, whatever its nullability:

```csharp
public IReadOnlyList<Stop> Stops { get; private set; } = new List<Stop>();   // NULL column: no list behind it

private readonly List<Stop> _stops = [];
public IReadOnlyList<Stop> Stops => _stops;                                  // NOT NULL: the field is the list

public List<Stop> Stops { get; private set; } = [];                          // NOT NULL: the property is the list
```

The first shape is mapped the old way, as an owned collection, and EF Core makes an owned collection's column NULL and
refuses `IsRequired()` on it.

**Moving an existing app onto `NOT NULL` is one migration, with no edit.** Every value-object collection used to be a
NULL column, so the next `rask db add` writes an `AlterColumn` to `NOT NULL` for each non-nullable one, with
`defaultValue: "[]"`. SQLite, SQL Server and PostgreSQL fill every NULL row with `[]` before the column turns
`NOT NULL`, and an `AddColumn` of a new collection fills the rows already there the same way. EF Core on its own would
write `"{}"`, an object that no collection can read back, so a context writes its migrations through Rask's differ
when it is on `UseRaskSqlite`, `UseRaskPostgres` or `UseRaskSqlServer`, or is a RaskApp's. A context on plain
`UseSqlite`, `UseNpgsql` or `UseSqlServer` gets EF Core's `"{}"`: switch it to the Rask call. A JSON column that holds
one value object keeps `"{}"`.

**Choose a child entity instead when the elements need identity** — to be referenced, updated one at a time, or
indexed. That is the real trade-off, and the deciding factor is usually the index:

| | SQLite | PostgreSQL | SQL Server |
|---|---|---|---|
| plain values | `json_each` | native `text[]` | `OPENJSON` |
| value objects | `json_each` | `jsonb` | `OPENJSON` |
| index over every element | **none** | GIN | — |

PostgreSQL can index straight into both. SQLite needs no extension for JSON (`json_extract` is core since
3.38), and it can index a **fixed** path through a generated column — but it has no equivalent of GIN, so a
filter over *any* element of the collection is a scan. If that filter is hot on SQLite, make the elements
`Entity<TId>` children and index the column.

`byte[]` is never a collection of bytes: it stays one BLOB column. Anything Rask has no column for — a
`Dictionary`, a collection of some framework type — is left alone for the entity's own `Configure` to map, and
so is any collection that `Configure` already mapped.

A converter on the **element** is read back on the read face too — an enum list stored by name, or values the app
stores its own way:

```csharp
builder.PrimitiveCollection(s => s.Handling).ElementType().HasConversion<string>();   // ["Fragile","Cold"]

await Skid.Where(s => s.Handling.Contains(SkidHandling.Cold));                         // the enum, not its name
```

Here is a whole model running — a `Note` aggregate, queried with `Note.OrderByDescending(…)`, written with `Note.Create`, and
searched with `Note.Search`, in a SQLite database that lives in this browser tab and survives a reload:

<!-- demo:data-notes -->

## Reading: the read face

There are three doors on the aggregate type, one per need:

| To… | Write | You get |
|---|---|---|
| show one, many, or joined | `Product.Where(…)`, `Product.OrderBy(…)`, … | rows of `ProductRead` — the read face |
| change one | `Product.Find(id)` → its methods → `product.Save()` | the aggregate itself |
| fill an edit form | `Product.Model(id)` | a `ProductModel` |

A query is called on `Product`, but its rows are the generated **read face**, `ProductRead` — primitives and
navigations, no behaviour:

```csharp
await Product.All;
await Product.Where(p => p.Active).OrderBy(p => p.Name);
await Product.Where(p => p.PriceAmount > 10).OrderByDescending(p => p.PriceAmount).Skip(20).Take(20);
await Product.OrderBy(p => p.Name).Select(p => p.Name);   // reads one column
await Product.IgnoreQueryFilters();                       // soft-deleted rows too
await Product.Where(p => p.Id == id).First();             // by id — throws when there is no such row
await Product.Where(p => p.Id == id).FirstOrDefault();    // …or null instead
await Product.First(p => p.Name == "Anvil");
await Product.SingleOrDefault(p => p.Name == "Anvil");    // null for none, throws for two
await Product.Count(p => p.Active);
await Product.Any();
await Product.Search("red anvil").Take(20);               // full-text, best match first
await foreach (var p in Product.AsAsyncEnumerable()) { }
```

`First`, `FirstOrDefault`, `Single` and `SingleOrDefault` mean exactly what they mean in LINQ and EF Core: `First` and
`Single` hand back a non-nullable row and throw `InvalidOperationException` when nothing matched (`Single` also when two
did), while the `…OrDefault` forms return `null` for "nothing matched". Pick `FirstOrDefault` wherever a missing row
is an outcome your code handles — a stale id in a URL, a job whose row was deleted since it was queued.

These are C# 14 static extension members, which is why nothing has to be declared or derived from a second
base. A member declared on the aggregate itself always wins, so your own static `Where` would be untouched.

### What the read face is

One is generated for **every mapped entity**, children included — the read side has no borders, so a part is
queryable on its own even though it is only writable through its root.

**A read face is generated into the assembly that DECLARES the aggregate**, the same as the form model and
the writes. Your own aggregates have one, and so do the aggregates a Rask package declares —
`Session.Where(…)` and `Passkey.Where(…)` are [Rask.Auth's](authentication.md). A package can ask for the read faces
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
| *another aggregate's* `Guid OrderId`, on `Invoice` | `IReadOnlyList<InvoiceRead> Invoices` — the rows that point here, [below](#the-rows-that-point-here) |
| `bool IsShipped => …` (computed) | *dropped* — no column behind it |
| `[NotMapped] string Display` | *dropped* |
| every method, every domain event | *dropped* — there is nothing here to change or save |

**Member names are C#-shaped; the columns are untouched.** `PriceAmount` maps to the existing `Price_Amount`
column, so no migration is involved in any of this.

**The navigations are inferred from the ids**, which is what gives the read side its freedom: you write the
DDD-correct id on the aggregate and the join appears on the read face, having declared nothing. A relationship you
*did* declare in `Configure` is followed instead — [configured relationships win](#configured-relationships-win).

| Property on the aggregate | Target | Navigation |
|---|---|---|
| `Guid CustomerId` + a `Customer : Aggregate<Guid>` | exact name match | `Customer` |
| `Guid? ShippedByUserId` + a `User : Aggregate<Guid>` | name *ends with* an aggregate's name | `ShippedByUser` (nullable) |
| `int? SumSupplyId` on `Supply` itself | name *ends with* its own aggregate's name | `SumSupply` (nullable) — a join to another row of the same table |
| `Guid PistolId` + a `Pistol : Entity<Guid>` held by another aggregate | a child, for a name no aggregate takes | `Pistol` — read side only; the write side still holds just the id |
| `string VirtualTankId` + a `VirtualTank` child of `Tank` | exact child name, though it also *ends with* the aggregate `Tank` | `VirtualTank` — an exact name beats a suffix |
| `Guid CustomerId`, no `Customer` aggregate or child | none | none — an ordinary `Guid` column |
| `int CustomerId` + a `Customer : Aggregate<Guid>` | name matches, key type does not | none, **and [RASK089](diagnostics.md#rask089)** |
| `Guid CustomerId` where two aggregates end in `Customer` | ambiguous | none, **and [RASK089](diagnostics.md#rask089)** |

The first rule that matches wins, in this order: an aggregate's **exact** name, a child's **exact** name, the
longest aggregate name it **ends with**, the longest child name it **ends with**.

The navigation is named after the **property**, not the target, so two references to the same aggregate never
collide. (`CategoryId` on `Category` — the exact name of its own type — stays a plain column: it reads as the row's own id.) And it is the one thing the write model cannot do, so this is the join you came for:

```csharp
await Order
    .Where(o => o.Customer.Country == "HU"
             && o.ShippedByUser!.Name == "ada"
             && o.TotalAmount > 100
             && o.Lines.Any(l => l.Product.Sku == "ANVIL"))
    .OrderByDescending(o => o.ShippedAt)
    .Select(o => new { o.Reference, Customer = o.Customer.Name, o.TotalAmount });
```

Four aggregates in one statement, from a write model that holds nothing but ids. An id may name another
aggregate's **child** too — `Fueling.Where(f => f.Pistol.TankId == tank)` reaches the pistol under its pump — and
the child is filtered by its root's tenant like any other read.

### Configured relationships win

A relationship your entity's own `Configure` declares is followed as written: the navigation points at the
`HasOne<T>()` you named, on the `HasForeignKey` you gave, and the id's name is never guessed at — the name rules above
apply only to an id nothing is configured on. (Laravel's `belongsTo(Category::class, 'fk')` and Rails'
`belongs_to :x, class_name:, foreign_key:` work the same way.)

```csharp
public sealed class Product : Aggregate<int, PerTenant>
{
    public int? ProductCategoryId { get; private set; }
    public int? ForeignTaxProductCategoryId { get; private set; }

    public static void Configure(EntityTypeBuilder<Product> b) =>
        b.HasOne<ForeignTaxCategory>().WithMany().HasForeignKey(p => p.ForeignTaxProductCategoryId);
}

public sealed partial class ProviderCountry : Entity<(int TransactionTypeId, Country)>
{
    public int TransactionTypeId { get; private set; }   // the legacy column the provider's id kept
    public string VatNumber { get; private set; } = "";

    public static void Configure(EntityTypeBuilder<ProviderCountry> b) =>
        b.HasOne<Provider>().WithMany().HasForeignKey(c => c.TransactionTypeId);
}
```

| Configured | Navigation | The rows that point here |
|---|---|---|
| `ForeignTaxProductCategoryId` → `ForeignTaxCategory` | `ProductRead.ForeignTaxProductCategory` (`ForeignTaxCategoryRead?`) — named by the id, which ends in the target's last word | `ForeignTaxCategoryRead.Products` |
| `ProductCategoryId`, nothing configured | `ProductRead.ProductCategory` — inferred, as before | `ProductCategoryRead.Products` |
| `TransactionTypeId` → `Provider` | `ProviderCountryRead.Provider` — named by the target, the id says nothing of it | `ProviderRead.ProviderCountries` |
| `PayeeId` and `AuditorId` → `Payee` | `Payee` and `Auditor` — an id that names itself keeps its name, and a second one to the same target is named by its id | `PayeeRead.Payrolls`, `PayeeRead.PayrollsAuditor` |

```csharp
await Provider.Where(p => p.Id == id)
    .Select(p => p.ProviderCountries.Where(c => c.CountryId == hungary).Select(c => c.VatNumber).First());
```

A relationship configured from the other end — `b.HasMany<ProviderCountry>().WithOne().HasForeignKey(…)` in
`Provider.Configure` — counts the same. One joined on a `HasPrincipalKey`, or on several columns, gets no navigation
(the read face joins on the target's key) and no guess either. When every name a configured navigation could take is
already a column, a collection or another relationship's navigation, the build stops with
[RASK112](diagnostics.md#rask112). Only `Configure` is read: a relationship set up in `OnModelCreating` or an
`IEntityTypeConfiguration` is not seen, and its id falls back to the name rules.

### The rows that point here

Every navigation the read face infers also works backwards: the **target** gets a collection of the rows that point
at it. A distribution moves fuel from one fueling to another, and holds nothing but their ids:

```csharp
public sealed class Distribution : Aggregate<string, Deletable>
{
    public string FromFuelingId { get; private set; } = "";
    public string ToFuelingId { get; private set; } = "";
    public decimal Quantity { get; private set; }
}
```

`DistributionRead` gets `FromFueling` and `ToFueling`, as above, and `FuelingRead` gets the other end of each:

```csharp
await Fueling
    .Where(f => !f.DistributionsFrom.Any())
    .OrderBy(f => f.Quantity + f.DistributionsTo.Sum(d => d.Quantity));
```

That is one statement: `NOT EXISTS` for the `Any`, a correlated `SUM` for the sort. The same goes for `Count`,
`Select` and a `GroupBy` whose rows the collection filters, on SQLite, SQL Server and PostgreSQL alike.

| Id on the referencing entity | Collection on the target's read face |
|---|---|
| `Distribution.FromFuelingId` | `FuelingRead.DistributionsFrom` — the plural of the referencing type, then the id's words before the target's name |
| `Distribution.ToFuelingId` | `FuelingRead.DistributionsTo` — two ids to one target are two collections |
| `Distribution.FuelingId` | `FuelingRead.Distributions` — nothing before the name, nothing after the plural |
| `SupplyPart.VirtualTankId`, a child of `Tank` | `VirtualTankRead.SupplyParts` — a child target gets one too, by the same rules |
| `ReportColumnTotal.ReportColumnColumnId`, configured to `CatalogColumn` | `CatalogColumnRead.ReportColumnTotals` — named by the id, which ends in the target's last word, so nothing is added |
| `ReportColumnTotal.TotalColumnId`, configured to `CatalogColumn` too | `CatalogColumnRead.ReportColumnTotalsTotal` — the name above is taken, so it adds its own words |

**Two ids that would give one target the same name** are told apart rather than dropped: the first in source order
(by type, then by `Configure`'s order) keeps it, and each other one adds its own words — those before the words it
shares with the target's name (`TotalColumn` and `CatalogColumn` share `Column`, leaving `Total`), or its id without
`Id` when it shares all of them (`TransactionTypeId` to a `Provider`, named `Provider`, adds `TransactionType`).
A name of EF's own, `WithMany("TotalOf")`, cannot be given instead: EF refuses a navigation with no CLR property
behind it, and an aggregate holds ids, not navigations.

The collection is an `IReadOnlyList<DistributionRead>`, and it exists exactly when the navigation it mirrors does,
inferred by the same rules. **Its rows go through their own filters**: a soft-deleted distribution is not in it, and
when the referencing aggregate is `PerTenant`, only the current tenant's rows are, even if the target is shared by
every tenant. The target keeps its own filters too, so another tenant's fueling and the rows pointing at it never
appear. A name the target's face already uses — a column, a navigation, a child collection — or one that two ids
would both claim with no words of their own to tell them apart gets no collection and a
[RASK108](diagnostics.md#rask108) warning;
the forward navigation is still there. `Include(f => f.DistributionsTo)` loads one with each row.

### By id: a row, or the aggregate

To *show* one, it is the narrowest query; to *change* one, `Find` it:

```csharp
ProductRead? row = await Product.Where(p => p.Id == id).FirstOrDefault();
Product? product = await Product.Find(id);   // whole, children loaded — see "Find, change, Save"
```

`Find` is a filtered query by key, not EF Core's `Find` — which **bypasses query filters** and would return a
soft-deleted row that `Where` hides. Both spellings agree: a soft-deleted or another tenant's row is `null`.

**Every read is untracked, and every read opens and disposes its own context.** Composing holds nothing
open: the terminal call opens a context, runs, and disposes it before it returns — so this is a complete
statement anywhere, including a component's `OnMount`:

```csharp
protected override async Task OnMount() =>
    _products = await Product.Where(p => p.Active).OrderBy(p => p.Name);
```

That is the shape a live page needs, not a default to tune. A Rask page lives as long as the browser
keeps its socket open, and a `DbContext` is neither thread-safe nor meant to accumulate a session's worth
of entities — so nothing here holds one between calls, and there is no `AsTracking()` to ask for one.
Most reads are rendered and never written back anyway, and tracking them would cost a graph walk and an
identity-map entry to buy nothing.

**The consequence is now in the type system.** A `ProductRead` has no behaviour and is not in the write
context, so there is nothing on it to change and nothing that could save it. A change goes back through
[`Find` and `Save`](#find-change-save), [a write on the type](#writing-create-update-delete) or
[a context](#writing-plain-ef-core), and each loads the aggregate it is about to change.

For a shape this does not wrap — a group-by, a join, an aggregate — `Query` hands you the live
`IQueryable` inside a managed context:

```csharp
var byMonth = await Product.Query((q, ct) =>
    q.GroupBy(p => p.CreatedAt.Month)
     .Select(g => new { Month = g.Key, Total = g.Sum(p => p.PriceAmount) })
     .ToListAsync(ct));
```

### A reusable query: a scope

A filter several pages share — Rails' `scope :in_stock`, Laravel's `#[Scope]` — is a static on the aggregate
that returns the query, unrun. `Product.Where(…)` returns a `ModelQuery<ProductRead>`, so that is the type:

```csharp
public sealed class Product : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
{
    public string Name { get; private set; } = "";
    public int Stock { get; private set; }

    public static ModelQuery<ProductRead> InStock => Product.Where(p => p.Stock > 0);

    public static ModelQuery<ProductRead> NamedLike(string prefix) => Product.Where(p => p.Name.StartsWith(prefix));
}
```

It composes like any other query, and nothing runs until it is awaited:

```csharp
await Product.InStock.OrderBy(p => p.Name);
await Product.InStock.Where(p => p.Name != "Anvil").Take(10);
await Product.NamedLike("An").Count();
```

Call it through the type — `Product.Where(…)`, not a bare `Where(…)` — and one scope can start from another:
`InStock.Where(…)`. Every operator returns a new query, so a scope held in a static is never changed by
the caller who composes on it. It is a read: the rows are `ProductRead`, as for any other query.

### Handing a query to a component: `AsQueryable()`

A component that composes its own LINQ — sorting with `OrderBy`, paging with `Skip`/`Take`, counting with
`Count()` — wants a standard `IQueryable<T>`, not a query somebody has to run. `Product.AsQueryable()` is
that, and it holds no context either: **each time it is executed, a context is opened for that one execution
and disposed after it.** So it is safe to keep in a field for as long as the page lives:

```csharp
[Route("/products")]
public sealed partial class ProductsPage : Component
{
    private readonly IQueryable<ProductRead> _products = Product.Where(p => p.PriceAmount > 0).AsQueryable();
    private int _page = 1;

    protected override Component Render() =>
        Ui.Table.Paginate(Ui.Pagination
            .Paginator(new UiPaginator { Page = _page, PerPage = 25, Total = _products.Count() })
            .OnPage(page => { _page = page; }))[
            Ui.TableColumns[Ui.TableColumn["Product"], Ui.TableColumn.End["Price"]],
            Ui.TableRows[
                _products.OrderBy(p => p.Name).Skip((_page - 1) * 25).Take(25).ToList().Select(p => Ui.TableRow.Key(p.Id)[
                    Ui.TableCell[p.Name],
                    Ui.TableCell.End[$"{p.PriceAmount:C}"]
                ])
            ]
        ];
}
```

A list spanning two aggregates is possible here, because the read face carries the navigation:
`OrderBy(o => o.Customer.Name)` needs nothing declared.

The sort becomes `ORDER BY` and the page becomes `LIMIT`/`OFFSET`, in the database — the table never reaches
memory, however large it is. A synchronous `Count()` and an awaited `ToListAsync()` both work against it;
each is its own short-lived context. The [tutorial's product list](tutorial/02-first-feature.md#5-the-list-page)
does the same through a query, so the read stays off the render and a write refreshes it.

**EF Core's own operators go on first.** `Include`, `IgnoreQueryFilters` and `AsSplitQuery` are EF Core
extension methods, and EF applies them only to its own query provider — called on the queryable
`AsQueryable()` returns, they do nothing, silently. Put them on the model query, before the hand-off:

```csharp
Product.Include(p => p.Reviews).AsQueryable();        // ✓ travels with the queryable
Product.IgnoreQueryFilters().AsQueryable();           // ✓ soft-deleted rows too

Product.AsQueryable().Include(p => p.Reviews);        // ✗ compiles, loads no reviews
```

## Writing: create, update, delete

The writes live on the aggregate type, beside the reads, and a create reads like the update it pairs with. The
update just names the row first:

| Create | Update | From |
| --- | --- | --- |
| `Product.Create(model)` | `Product.Update(id, model)` | a form: the generated `ProductModel` |
| `Product.Create(model, p => …)` | `Product.Update(id, model, p => …)` | a form, plus values it does not carry |
| `Product.Create(name, price)` (its factory) + `Save()` | `Product.Update(id, p => …)` | code, with no form behind it |

- **A create** builds an empty `Product`, applies the model and then the lambda, and inserts it. The key is a new
  version-7 `Guid` (or the store's identity for an integer key). The lambda is for what a form must not carry —
  the signed-in user, a parent from the route, a domain event: `Ticket.Create(model, t => t.Open(Current.UserId))`.
- **From code, an aggregate is born by its own factory** — so its invariants hold from the first moment — and
  `Save()` inserts it. There is no `Create(p => …)`: an empty aggregate put together afterwards is the shape a
  factory exists to prevent, and `new Product()` outside `Product` is [RASK107](diagnostics.md#rask107).
- **`Product.Create(id, model)`** does the same under a key you give. They
  are the only creates for a key nothing can produce, such as a strongly-typed id over a `string`. They are not
  generated for an integer key the database produces: an explicit identity value fails on SQL Server and leaves
  PostgreSQL's sequence behind.
- **An integer key your application assigns** is a key you give, too. Say so in the aggregate's own `Configure`
  — `b.Property(x => x.Id).ValueGeneratedNever();` — and the aggregate gets `Create(id, model)` **instead of**
  the create that takes no key, which would insert the row under `0`:

  ```csharp
  public sealed class Destination : Aggregate<int, Model>
  {
      public string Name { get; private set; } = "";

      public static void Configure(EntityTypeBuilder<Destination> b) =>
          b.Property(d => d.Id).ValueGeneratedNever();          // the table is numbered by the application
  }

  await Destination.Create(1000, model);                        // inserted under 1000
  await Destination.Create(1001, d => d.Rename("Baja"));
  await Destination.Create(model);                              // does not compile: there is no such create
  ```

  A key given as `0` is refused at the save, by name, like any key left at its default; a key that is taken is
  the database's own duplicate-key error (`DbUpdateException`). The line is read from the source, so it has to
  be written in the static `Configure` on the aggregate, as one chain from `Property(x => x.Id)` — or
  `Property("Id")`, `Property(nameof(Id))`. Written in a helper method, an `IEntityTypeConfiguration` or
  `OnModelCreating`, the key is still inserted as given, but the aggregate keeps the creates of a stored
  identity.
- **`Product.Create(entity)`** inserts an aggregate you built with its own factory:
  `Product.Create(Product.Create("Anvil", new(30m, "EUR")))`.
- **An update** loads the row, applies the model and then the lambda, and saves **only the columns that changed**.
- **`Product.Delete(id)`** loads the row and **removes** it — generated for an aggregate that names `Deletable`.
  One that names `SoftDelete` keeps the row instead: `DeletedAt` is stamped and every read stops seeing it. One
  that names neither has no `Delete`. A row that is still referenced is refused by the database —
  [with the sentence you declared](#what-deleting-a-referenced-row-says); a soft delete never asks it.

Each write is one unit of work for one aggregate, and each goes through EF Core's change tracker, so the
interceptors run exactly as for a hand-written save: `CreatedAt`/`UpdatedAt` are stamped and `Version` is bumped
where the aggregate names them, a delete becomes a stamp *for an aggregate that names `SoftDelete`*, and the aggregate's domain events are
published after the commit.

- **The id is always yours.** The model carries none; the row is addressed by the `id` you pass, never by anything
  a form posted.
- **Values that do not come from the form** go in an optional lambda, which runs after the model's values are
  written, so it has the last word: `Product.Create(model, p => p.AssignTo(user.Id))`. With private setters
  the lambda calls the aggregate's methods, and so the rules in those methods run.
- **A stale edit is refused** on a `Versioned` aggregate. `Update(id, model)` checks the `Version` the model carries and throws
  `DbUpdateConcurrencyException` when someone saved since; a model whose `Version` is `null` skips the check.
  `Update(id, p => …)` and `Delete(id)` take an optional `version:` for the same check.
- **A missing row**, never created or soft-deleted, is `KeyNotFoundException`, naming the aggregate and the key.

A write refreshes what is on screen: once it commits, every `Rask.Query` query about that aggregate —
`QueryKey.For<Product>(…)` — refetches in the session that made it, so a list beside a create form updates
with no invalidation to write. See [Writes refresh queries by themselves](query.md#writes-refresh-queries-by-themselves).

A create needs the parameterless constructor an aggregate has when it declares none. One that declares a
constructor with arguments still gets `Update` and `Delete`, and the build says why it has no
`Create` ([RASK086](diagnostics.md#rask086)).

### Find, change, Save

When the change is the aggregate's own method, load it, call it, save it:

```csharp
var order = await Order.Find(id) ?? throw new KeyNotFoundException();
order.Ship("1Z999");
await order.Save();

await Order.Place("A-1001").Save();   // a new aggregate is inserted
```

It works the way Rails' and Laravel's models do: `Find` reads the row once and remembers what it read, and `Save()`
writes what changed since — it never reads the row again.

- **Read once.** `Find` loads the aggregate whole, through a context of its own, and hands it back detached with a
  snapshot of what it read. `Save()` compares with that snapshot and writes only the columns you changed — a column
  someone else changed meanwhile keeps their value — with no SELECT, however large the row. Every successful save
  takes a new snapshot, so the next `Save()` reads nothing either.
- **New is inserted with no read.** An aggregate your code made (`Order.Place(…)`, `new() { Id = Guid.CreateVersion7() }`)
  and never saved is an INSERT. So an aggregate built — or deserialised — with the key of an EXISTING row is an
  insert too, refused by the key: `Find` it and change it instead.
- **Children are synced by id.** A child you added is inserted, one you removed is deleted, one you changed is
  updated.
- **Changed underneath is refused, never written over.** The `Version` compared is the one `Find` read, so on a
  `Versioned` aggregate a save after someone else's — or after a soft delete, which bumps it — is
  `DbUpdateConcurrencyException`. So is a save whose row, or a changed child's, has been deleted outright: nothing is
  silently re-inserted. An aggregate with no `Version` that names `SoftDelete` is asked first whether its row is
  still there (by its key alone — one small SELECT), and is `KeyNotFoundException` when it is not.
- **The interceptors run** as for any write: times stamped, `Version` bumped, tenant checked, domain events
  published after the commit.
- **Nothing is held open.** `Find` takes no `db:` and returns the aggregate untracked, so it is safe to keep in a
  page field between `Find` and `Save`. Pass `db:` to `Save` to stage it in your own unit of work; its snapshot is
  taken when that context's `SaveChangesAsync` succeeds.
- **Delete what you found: `await file.Delete();`** — `var file = await QueuedFile.Find(id); if (file.Expired) await
  file.Delete();`. Like Rails' `record.destroy` and Laravel's `$model->delete()` it works from what `Find` read, with no
  read first: an aggregate that names `SoftDelete` is stamped, any other has its row and its children removed. The
  `Version` read is compared, so a row changed or deleted since is `DbUpdateConcurrencyException` (an unversioned
  soft-deleting one is asked by its key first, `KeyNotFoundException` when gone); one never saved is
  `InvalidOperationException`. It takes `db:` like `Save`, and inside `Db.Transaction` it is written in the
  transaction — a rollback leaves the row and its children, and its events go out after the commit. The static
  `QueuedFile.Delete(id)` stays, and loads the row first.
- **Only to show it? Do not `Find` it.** A list, a detail page or a check reads the
  [read face](#reading-the-read-face) (`Order.Where(…)`); an edit form reads `Order.Model(id)`. `Find` is for changing
  the aggregate through its own methods.

`Order.Update(id, o => o.Ship("1Z999"))` does the same in one call when there is nothing to show in between.

### Changing two aggregates together: `db:`

**No `db:` means the write owns its unit of work; `db:` means you do.** Without a context, a write opens one,
saves and disposes it. Pass `db:` and it **stages** its change in that context and returns — you save:

```csharp
await using var db = await contexts.CreateDbContextAsync(ct);

var order = await Order.Create(orderModel, db: db);                  // staged
await StockItem.Update(stockId, s => s.Reserve(quantity), db: db);   // staged

await db.SaveChangesAsync(ct);   // both rows, or neither
```

The writes on the type need no token — they are cancelled with the work they run in. EF Core's own calls,
`CreateDbContextAsync` and `SaveChangesAsync`, do not look for that, so they are handed one.

There is no `BeginTransactionAsync` here, and there should not be: **one `SaveChangesAsync` is already one
transaction.** Several writes staged on one context commit together by construction.

This is also why a Rask app can change two aggregates at once without eventual consistency. Having had to
load each root by its own id, the write is deliberate — and a local transaction is a better answer than a
message for an app on one box. [Domain events](#keeping-state-inside-the-aggregate) remain there for when
eventual consistency is genuinely what you want.

Two things to know:

- **A staged `Create` returns an entity that is not yet persisted.** A `Guid` key is already set (the
  factory assigned it); a store-generated `int` key is `0` until you save.
- **The concurrency check still works and is still automatic.** `Update(id, model)` pins the original
  `Version` from `model.Version`, and EF compares it at *your* save.

A row that context already tracks is the one updated, not a second copy.

### A check and the writes it guards: `Db.Transaction`

`db:` stages writes; it cannot put a **read** in the same transaction, and a read face (`Pistol.Count`,
`Tank.Any`) always opens a context of its own. A check made that way runs outside the save it guards, so two
requests can both see "free" and both take it. `Db.Transaction` closes that gap — Laravel's `DB::transaction`,
Rails' `transaction(isolation:)`:

```csharp
await Db.Transaction(async () =>
{
    if (await Pistol.Count(p => ids.Contains(p.Id)) != ids.Count) return;   // runs in the same transaction
    await Tank.Update(id, t => t.Change(...));                              // joins it, with no db:
});                                                                         // provider default isolation

await Db.Transaction(async () => { ... }, isolation: IsolationLevel.Serializable);

var order = await Db.Transaction(async () => await Order.Create(model));    // hands back a value
```

- **Everything without a `db:` joins.** `Find`, `Where`, `Any`, `Count`, the read faces, `Create`, `Update`,
  `Delete`, `Save`, child writes and the unique/store-rule checks each still open a context — on the
  transaction's connection, enlisted in it.
- **A `db:` you pass joins too**, or is refused — it never commits on its own beside the work. On the same
  connection string, and not opened before it was handed in, it is enlisted (so `db.SaveChangesAsync()` writes
  inside the transaction); on another database, in a transaction of its own, or with its connection already
  open, the call throws `InvalidOperationException`. The same goes for `db.BulkInsert(...)`. It stays yours:
  when the transaction ends — committed, rolled back or retried — it is out of it and back on its own connection,
  so the same `db` can go into the next `Db.Transaction` or save on its own.
- **Your own context joins at its first command.** A context of the app's own — from the factory `AddRaskData<T>(o => …)`
  registers or any whose options carry Rask's interceptors (`AddInterceptors(sp.GetServices<ISaveChangesInterceptor>())`,
  which a `RaskApp` and every documented hand-wired host already add) — is enlisted before the first thing it runs
  inside the block, even when it was created before it: a LINQ read, `SaveChangesAsync`, `ExecuteSqlRawAsync`,
  `SqlQueryRaw`, `FromSqlRaw`. So its save does not wait on the locks the transaction's own reads took, a rollback
  takes its writes back, and a later `db: db` call finds it joined. It is handed back on its own connection like
  any `db:`. No `db.Database.BeginTransactionAsync()` inside the block — the context is already in one, and EF Core
  refuses a second:

  ```csharp
  await using var db = await contexts.CreateDbContextAsync();
  await Db.Transaction(async () =>
  {
      var fueling = await db.Set<Fueling>().SingleAsync(f => f.Id == id);  // joins here
      fueling.Annotate(note);
      if (await Fueling.Count(f => f.CardId == fueling.CardId) > 1) return; // read face, same transaction
      await db.Database.ExecuteSqlRawAsync("UPDATE Cards SET Touched = 1 WHERE Id = {0}", fueling.CardId);
      await db.SaveChangesAsync();                                          // same transaction
  }, isolation: IsolationLevel.Serializable);
  ```

  A context on another database, already in a transaction of its own, or whose connection you opened before the
  block runs as it always did, outside — and a `db:` call with it is refused as above.
  A context that joins as it opens still has the open reported to its connection interceptors, on the transaction's
  connection. Rask's SQL Server and SQLite interceptors send nothing then — that connection got its `SET`s and
  pragmas when it opened. A `ConnectionOpened` interceptor of your own that sets a connection up should skip it the
  same way: `if (eventData.Context?.Database.CurrentTransaction is not null) return;`.
- **A job or a mail queued inside is a row in the transaction.** `Jobs.Enqueue(...)` (with or without
  `.UnlessWaiting()`) and `Mail.Send(...)` write on its connection: no processor sees them before the commit,
  and a rollback leaves nothing to run or deliver. Their table must be in the same database; otherwise the
  enqueue throws rather than write outside.
- **Committed when the work completes; rolled back when it throws**, and the exception reaches you unchanged.
- **After the commit, never before:** domain events are published, the outbox is woken (its rows were written
  inside, so they commit or roll back with the work), and the session's queries are told what changed.
  A rollback announces nothing.
- **`isolation` is optional.** Omitted, none is sent and the database's default applies: read committed on SQL
  Server and PostgreSQL. SQLite is serializable and has one writer — a second `Db.Transaction` waits at its
  `BEGIN` for the first to commit, then sees what it wrote.
- **Serializable is what makes a check-then-write safe** on SQL Server and PostgreSQL. Two such transactions
  that both read the same rows and then write cannot both commit: SQL Server picks a deadlock victim (1205),
  PostgreSQL refuses one with a serialization failure (40001). Both are transient, so with retrying on (the
  default for both providers) the loser runs again from the top and sees the winner's row; with retrying off
  it throws.
- **On a retrying context the whole work may run again**, so keep side effects outside the database (a mail,
  an HTTP call) out of it — or put them in a [durable handler](outbox.md).
- **Nested `Db.Transaction` joins the outer one.** Same connection, same transaction, committed by the
  outermost. It is not a savepoint, and asking for a different `isolation` inside throws.
- **One connection, one thing at a time:** no `Task.WhenAll` over reads inside it.
- **What does not join:** the cache does not take part — `Cache.Remember` and `Cache.Forget` act at once and
  are not undone by a rollback. Nor does a context built without Rask's interceptors and never handed to a Rask call.

It works the same from a live event handler, an HTTP endpoint, a job and a test, on a `RaskApp` host and on
one wired by hand with `AddRaskData<TContext>()`.

### Named sets: `db.Orders`

Every mapped entity also gets an accessor on `DbContext`, beside the `Set<T>()` that always worked:

```csharp
await using var db = await contexts.CreateDbContextAsync(ct);

var order = await db.Orders.FindAsync([id], ct);   // children come with it
if (order!.TotalAmount > limit) { order.Hold(); }

await db.SaveChangesAsync(ct);
```

`db.Orders`, `db.OrderLines` — children too, since they are in the write context, but a save that writes a child
it reached that way [without its root is refused](#children). These are extension members on `DbContext` itself, so an app that brings its
own context gets them without the context having to be `partial`.

The name comes from one documented rule — `s`, `es` after `s`/`x`/`z`/`ch`/`sh`, and `y` → `ies` after a
consonant — and nothing cleverer, because an irregular guess is worse than a predictable one. A name two
entities want, or one `DbContext` already declares, is [RASK090](diagnostics.md#rask090) rather than a silent
rename.

### A create and an edit form

A form edits something mutable and an aggregate is not, so an aggregate that names `Model` gets a **form model
generated beside it**: `ProductModel` for `Product`. Forms always bind the model, never the aggregate. It is a plain class with a
settable copy of every mapped property, and:

- **every property is nullable**, because a form field can be empty whatever the column is — and a column that
  cannot be empty (`decimal Price`, `string Name`) is **required by its type**: leaving it unfilled is a
  validation error (*This field is required.*, in the reader's language) rather than a silent 0. No `[Required]`
  is generated for it — see [what a form over the model asks by itself](#what-a-form-over-the-model-asks-by-itself).
  A control bound to it says `aria-required="true"`: the type's registration answers
  [the one question a control asks](forms-validation.md#how-a-control-knows-a-field-is-required);
- **`new ProductModel()` holds the aggregate's defaults**: it is filled from an empty `Product`, so a create form
  starts from the same values a `Product` would;
- **`product.ToModel()`** copies a row into a model for an edit form, `Version` included;
- **the aggregate's validation attributes are copied onto it**, so `Form.Model(…)` checks input by the aggregate's
  own rules in the form, and EF Core reads the same attributes for the column;
- **`Product.Create(model)` and `Product.Update(id, model)` check them again on the server** — the value
  objects' rules first, then the attributes — before the database is asked anything, and throw
  `RaskValidationException` with nothing written. Its `Failures` name each field as the form names it (`Name`,
  `Price.Amount`, `Lines[2].Sku`), so a form shows them under the field and an API caller gets the 400 with
  `errors`. A posted form cannot skip the rules, and neither can a call with no form behind it.

A create page starts from a new one:

```csharp
[Route("/products/new")]
public sealed partial class NewProductPage : Component
{
    private readonly ProductModel _product = new();

    protected override Component Render() =>
        Form.Model(_product).OnSubmit(Create)[submitting => [
            Ui.Input.Bind(() => _product.Name).Label("Name"),
            Ui.Input.Bind(() => _product.Price!.Amount).Label("Price"),
            Ui.Button.Submit.Disabled(submitting)["Create"],
        ]];

    private async Task Create(ProductModel product)
    {
        await Product.Create(product);
        Routes.ProductsPage().Go();
    }
}
```

An edit page fills one from the row it read, and hands it back with the route's id:

```csharp
[Route("/products/{id:guid}/edit")]
public sealed partial class EditProductPage : Component
{
    [RouteParam] public Guid Id { get; set; }

    private ProductModel? _product;
    private string? _conflict;

    protected override async Task OnMount() =>
        _product = await Product.Model(Id);

    protected override Component? Render() =>
        _product is null ? P["Loading…"] :
        Form.Model(_product).OnSubmit(Save)[
            _conflict is null ? null : Ui.Callout.Warning.Role("alert").Text(_conflict),
            Ui.Input.Bind(() => _product.Name).Label("Name"),
            Ui.Input.Bind(() => _product.Price!.Amount).Label("Price"),
            Ui.Textarea.Bind(() => _product.Notes).Label("Notes"),
            Ui.Button.Submit["Save"],
        ];

    private async Task Save(ProductModel edit)
    {
        try
        {
            await Product.Update(Id, edit);        // Version checked
            Routes.ProductsPage().Go();
        }
        catch (DbUpdateConcurrencyException)
        {
            _conflict = "Someone saved this product while you were editing it.";
        }
    }
}
```

The list those pages link from is a [table](ui-kit.md#tables) over one page of `Product` at a time
([above](#handing-a-query-to-a-component-asqueryable)): the rows are read faces, read-only by construction, and
each links to its edit page.

### Choosing what a table carries

A table carries only what its base names. The type arguments after the key are **traits**, in any order, and an
aggregate that names none — `Aggregate<Guid>` — has no timestamps, no `Version`, no form model and no generated
`Delete`: its own columns, its read face, `X.Update(id, x => …)`, and [`Find` and `Save`](#find-change-save).

| trait | what the table gets |
|---|---|
| `Timestamps` — or `Created` / `Updated` for one | `CreatedAt` and `UpdatedAt`, stamped on save |
| `Versioned` | a `Version` concurrency token: a save from a stale copy is refused |
| `Model` — or `CreateModel` / `UpdateModel` for half | the form model, `X.Create(model)` and `X.Update(id, model)` — [below](#turning-the-form-surface-off) |
| `Deletable` | a generated `X.Delete(id)` that removes the row |
| `SoftDelete` | `DeletedAt` and a filter; the generated `Delete` stamps it instead (it implies `Deletable`) |
| `PerTenant` — or `PerTenant<int>` / `PerTenant<long>` | a `TenantId`, a filter and a stamp — see [Multi-tenancy](multi-tenancy.md) |
| `Broadcast` | a save announced to the whole process — see [Announcing a save](#announcing-a-save) |

With neither `Deletable` nor `SoftDelete` there is no `Delete` at all — calling it is a compile error, which is
what a [behaviour-rich aggregate](#behaviour-rich-aggregates) wants.

```csharp
public sealed class Product : Aggregate<Guid, Timestamps, Versioned, Model, Deletable> { }    // what rask new scaffolds
public sealed class Order : Aggregate<Guid, Timestamps, Versioned, Model, SoftDelete> { }     // keep the row when deleted
public sealed class Invoice : Aggregate<Guid, Timestamps, Versioned> { }   // behaviour-rich: no model, no Delete
public sealed class Reading : Aggregate<Guid, Created> { }                 // append-only: no UpdatedAt, no Version
```

What the table does not carry, the generated types do not either. A `Reading` like the one above has no
`Version`, and its read face has `CreatedAt` and nothing else of the framework's:
`Reading.OrderBy(r => r.UpdatedAt)` is a compile error, not a query that fails. `DeletedAt` is on a read face
only for an aggregate that names `SoftDelete`.

A child `Entity` takes only a timestamp trait — `OrderLine : Entity<Guid, Timestamps>`. Tenancy, the version, the
delete and the form model are the aggregate's, so naming one on an entity is a compile error (CS0311).

> **Upgrading from the consts.** An aggregate that said nothing used to get `Timestamps`, `Versioned`, `Model` and
> `Deletable`. Name them on its base — `Aggregate<Guid, Timestamps, Versioned, Model, Deletable>` — or its next
> migration drops `CreatedAt`, `UpdatedAt` and `Version`, and its form writes stop compiling. An old const
> (`Writes`, `Stamps`, `Deletes`, `Checks`, `Scope`, `Broadcast`) no longer compiles: its enum is gone.
> `Deletes = Deletion.Soft` is `SoftDelete`, `Scope = Tenancy.PerTenant` is `PerTenant`,
> `Broadcast = Broadcasts.OnCommit` is `Broadcast`, and `Deletes = Deletion.None` is leaving both deletes off.

### Announcing a save

A save already refreshes the screen of the session that made it — that is `IDataChanges`, and it costs nothing and
needs no declaration. What it deliberately does not do is reach anyone else's screen, because another session's cache
is not this save's to touch.

`Broadcast` is that other half:

```csharp
public sealed class Order : Aggregate<Guid, Timestamps, Versioned, Broadcast> { }
```

The save is now announced to the whole process once it commits, and every query that reads orders refetches — a
read face keyed `QueryKey.For<Order>(…)`, or a message query carrying `[Live(typeof(Order))]`. Nothing is said in the
component; see [live queries](query.md#staying-fresh). Nothing is published unless a page is listening, and the
page's own query decides who may see the refresh.

Three things worth knowing:

- **After the commit, never on a rollback** — the same rule the interceptors already follow.
- **It needs no session.** A background job's write announces itself; `IDataChanges` could not, having no session's
  cache to invalidate. That is the gap this closes.
- **It is off unless named**, so nothing is published for a table no page is watching.

Like every subscription in Rask today, it reaches the process it was published in. Behind a load balancer, a page on
the other server catches up on its next refetch.

**Why a type argument and not an attribute.** The base is part of the type, so the generator reads it at
compile time and can never quietly miss it, and a trait in the wrong place is a compile error at the
declaration. Nothing is reflected over at run time, so a trimmed publish has the same shape as a debug build.

**The traits are the whole family.** Anything per-property or expression-shaped — a column name, an index, a value
converter, a length — stays in [`static Configure`](#mapping-rules-the-conventions-dont-cover), which is where
it can actually be expressed.

#### Why the scaffold checks the version and soft-deletes nothing

Both are opt-in, and `rask new` names `Versioned` and `Deletable`. They look symmetrical and are not.

Soft delete used to be the default and is not any more, because it costs an ordinary application more than the
recovery is worth. A stamped row still occupies its UNIQUE constraints, so deleting the account `a@b.com` and
letting that person sign up again fails on a row nobody can see. "Delete my account" has to be able to mean
delete. And Rask already ships real recovery — [SQLite snapshots and Litestream](sqlite.md) — so keeping the
row was solving that problem a second time, worse, while charging every query a predicate.

The version check is scaffolded because **a lost delete is visible and a lost update is not**. A deleted row is
gone and somebody notices; two people editing one form both see "Saved" and the second silently erases the
first. The cost that would normally argue for turning it off — an unhandled `DbUpdateConcurrencyException` —
Rask already absorbs: the scaffolded edit page catches it and says somebody else changed the row.

#### An entity that knows its own time

`CreatedAt` and `UpdatedAt` — on a table that names `Timestamps` — are normally the framework's, stamped by
Rask's auditing interceptor on save. An
entity in a package that does **not** own the `DbContext` cannot rely on that, and a column nobody stamped
reads as `0001-01-01` — which is not obviously wrong anywhere it is used. Rask's own file store found this the
hard way: an unstamped `CreatedAt` became a `Last-Modified` header and the cutoff of an orphan sweep.

So `Entity<TId>` has a `protected void Stamp(DateTime at)`, and the interceptor fills only what is still
unset. An entity that knows its own creation time says so; one that does not gets the framework's; neither can
end up at `0001-01-01`.

### Turning the form surface off

Not every aggregate should be creatable from a form. A passkey is minted by a WebAuthn ceremony; a session
is started by signing in. The form surface is a trait, so an aggregate that leaves `Model` off has none:

```csharp
public sealed class Passkey : Aggregate<Guid, Timestamps, Versioned, Deletable>
{
    public string Name { get; private set; } = "";

    public void Rename(string name) => Name = name;
}
```

| The base names | What is generated |
|---|---|
| `Model` | `PasskeyModel`, `Create(model)`, `Update(id, model)`, `Model(id)`, `ToModel()` |
| `CreateModel` | `PasskeyModel`, `Create(model)`, `Model(id)`, `ToModel()` |
| `UpdateModel` | `PasskeyModel`, `Update(id, model)`, `Model(id)`, `ToModel()` |
| none of them | no `PasskeyModel` at all, and nothing that takes one |

**It reaches the form surface and nothing else.** These take no model, so they are there either way:

```csharp
var passkey = Passkey.For(me, "laptop"); await passkey.Save();   // its factory, then Save — takes no model
await Passkey.Update(id, p => p.Rename("desktop"));       // behaviour
await Passkey.Delete(id);                                 // never took a model (there under Deletable or SoftDelete)
await Passkey.Where(p => p.UserId == me);   // the read face is not negotiable
```

The read face stays because querying works through read models: an aggregate that could switch its own off
would be an aggregate nothing can read.

A **child** takes its root's answer: its model exists to be an element of the root's list, so an `Entity` cannot
name `Model` at all — the compiler refuses it.

### Behaviour-rich aggregates

Create, update and delete fit data whose only rule is "the fields are valid" — a product, a category, a
setting. They do not fit an aggregate whose state moves through steps that each have rules of their own. An
order is not *updated*: it is placed, paid, shipped or cancelled. An invoice is never deleted: it is voided,
and a mistake is corrected by a credit note. Nothing new is needed for these — the same writes, used this way:

```csharp
public enum OrderStatus { Placed, Paid, Shipped, Cancelled }

public sealed class Order : Aggregate<Guid, Timestamps, Versioned>   // no Model, no Deletable: methods only
{
    public string Reference { get; private set; } = "";
    public OrderStatus Status { get; private set; }
    public string? Tracking { get; private set; }

    public static Order Place(string reference) =>
        new() { Id = Guid.CreateVersion7(), Reference = reference, Status = OrderStatus.Placed };

    public void Pay() => Status = Status == OrderStatus.Placed
        ? OrderStatus.Paid
        : throw new InvalidOperationException($"A {Status} order cannot be paid.");

    public void Ship(string tracking)
    {
        if (Status != OrderStatus.Paid) throw new InvalidOperationException($"A {Status} order cannot ship.");
        (Status, Tracking) = (OrderStatus.Shipped, tracking);
    }

    public void Cancel() => Status = Status is OrderStatus.Placed or OrderStatus.Paid
        ? OrderStatus.Cancelled
        : throw new InvalidOperationException($"A {Status} order cannot be cancelled.");
}
```

```csharp
var order = await Order.Create(Order.Place("A-1001"));               // the factory decides what a new order is
await Order.Update(order.Id, o => o.Pay());                          // load whole, run the rule, save
await Order.Update(order.Id, o => o.Ship("1Z999"), version: v); // stale version throws
await Order.Update(order.Id, o => o.Cancel());
```

When a page shows the order between loading and changing it, the same rules run through
[`Find` and `Save`](#find-change-save): `var o = await Order.Find(id); o!.Pay(); await o.Save();`.

- **Only the methods are callable.** The properties have private setters, so `o => o.Status = OrderStatus.Shipped`
  is a compile error (CS0272) — the lambda can do what the aggregate's public surface allows and nothing else.
  Each call is one unit of work: the aggregate is loaded with its children, the method runs, the version is
  checked, and any events it [raises](#keeping-state-inside-the-aggregate) are saved in the same transaction.
- **No `Model` closes the form path.** A form update copies each field of the model onto the private setters
  directly, which is right for a product and wrong here: a posted form could set `Status` without going through
  `Ship`. Name `CreateModel` instead if a form may place an order but never edit one.
- **No `Deletable` means no `Delete`.** `Order.Delete(id)` is then a compile error, and
  the underlying `GeneratedModelWrites.Delete<Order>` throws. Retiring an order is `Cancel`, which keeps
  the row, its history and everything that refers to it.

### How a form save writes

**The model is the truth.** A save stores what the model holds, property by property, and reads nothing back
from the row to fill a gap:

| The model's value | The aggregate's property | What is saved |
| --- | --- | --- |
| a value | any | the value |
| `null` | declared nullable (`string? Notes`) | `null`: the user emptied the field |
| `null` | not nullable (`string Name`) | nothing: the write is refused, *This field is required.* under `Name` |
| a nested value-object model | a value object | the value object **built from the nested model alone**, by the same rules |
| `null` | a value object declared nullable (`Address? Billing`) | `null`: every column of it |
| a nested model **left wholly empty** | a value object declared nullable (`Address? Billing`) | the same `null`: it was not given, and nothing in it is asked for |
| `null` | a value object that is not nullable (`Money Price`) | nothing: the write is refused, under `Price` |
| `Version` is `null` | | no concurrency check |

The rules are the same one level down, and at every level below that. `Address` holds `string Street`,
`string? Floor` and `int? Door`; a reader who empties the floor has emptied it:

```csharp
var model = (await Customer.Model(id))!;      // Address: Street "1 Quay", Floor "2", Door 7
model.Address!.Floor = null;                  // what an emptied field binds
model.Address.Door = null;

await Customer.Update(id, model);             // Address_Floor NULL, Address_Door NULL, Address_Street untouched

model.Address.Street = null;
await Customer.Update(id, model);             // RaskValidationException: "Address.Street" — This field is required.
```

So a model from `new ProductModel()` or `product.ToModel()` saves exactly what the form shows, an update that
changes nothing writes nothing — text stored as `''` stays `''`, it is not turned into `NULL` — and a model is
never a patch: one built in code with only some members set clears the nullable rest and is refused for the
required rest. To change a few properties from code, `Product.Update(id, p => p.Reprice(price))` says so
directly. A member that is text and cannot be empty is refused when it holds `''` or white space as well, as a
root property is; declare it `string?` where the table really holds empty text.

The same holds for a create, which builds the value object from the model and nothing else: a member the model
leaves empty is not filled from the default the aggregate's initializer gave it.

**An optional value object left empty was not given.** A form over `Address? Billing` has to hold a nested model
to bind its fields to (`model.Billing ??= new()`), so a reader who fills none of them posts a model that is there
and empty. That is stored as no address — every column `NULL` — and none of its fields is asked for. Fill one
and it is an address: the rest of the rules apply, and a missing street is refused under `Billing.Street`.

```csharp
model.Billing = new();                                   // nothing typed
await Customer.Update(id, model);                        // Billing is null: every Billing_ column NULL

model.Billing = new() { City = "Szeged" };               // one field typed
await Customer.Update(id, model);                        // refused — "Billing.Street": This field is required.
```

*Empty* means exactly this, for every member of the nested model, at every level:

| The member's value | Counts as |
| --- | --- |
| `null` | empty |
| text that is `""` or only white space | empty |
| `false` | empty: a box nobody ticked says nothing |
| a nested model that is `null`, or itself wholly empty | empty |
| any other value — **including `0`** | filled |

Every member of a model is nullable (`bool?`, `int?`), so each has an empty state. A control the reader never
touched writes nothing and leaves `null`; a number field that is cleared goes back to `null`; a box that is
ticked and unticked again writes `false`, which is why `false` is empty too. `0` is a number somebody typed. A
value object that is **not** nullable is never read this way — its empty model is refused for each field it
lacks — and so is a row of a list, which is there because somebody added it.

**A save that changes nothing still writes nothing.** When the model is empty and what the row holds there is
empty by the same rule — every column `NULL`, blank text or `false` — the row is left exactly as it is: text
stored as `''` stays `''`. The columns are cleared only when something stored was a value.

**A form says "required" per place, not per type.** `Address Shipping` and `Address? Billing` are one nested
model type, and a control bound to `Shipping.Street` announces `aria-required` and refuses an empty street
before anything is saved, as ever. One bound to `Billing.Street` announces nothing and lets an empty street
through: a field's rule sees its own value and not the fields beside it, so it cannot tell an address nobody
gave from one that lacks a street. The write can, and says *This field is required.* under `Billing.Street`,
[where the form shows it](#a-create-and-an-edit-form). A rule the field's value object carries (`Zip.Validate`)
is still asked there about a value that was typed. Nothing is written for this: the generated model registers
which of its properties may be left empty
([`RaskValidation.RegisterOptionalFields`](forms-validation.md#how-a-control-knows-a-field-is-required)).

A form save writes properties through the aggregate's private setters, with generated `[UnsafeAccessor]`s and no
reflection. **It does not call the aggregate's methods**, so a rule that must hold for form input belongs in a
validation attribute on the aggregate (copied to the model), and a change that is a domain operation goes through
a method in the lambda.

**Or send it in a command.** A command, a query, an API endpoint or an island prop can carry a `ProductModel`
(`public sealed record AddProduct(ProductModel Product) : ICommand<Guid>`), and the generators that build their
codecs recognise it although it is itself generated; the handler calls `Product.Create(command.Product)`. The
model carries every mapped property, so input from outside your own pages is best a command of its own that says
what may change.

**Your own write wins.** A static `Create(ProductModel, …)` or `Delete(Guid, …)` declared on `Product`
replaces the generated one at every call site (a member on the type beats an extension member), and the
generated one stays reachable as `ProductModelExtensions.Create(…)` for an override that only adds to it.

### What the model carries

- **Every mapped property** of the aggregate, nullable, and its `Version` when it names `Versioned`.
- **Not** the `Id`, `CreatedAt`, `UpdatedAt`, `DeletedAt`, the domain events, navigations, collections or computed
  properties. Computed is read off the member: no setter, no backing field, and — for a collection — a getter that
  makes something of a list the aggregate already stores rather than handing it out. `OpenLines => _lines.Where(…)`
  beside `Lines => _lines`, or `SortedTags => _tags.Order().ToList()` beside `Tags => _tags`, is on neither the model
  nor the read face, and is not mapped; `Lines` and `Tags` stay, edited by the form as before.
- **A value object as a model of its own:** `Money Price` on `Product` is a nested `MoneyModel? Price` on `ProductModel`, filled by `new ProductModel()` and `ToModel()`, so a
  form binds `() => _product.Price!.Amount` like any [nested model](forms-advanced.md), however `Money` is declared.
  A one-value value object (`record Email(string Value)`) is carried as its value: `string? Email`.
- **Children keyed by several columns:** a link table as a list of ids (`List<int> ActivityIds`), a row with fields of
  its own as a list of rows (`List<CompanyColumnModel> Columns`) — see
  [Link tables and child rows on the form](#link-tables-and-child-rows-on-the-form).

### What a form over the model asks by itself

A form bound to an aggregate's generated model runs two kinds of rule with nothing written on the form:

```csharp
public sealed record DestinationName(string Value)
{
    public static IEnumerable<string> Validate(string value) =>       // found by its shape
        value.Trim().Length < 2 ? ["A name has at least two characters."] : [];
}

builder.HasIndex(d => new { d.Name, d.TenantId }).IsUnique("A destination with this name already exists.");

Form.Model(_destination).OnSubmit(d => Destination.Create(d))[   // _destination is a DestinationModel
    Ui.Input.Bind(() => _destination.Name),                      // both rules, no step
    Ui.Button.Submit["Save"]
]
```

**The value object's own rule.** A value object that holds ONE value and has a `public static Validate` taking
exactly that value and returning the messages — `IEnumerable<string>`, or a `ValueTask`/`Task` of it — is asked by
the field that holds it, before any `.Validate(…)` written on the field, which then runs only for a value it
accepted. There is no interface and no attribute. A `Validate` of any other shape is not a rule, and the build
says so ([RASK102](diagnostics.md#rask102)) rather than let it look like a rule that passed; one on a value
object of several values is left alone. Over a number, a date or an id, declare the parameter `decimal`, not
`decimal?` — RASK102 says so: an unfilled field is never put to the rule.

**A non-nullable value-object property is required: its rule is asked with the empty value.** So "required" is
said by the type, in its own words, and no attribute is needed:

```csharp
// before                                               // after
[Required(ErrorMessage = "A destination needs a name.")]
public DestinationName Name { get; private set; }       public DestinationName Name { get; private set; }

public static IEnumerable<string> Validate(string value)
{
    if (string.IsNullOrWhiteSpace(value)) yield return "A destination needs a name.";   // asked for an empty field too
    else if (value.Length > MaxLength) yield return $"A name is at most {MaxLength} characters.";
}
```

- a **text** value object is asked about `""` when the field holds nothing, and what it answers is shown. If it
  accepts `""`, an empty text field is stored as that; a field that holds nothing at all (`null`, from a caller
  with no form) still says *This field is required.*
- a value object over a **number, a date or an id** has no empty value to be asked about, so an unfilled field
  says the one sentence *This field is required.* and the rule is asked once there is a value
- a **nullable** property (`DestinationName? Alias`) is optional: an empty field is not put to the rule — and
  so is a one-value **struct** held as `T?` (`NormPercent? Discount`), whose field is the `decimal?` it holds
- a **plain** non-nullable property — `int Weight`, `decimal Price`, `DateOnly Built`, `Guid OwnerId`, `bool`,
  `string Plate` — and a value object with no rule are required the same way: an unfilled field says the one
  sentence. For text, unfilled means `null`, `""` or only white space; for everything else, no value
- a nullable plain property (`int? Axles`, `string? Notes`) is optional
- a `[Required(ErrorMessage = "…")]` you wrote on the aggregate yourself is kept, with its message, and is then
  the only thing that says it

A control bound to a field that is required this way says `aria-required="true"`
([how a control knows](forms-validation.md#how-a-control-knows-a-field-is-required)): each generated model type
— the root, a nested value-object model, a row — announces exactly the fields its write refuses unfilled. A
member of a value object of several values is such a field: `string Street` on `Address` is required under
`Address.Street`, `string? Floor` is optional, and the validation attributes the value object's own properties
carry are checked by the write under the same path. The exception is a field under a value object that
[may be left wholly empty](#how-a-form-save-writes) — `Address? Billing` — which a form announces nothing
about, while the same type held as `Address Shipping` is announced as ever.

The one sentence is the strings catalog's `ValidationRequired` — *This field is required.*, with the kit's
built-in translations — so it follows the reader's language, and
[a catalog of your own](localization.md) can reword it.

These rules are not attribute validation, so `RaskValidation.AutoValidate = false` does not switch them off, and
they are the same ones `Create(model)` and `Update(id, model)` ask with no form at all.

**The aggregate's unique rules.** **Write the `IsUnique("…")` in the aggregate's own `Configure`**: an index
declared in a helper method or an `IEntityTypeConfiguration` is enforced at save but not asked beforehand, and
so is one without a message. Every index `Configure` declares with a message is
asked of the database [when a bound field is committed and again on submit](forms-validation.md#the-database-said-no),
and the message shows under the field, or under each field of an index over several:

- over **exactly the index's columns**: one that names `TenantId` about the current tenant's rows — the tenant is
  the one in flight, never the form's to say, and with no tenant, or inside `Tenant.Across()`, the rule is left to
  the save — and one that does not about **every tenant's rows**, with a tenant or without one
  ([an index is what you write](multi-tenancy.md#an-index-is-what-you-write))
- an edit form does not collide with **the row it was filled from**: the model remembers its key from
  `Destination.Model(id)` / `ToModel()`. It is not a field — nothing binds or posts it — and it only decides
  which row this courtesy check leaves out; the save takes its id beside the model and is checked on its own
- **an unfilled field is not asked** — `null`, or a text field still `""` — so an index over two fields is
  asked once both hold a value, and once for the pair, not once per field. A genuine empty-string duplicate is
  caught by the save
- the same indexes are asked as [by the save on an adopted table](#when-the-index-may-not-be-there): an index
  filter other than its own columns being `NOT NULL` is skipped
- on every host that has Rask.Data, whoever created the schema

It is a courtesy, never the rule: two people can pass it at the same moment, and the save — the index itself,
or the declared rule asked again in the save's transaction — still refuses the second, shown the same way.

Both belong to the **generated** model. A model you write by hand gets neither — name the rule on the field
(`.Validate(DestinationName.Validate)`) and let the save report the index. An index declared somewhere other
than the aggregate's own `Configure`, and one on a row inside an aggregate, are enforced by the save and not
asked beforehand.

A `partial class ProductModel` of your own merges into the generated one, which is the way to add members,
`IValidatableObject` or display helpers. The build says when it cannot generate: a **hand-written, non-`partial`
`ProductModel`** beside a `Product` is an error ([RASK082](diagnostics.md#rask082)), and an aggregate **declared
inside another class** is a warning and gets no model ([RASK083](diagnostics.md#rask083)).

On the wire a model's properties are **camelCase**. Only validation attributes are copied from the aggregate, so a
`[JsonPropertyName]` on the aggregate does not rename the model's property; a property you declare yourself on a
`partial ProductModel` keeps its own pin.

## The current user — `Current`

A read or a write on a model is a static call — `Product.Where(…)`, a `Product.Create(…)` factory — so there
is no constructor to inject the signed-in user into. `Current` answers from anywhere instead, with nothing
injected:

```csharp
public sealed class Product : Aggregate<Guid>
{
    public string Name { get; private set; } = "";

    public Guid OwnerId { get; private set; }

    public static Product Create(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        OwnerId = Current.RequiredUserId,
    };
}
```

| Member | What it answers |
|---|---|
| `Current.UserId` | the signed-in user's id (`Guid?`), or `null` when the work is for nobody |
| `Current.RequiredUserId` | the same, or an `InvalidOperationException` when there is none |
| `Current.Principal` | the session's or request's `ClaimsPrincipal` — roles, email, any claim |
| `Current.Tenant` / `Current.RequiredTenant` | the tenant in flight — see [Multi-tenancy](multi-tenancy.md) |
| `Current.UseUser(id)` | a scope that runs the work for `id` — a test, or a tool acting for a user |

**Where it is set.** For a live session's work (rendering, event handlers), for every HTTP request (a minimal API,
a controller, a CQRS endpoint), and for a background job — which runs for the user who enqueued it, because the
job row records `UserId` and the runner re-enters it. Anywhere else (a hosted service's own loop, startup) it is
`null`, and the `Required…` forms throw rather than let an anonymous write through looking like a signed-in one.

**The user is an id, not your `User` row.** Loading the row is a query, and a property that silently queried the
database on every read would be the wrong thing to hide. Load it when you need it:

```csharp
var me = await User.First(u => u.Id == Current.UserId);
```

Inside a component, inject `IUserProvider` as before — it also raises `Changed` so the UI re-renders on sign-in.
`Current` is for the code that has no constructor to inject into.

## Multi-tenancy

One trait partitions a table by tenant. Opt-in, so a table that does not name it is one table for everybody:

```csharp
public sealed class Invoice : Aggregate<Guid, Timestamps, PerTenant> { }

var invoices = await Invoice.OrderByDescending(i => i.CreatedAt).Take(20);   // this tenant's
```

That gives the table a `TenantId` column and a query filter no read can compose away. Its indexes are
[what you write](multi-tenancy.md#an-index-is-what-you-write): name `TenantId` in one that is unique per tenant,
leave it out of one that is unique across every tenant, like a sign-in name. The column is a shadow one; an aggregate that reads its tenant declares
`public Guid? TenantId { get; private set; }` — or `int?` / `long?` where tenants are numbered, and the declared
type is then the column's; `PerTenant<int>` / `PerTenant<long>` gives a numbered shadow column without declaring
one, and a declared `TenantId` must match it. The tenant is the signed-in user's — it rides on the principal as the `rask:tenant`
claim — or whatever a resolver you register returns (`services.AddRaskTenant(sp => …)`), asked once by a request
and [at every event by a live page](multi-tenancy.md#from-the-request-a-resolver), so the read above
filters with nothing passed to it and a page follows its user into the tenant they chose; `Tenant.Use(id)` and `Tenant.Across()` say otherwise explicitly.
A tenant-scoped read with no tenant **throws** rather than return nothing, `IgnoreQueryFilters()` never
crosses tenants, and a create stamps the tenant while an update refuses to move a row between tenants.
Only the ROOT carries the tenant: an entity under it — at any depth — is
[read through its root](multi-tenancy.md#a-child-belongs-to-its-roots-tenant), an `EXISTS` up the path the
aggregate's own collections make, one per parent for a table two aggregates hold (`Driver.Chips`, `Vehicle.Chips`).
`RaskAppDbContext` applies it; a context of your own calls `modelBuilder.ApplyRaskConventions(this)`.

Where the tenant comes from, administrators, what it means for jobs, mail, the outbox, the cache, file storage
and accounts, and how each provider behaves: **[Multi-tenancy](multi-tenancy.md)**.

## Writing: plain EF Core

The writes on the type cover a create, an update and a delete. **Everything past that is ordinary EF Core** — a
change several entities make together, a query-then-decide, a bulk statement: load the entities into a context,
call their methods, save — so the entity's own rules run on every write, and so do the interceptors:
`CreatedAt`/`UpdatedAt` are stamped and `Version` is bumped where the aggregate names them, a delete of an
aggregate that names `SoftDelete` becomes a `DeletedAt` stamp, and the aggregate's domain events are published after the commit. When a
check and the writes it guards must be one transaction, wrap them in
[`Db.Transaction`](#a-check-and-the-writes-it-guards-dbtransaction) instead of opening one by hand.

`RaskAppDbContext` (namespace `Rask`) is the context the host builds: every entity you declared, plus every
battery's tables. It has no `DbSet` properties, so an entity is reached with `db.Set<Order>()`. An app that
[registered its own context](#using-ef-core-the-usual-way) uses that one the same way, with
`IDbContextFactory<TheirContext>` in place of the one below. An app `rask new` scaffolds writes no context, so
it takes `IDbContextFactory<RaskAppDbContext>` as shown.

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
    public async Task Handle(CancelOrder command)
    {
        var ct = Current.Cancellation;   // EF Core's own calls take the token by hand
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
    public async Task<Guid> Handle(PlaceOrder command)
    {
        var ct = Current.Cancellation;
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

    private async Task Ship()
    {
        await using var db = await contexts.CreateDbContextAsync(CancellationToken);   // EF Core: the page's token, by hand

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
(`ValueGeneratedOnAdd()`, a database default) is left as you set it, and an integer key you marked
`ValueGeneratedNever()` is yours to assign like the rest: `0` is refused, and the aggregate takes its key
[in `Create(id, …)`](#writing-create-update-delete).

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

Behaviour that touches the database gets a real one in a line, rather than a mocked `DbContext` — and
it is the app's OWN context, so the test runs the model and the save the app runs in production. Save
through `database.Context`, then read through the model surface exactly as the app does:

```csharp
await using var database = await TestDatabase.Start<AppDbContext>(o => o.UseSqlite($"Data Source={path}"));

var anvil = Product.Create("Anvil", 9.99m);
database.Context.Add(anvil);
await database.Context.SaveChangesAsync();

anvil.Reprice(12.50m);
await database.Context.SaveChangesAsync();   // stamped and versioned, exactly as in production

Assert.Equal(12.50m, (await database.Load<Product>(anvil.Id))!.Price);
```

```csharp
database.Context.AddRange(Order.Place("B-2"), Order.Place("B-3"));
await database.Context.SaveChangesAsync();

Assert.Equal(2, await Order.Count());
```

`TestDatabase.Start<AppDbContext>` builds the app's context the way the app does — through its constructor
taking `DbContextOptions<AppDbContext>` — so what its `OnModelCreating` adds (`b.AddRaskOutbox()`, a table of
its own) is in the schema, and its `SaveChangesAsync` override (a guard, an audit) runs on every save, whether
the test saves through `database.Context` or through an aggregate (`product.Save()`, `Product.Create(model)`).
`database.Context` is typed as `AppDbContext`, so its own sets need no cast. A context with no such constructor
is refused with a message naming the one to add. An app on Rask's own context writes `TestDatabase.Start(…)`,
which runs a plain `RaskDbContext`.

Either way it maps every entity the build found — so no fixture has to list entities — creates the
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
mapped by convention. An index declared here is kept exactly as written — on a
[tenant-scoped](multi-tenancy.md#an-index-is-what-you-write) table too.

The method is matched by signature, so a near miss (an instance method, a private one, the wrong
builder type) is reported as [RASK072](diagnostics.md#rask072) rather than silently not called.

### App-wide model rules: one `IRaskModel`

What holds for every table — a string length, a decimal's precision, how a constraint is named, what a delete does —
goes in **one class** implementing `IRaskModel`, the way Rails puts it in `ApplicationRecord`. The source generator
finds it; there is nothing to register:

```csharp
public sealed class AppModel : IRaskModel
{
    public static void Conventions(ModelConfigurationBuilder c)
    {
        c.Properties<string>().HaveMaxLength(1000);
        c.Properties<decimal>().HaveColumnType("decimal(32, 16)");
        c.Conventions.Remove(typeof(ForeignKeyIndexConvention));
        c.ConstraintNames(n => n.Kind switch
        {
            ConstraintKind.PrimaryKey => $"{n.Table}_pkey",
            ConstraintKind.ForeignKey => $"{n.Table}_{n.Columns[0]}_fkey",
            ConstraintKind.Unique     => $"{n.Table}_{n.Joined}_key",
            ConstraintKind.Index      => $"{n.Table}_{n.Joined}_idx",
            ConstraintKind.Default    => $"{n.Table}_{n.Columns[0]}_df",
        });
        c.DeleteBehavior(children: DeleteBehavior.ClientCascade, others: DeleteBehavior.NoAction);
    }

    public static void Model(ModelBuilder m) => m.HasDefaultSchema("dbo");
}
```

- **`Conventions`** is EF Core's `ConfigureConventions` for the whole app; **`Model`** runs on the model after Rask's
  conventions and **before** each entity's own `Configure`. Both are optional. They reach `RaskAppDbContext`, an
  app's own context deriving from `RaskDbContext`, every battery's tables, and the read faces' context.
- **`ConstraintNames`** names every primary key, foreign key, unique index or alternate key, index and — on SQL
  Server — column default the model did not name itself. `n.Table`, `n.Columns` (as the database names them) and
  `n.Joined` (`Name_TenantId`) describe it; return `null` to keep EF Core's name. A name longer than the database
  allows is cut to its limit: 128 characters on SQL Server, 63 on PostgreSQL.
- **`DeleteBehavior(children:, others:)`** says what a delete does: `children` for an aggregate's
  [children](#children) (Rask's own rule is `Cascade`), `others` for every other foreign key. `ClientCascade` keeps
  the children going with their aggregate while the database's key says `NO ACTION` — what a schema another tool made
  usually has.
- Both are applied as the model is finalized, so they see every table — **and an entity's `Configure` still wins**:
  a `HasDatabaseName`, `HasConstraintName` or `OnDelete` written there is kept as written. A unique index's
  [message](#what-a-unique-indexs-violation-says) and a [referenced delete's sentence](#what-deleting-a-referenced-row-says)
  find their index or key under the name the rule gave it.
- A battery states the length of every text column it owns (a job's payload, a mail's body are unbounded), so an
  app-wide `HaveMaxLength` never cuts one off.

One per app: a second class implementing `IRaskModel` in the same project is [RASK110](diagnostics.md#rask110), and
two assemblies each bringing one refuse the start, naming both.

### What a unique index's violation says

A unique index is a rule, and the database is the only place that can enforce it without a race. Say what
breaking it means, on the index:

```csharp
builder.HasIndex(d => new { d.Name, d.TenantId }).IsUnique("A destination with this name already exists.");

await Destination.Named(model.Name).Save();   // nothing else at the call site
```

A save that violates the index then fails the way a [validator's rule](validation.md#a-rejected-request) does — a
`RaskValidationException` whose `Errors` carry the message — instead of as the provider's `DbUpdateException`,
whose text names tables and holds the conflicting value. Every save through the context does this:
`Save()`, `Create`, `Update`, and plain `SaveChangesAsync`.

It carries ONE failure for the index (`Failures`, the shape a form reads): the message, over every property the
index names in index order — `TenantId` aside, which is never a field — and the index's name as its source.
`Errors`, the shape that crosses the wire, repeats the message under each of those fields.

| The index is over | `Failures` | `Errors` |
|---|---|---|
| `(Name, TenantId)` | one, over `Name` | `"Name"` |
| `(Year, Number)` | one, over `Year` and `Number` | `"Year"` and `"Number"`, the same message under both |

- **The message is a constant, and stays one.** It is shown to whoever sent the value, and nothing is appended
  to it — not the value, and not which row it collided with.
- **An index with no message keeps the provider's error**, exactly as before. `IsUnique()` and `IsUnique(false)`
  are still EF Core's own; only a string is Rask's.
- **The index is recognised by the provider's own error**, not by the text of a message a server may
  translate: SQLSTATE `23505` and the constraint name on PostgreSQL, error `2601` / `2627` and the index name on
  SQL Server, and on SQLite — which names no index — the table and columns it lists. It needs no provider
  package of Rask's: a plain `UseSqlServer` works. On a table that already exists the index's name in the
  database has to be the one in the model — EF Core's `IX_{Table}_{Columns}`, or say `HasDatabaseName("…")`,
  which is kept as written. On SQLite an index the database holds without the `TenantId` the model's names is
  still recognised by its other columns.
- The original exception is the `InnerException`, for the log.

**In a `Form` the message appears under the field** — under each of them, for an index over several — with
nothing written in the submit handler: the exception names its fields (`IFieldFailures`), and a form shows
such a failure where it can be corrected instead of failing the submit. Over
[remote dispatch](validation.md#a-rejected-request) it is the same 400 with field errors a validator produces,
and the client's exception names its fields too, rebuilt from that dictionary: one failure per distinct
message, over every field that carries it. Thrown from a handler that is no form's submit — a list's delete — its
message is an error toast: [a handler that refuses](composition-lists.md#a-handler-that-refuses).

#### When the index may not be there

On a database Rask did not create — [a table that already exists](#mapping-tables-that-already-exist) — an
index the model declares is a claim about somebody else's schema, and the table may not have it. A host wired
with `AddRaskData<TContext>(o => …)` therefore asks every rule declared with `IsUnique("…")` as a **query**
before each save, and refuses the save with the same failure:

- over exactly the index's columns: the row's tenant for an index that names `TenantId`, every tenant's rows for
  one that does not ([an index is what you write](multi-tenancy.md#an-index-is-what-you-write))
- a NULL in one of the index's columns is not checked, as a database does not let NULLs collide
- an index filter that only says its own columns are `NOT NULL` counts as no filter; an index with any other
  filter is skipped, because its condition is SQL that Rask will not guess at
- two rows of one save that break a rule between them are refused too
- the row being saved does not collide with itself

The check and the save share one transaction — the caller's when there is one, otherwise one opened for the
save (not on a context whose execution strategy retries, which refuses a transaction it did not start; there
the check runs just before the save). **It is a check, not a lock:** two saves at the same instant can both
pass it, and only a real unique index closes that. Where the index does exist the database still has the last
word, reported the same way. An index declared with plain `IsUnique()` has no message to fail with and is not
checked.

### What deleting a referenced row says

A row that other rows still point at cannot go, and only the database knows. Say what that means once, on the
aggregate being deleted:

```csharp
public static void Configure(EntityTypeBuilder<Trailer> b)
{
    // a relationship the model maps: its own sentence
    b.HasMany<DeliveryNote>().WithOne()
     .HasForeignKey(n => n.TrailerId)
     .OnDelete(DeleteBehavior.Restrict, "The trailer is on a delivery note and cannot be deleted.");

    // every other reference — a table another context owns, one this app never reads
    b.WhenReferenced("The trailer is still in use and cannot be deleted.");
}

await Trailer.Delete(id);   // nothing else at the call site
```

A delete the database refuses then fails as a `RaskValidationException` with ONE failure that names **no
field** — it is about the delete, not about anything on a form — where the provider's `DbUpdateException`
would have named tables and constraints. `Errors` holds the sentence under the empty key. Every delete through
the context does this: `Trailer.Delete(id)` and plain `db.Remove(…)` + `SaveChangesAsync`.

Which sentence, in this order:

| The database says | The failure carries |
|---|---|
| which constraint (PostgreSQL, SQL Server), and it is a relationship in the model | that relationship's sentence, else the entity's `WhenReferenced` |
| which constraint, and the model does not know it — a table that is not mapped | the entity's `WhenReferenced` |
| nothing (SQLite), and exactly one relationship onto the deleted entity has a sentence | that sentence |
| nothing (SQLite), and no relationship has a sentence, or several do | the entity's `WhenReferenced` |
| anything, and no sentence fits | the provider's exception, untouched |

- **Only a refused DELETE is translated.** A row inserted or updated with a key that points nowhere fails with
  the same provider code and keeps the provider's exception.
- **A context that already tracks the referencing row refuses before the database does.** EF Core throws an
  `InvalidOperationException` from `Remove` itself for a required relationship that does not cascade.
  `Trailer.Delete(id, db: context)` answers that with the same sentence; a plain `context.Remove(trailer)` is
  EF Core's own and keeps its exception — delete in a context that is not holding the referencing rows.
- **`OnDelete(behavior)` with no string is still EF Core's own**, and such a relationship has no sentence. The
  string is also accepted when the relationship is written from the other end (`HasOne<Trailer>().WithMany()`)
  and on a one-to-one.
- **The message is a constant**, shown as written; the original exception is the `InnerException`, for the log.
- **Recognised by the provider's own error**, with no provider package of Rask's: SQLSTATE `23503` on
  PostgreSQL, error `547` on SQL Server, and SQLite's `FOREIGN KEY constraint failed`. On a table that already
  exists the constraint's name in the database has to be the one in the model
  (`FK_{Dependent}_{Principal}_{Column}`, or `HasConstraintName("…")`) for the relationship's sentence to be
  found; under any other name the entity's `WhenReferenced` answers.
- **A soft delete is never refused.** An aggregate that names `SoftDelete` keeps its row, so
  the database is not asked and the rows that reference it stay as they are.

## Value objects

A value object is **its values**: two with the same values are the same. Any composite an entity holds that is
not itself an entity is one, and its values become columns on the owning row: one that holds a single value is that
value's column, and one that holds several is an EF Core **complex type**.

One an abstract base declares is mapped the same way, at any depth, on every aggregate deriving from it: each
concrete aggregate is its own table holding the base's columns, and its read face and form model carry them too.
A generic base (`Filing<TSelf> where TSelf : Filing<TSelf>`) is no different: `VatReturn : Filing<VatReturn>` holds
the base's children and collections of values behind its private fields as its own.
The base itself is never an entity of the model.

It must compare by value, or the build says so ([RASK105](diagnostics.md#rask105)). A `record` or a
`record struct` already does, and needs nothing else. A plain class derives from `ValueObject` and names the values
that make it the same:

```csharp
public sealed class Address : ValueObject
{
    private Address() { }

    public string Street { get; private set; } = "";
    public string City { get; private set; } = "";

    protected override IEnumerable<object?> EqualityComponents()
    {
        yield return Street;
        yield return City;
    }
}
```

A record that compares some other way says so in its own `Equals` — the language uses it for `==` too:

```csharp
public sealed record Email(string Value)
{
    public bool Equals(Email? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
}
```

**The database may disagree with C#.** A string record compares ordinally and case-sensitively, while SQL Server's
default `*_CI_*` collation — and a unique index under it — treats `"Abc"` and `"abc"` as one. Compare the value
object the way its column does, as `Email` above does.

```csharp
public sealed record Money(decimal Amount, string Currency);
public sealed record Email(string Value);

public sealed class Customer : Aggregate<Guid, Model>
{
    public Money CreditLimit { get; private set; } = new(0m, "EUR");   // CreditLimit_Amount, CreditLimit_Currency
    public Email Email { get; private set; } = new("");                 // one column: Email
}
```

**A one-value value object is one column named after the property**, so `Email Email` is stored as `Email`, and a
form model carries it as its value, `string? Email`. A value object with several values is prefixed by the
property, and a form model carries it as a nested model.

**Declare it nullable and it is optional** — a class or a struct alike:

```csharp
public readonly record struct Percent(decimal Value)
{
    public static IEnumerable<string> Validate(decimal value) => value is < 0 or > 100 ? ["0 to 100."] : [];
}

public Email? Backup { get; private set; }        // nullable column Backup,   form field string?  Backup
public Percent? Discount { get; private set; }    // nullable column Discount, form field decimal? Discount
```

An empty field is stored as `NULL` and is not put to the rule; a filled one is asked `Percent.Validate` by the
form and by `Create`/`Update`, and the read face shows `decimal? Discount`. A nullable struct of **several**
values (`Money?` over a `record struct`) is not unwrapped: the form model carries the struct as it is, and its
mapping is yours to write in `Configure`.

That one column is an ordinary scalar column with a conversion — the value object goes in and out through its
one value — so everything a column can have, it can have, configured on the property itself:

```csharp
public static void Configure(EntityTypeBuilder<Customer> builder)
{
    builder.Property(c => c.Email).HasMaxLength(255);
    builder.HasIndex(c => new { c.Email, c.TenantId }).IsUnique("This address is already registered.");
}
```

The unique rule's failure names the field `Email` — the form's own name for it — not `Email.Value`. The value
object is rebuilt through the constructor that takes its value, or, for the private shape, a parameterless
constructor and its property; both may be private.

**What it costs: a query on the aggregate cannot reach inside the value.**

```csharp
// on the read face — where queries belong — the value IS the primitive, and everything translates
await Customer.Where(c => c.Email.StartsWith("ada@")).OrderBy(c => c.Email);

// on the write model (plain EF Core over the aggregate): comparing the whole value translates…
await db.Set<Customer>().Where(c => c.Email == new Email("ada@example.com")).ToListAsync(ct);

// …and member access does not: "could not be translated"
await db.Set<Customer>().Where(c => c.Email.Value.Contains("ada")).ToListAsync(ct);
```

Before, when a one-value value object was a complex type, the last line translated and no index could name the
column. Filter and sort on the read face, or compare whole values.

**The same holds inside another value object.** `Currency` in `record Money(decimal Amount, Currency Currency)`
is a converted scalar column of `Money` — `Price_Currency`, the column it always was — which is what lets EF Core
hand it to `Money`'s constructor. Its facets go on that property:

```csharp
builder.ComplexProperty(c => c.Price).Property(m => m.Currency).HasMaxLength(3);
```

One shape stays out of reach: a value object built through its **constructor** cannot hold another value object
of **several** values (`record Order(Money Total, …)`) — EF Core binds a constructor parameter to a column, never
to a complex type. The build refuses it ([RASK103](diagnostics.md#rask103)) rather than let the app fail when it
starts: give the outer type a private parameterless constructor and private setters, and it maps.

What is **not** a value object: a type EF Core maps as a column on its own (`string`, `DateTime`, `Guid`, an
enum, anything from `System` or `Microsoft`), a collection, an abstract or generic type, and an `Entity<TId>`.

**A value object with several values is a complex type, not an owned entity**, and the distinction is the point. An owned entity is a row
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
`RaskAppDbContext` is never constructed. Call `modelBuilder.ApplyRaskConventions(this)` from its
`OnModelCreating` to keep the query filters (soft delete where an aggregate opts in, and the
[tenant](multi-tenancy.md)) and the concurrency tokens, or `ModelRegistry.Apply(modelBuilder)` to keep every
declared entity mapped as well. A context with a tenant-scoped table implements `ITenantScoped`, as
`RaskDbContext` does; the parameterless `ApplyRaskConventions()` is for one without.

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
        modelBuilder.ApplyRaskConventions(this); // query filters + concurrency tokens — always last
    }
}
```

Over plain `DbContext` this compiles, boots and migrates with every entity silently absent, so the first
`Product.Where(…)` throws saying the type is not part of the model. If you
cannot change the base type — it is already someone else's — call `ModelRegistry.Apply(modelBuilder)` in
place of `base.OnModelCreating`, and `ModelRegistry.ApplyConventions(configurationBuilder)` from an
overridden `ConfigureConventions`.

**Who is signed in, and what a save should refresh.** A Rask host also registers two scoped seams, and a host
of your own registers what it needs of them:

| Seam | What it answers | Without it |
|---|---|---|
| `IPrincipalSource` | `Current` — the principal of the session or request, and through it `Current.UserId` and the tenant a [tenant-scoped](multi-tenancy.md) read filters by | `Current.UserId` is `null`, and a tenant-scoped read throws unless a `Tenant.Use` scope says which tenant |
| `IDataChanges` | told which entity types a save wrote, once it commits — a Rask host refreshes the session's [Rask.Query](query.md) queries about them | nothing is told, which is the right answer where no screen is waiting |

Both are **scoped**, because the principal is, and a read is a static call that runs outside any DI scope — so
open `Db.UseScope(scope)` around each request's work, after authentication, and every read and write inside
it resolves them from that scope:

```csharp
builder.Services.AddScoped<IPrincipalSource, HttpPrincipalSource>();   // yours: returns HttpContext.User

app.UseAuthentication();
app.Use(async (context, next) =>
{
    using (Db.UseScope(context.RequestServices))
    {
        await next(context);
    }
});
```

Outside any scope — a hosted service's loop, a test — say who the work is for with `Current.UseUser(id)` and
`Tenant.Use(id)`, or clear the tenant with `Tenant.None()`.

**In a WebAssembly app none of this is yours to write.** Rask.Data's browser build does it from
`AddRaskData<AppDbContext>()`: it points the model surface at the context before the first render, and the browser
host makes the page's services ambient around its renders and handlers, so a save refreshes the page's
[Rask.Query](query.md#writes-refresh-queries-by-themselves) queries about what it wrote:

```csharp
host.Services.AddRaskData<AppDbContext>();
host.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseSqlite(BrowserSqlite.ConnectionString("app"))
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
host.Services.AddRaskCqrs();
host.Services.AddRaskQuery();

.OnSubmit(note => Note.Create(note))        // the QueryKey.For<Note> list refetches
```

## Mapping tables that already exist

An app that already has a database — its own `DbContext`, its own classes, its own migrations — can model
its domain with aggregates over the tables it has, with **no migration**. A bare aggregate carries no column of
the framework's, so the work is naming only the traits the table really has, on the entity that maps it.

```csharp
public sealed class Destination : Aggregate<int, PerTenant<int>>   // filtered and stamped by TenantId — nothing else
{
    public int? TenantId { get; private set; }                // the column's own type: int?, long? or Guid?
    public string Name   { get; private set; } = "";

    public static Destination Named(string name) => new() { Name = name };
    public void Rename(string name) => Name = name;

    public static void Configure(EntityTypeBuilder<Destination> b)
    {
        b.ToTable("Destinations");                                // the default would be the class name
        b.Property(d => d.Name).IsRequired().HasMaxLength(255);
        b.HasIndex(d => new { d.Name, d.TenantId }).IsUnique();   // names the tenant, so it stays as written
    }
}
```

| What Rask can map | What an existing table usually has | How to say so |
|---|---|---|
| `CreatedAt` and `UpdatedAt` | neither, or one | name nothing, or `Created` / `Updated` for the one it has — on the root; a [child](#children) names it on `Entity<TId, …>` |
| a `Version` concurrency token | none | name nothing — a write then takes no version, and the last writer wins |
| a delete | a hard one | `Deletable`; `SoftDelete` only for a table with a `DeletedAt` column; neither, and there is no `Delete` |
| a table named after the class | a plural, or a name of its own | `b.ToTable("Destinations")` in `Configure` |
| an integer key the database numbers | a key the application numbers | `b.Property(d => d.Id).ValueGeneratedNever()` in `Configure` — the aggregate then takes its key in [`Create(id, model)`](#writing-create-update-delete) |
| a `Guid?` tenant column | an `int` one | `PerTenant<int>`, and declare `public int? TenantId { get; private set; }` to read it — see [numbered tenants](multi-tenancy.md#tenants-that-are-numbered) |
| — | `TenantId` in a per-tenant index | write it into the index: it is [left exactly as written](multi-tenancy.md#an-index-is-what-you-write) |

Column names, lengths, precision, conversions and relationships are ordinary EF Core in the same `Configure`.
Tables this context does not map still hold foreign keys onto the ones it does: `b.WhenReferenced("…")` is
[what a delete they refuse says](#what-deleting-a-referenced-row-says).

### A second context, beside the one you have

The app's own context and classes stay exactly as they are. Beside them goes one more context — Rask's model,
over the same tables — and nothing has to make an aggregate live inside the old one:

```csharp
public sealed class DomainContext(DbContextOptions<DomainContext> options) : RaskDbContext(options);
```

On a host you wire yourself — `AddRask()` and `MapRask<TApp>()` rather than `RaskApp` — that is two calls from
`Rask.Server`:

```csharp
builder.Services.AddRask();
builder.Services.AddRaskData<DomainContext>(o =>
    o.UseSqlServer(configuration.GetConnectionString("Default")));   // the same database as the legacy context
builder.Services.AddRaskTenant(sp => sp.GetRequiredService<ICurrentRequest>().TenantId);

var app = builder.Build();
app.UseAuthentication();
app.UseRaskData();                       // after authentication, before the endpoints
app.MapRask<App>(pathBase: "/new");
```

`AddRaskData<TContext>(o => …)` registers what a `RaskApp` wires for data and a hand-wired host otherwise goes
without: the context factory with Rask's interceptors on it (the tenant stamp lives there), the read context on
the same database, who is signed in — for an HTTP request and for a live session — the scope a live page's
work runs in, and the mediator and query cache a save refreshes. An `(sp, o) => …` overload hands the callback
the app's services. `app.UseRaskData()` makes each request's services ambient and points the model surface at
the context — the `Db.Configure(app.Services)` a host used to call.

**Neither creates, migrates or checks anything.** No connection is opened until the first read, no
`MigrateOnStart` is registered, and `Rask:Database` is not read: the database is the one the callback names.

Without ASP.NET — a worker, a console app — the [wiring above](#wiring-when-rask-is-not-hosting) is the same
thing by hand: `AddRaskData<DomainContext>()`, the two context factories, `Db.Configure`, and `Db.UseScope`
around each unit of work.

```csharp
await Destination.Named("Budapest").Save();                  // TenantId stamped with the resolved tenant
var first = await Destination.OrderBy(d => d.Name).Take(15); // only the resolved tenant's rows
await Destination.Update(id, d => d.Rename("Pest"));         // no version to pass
```

Three things keep the two contexts honest with each other:

- **The old context goes on owning the schema.** Never point `dotnet ef migrations` at the Rask context, and
  never call `EnsureCreated` or `Migrate` on it: it maps tables, it does not define them.
- **Nothing checks the mapping against the database.** A column the table has and the class does not is left
  alone on an update and must be nullable or defaulted for an insert; a property the table has no column for
  is an error from the first query that selects it. Map one table, run it, then the next.
- **Each context tracks its own changes.** A row saved through one is not seen by an instance of the other
  that already loaded it, and a save through each is two transactions unless you open one on a shared
  connection yourself.

Where the tenant comes from, what a request with no tenant reads, and every path that reads or writes without
the filter: **[Multi-tenancy](multi-tenancy.md#from-the-request-a-resolver)**.

## What the interceptors do

- **`AuditingInterceptor`** — stamps `CreatedAt`/`UpdatedAt` (UTC, from an injectable `TimeProvider`) and
  increments each `Versioned` aggregate's `Version` on update, so the stored token changes (SQLite has no rowversion).
- **`SoftDeleteInterceptor`** — for an aggregate that names `SoftDelete`, rewrites a `Deleted`
  entry to `Modified` + sets `DeletedAt`. Your handler just calls `db.Remove(entity)`; to restore, load with
  `IgnoreQueryFilters()` and clear `DeletedAt`. Every other entity's delete removes the row.
- **`DomainEventInterceptor`**: after the change commits, publishes each entity's `DomainEvents`
  through `IDispatcher.Publish` (in a fresh scope) and clears them, so every `IEventHandler<T>` reacts in memory.
  An `IDurableHandler<T>` is not run here: [`Rask.Outbox`](outbox.md)'s interceptor stored it in the save's own
  transaction, reading the same events. The events are cleared only after the commit, so the order of the two
  interceptors doesn't matter. With no `AddRaskCqrs()` there is nothing to reach, and nothing is published.

## Optimistic concurrency

A `Versioned` aggregate's `Version` is an EF Core concurrency token, bumped on every save. `Product.Update(id, model)`
does the rest for you — it checks the `Version` the model carries — and `Update(id, p => …)` and
`Delete(id)` take a `version:`. A hand-written save does it itself. The edit form carries the
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
| `ignoreSoftDeleted` | Lets a soft-deleted row free its slot. Applies only to an aggregate that names `SoftDelete`, where it defaults to on; a hard-deleted row has already freed its slot, so asking for it on any other entity throws. |

Three things worth knowing:

- **Enforcement is in the database, not the `DbContext`.** Raw SQL, a second process and a background job
  are all bound by it. That is the point — an application-level check is bypassable, and a check-then-insert
  in your own code has a race between the check and the insert.
- **It arrives via migrations.** Adding the rule to an existing table is a migration of its own —
  `dotnet ef migrations add` sees it even when no column changes — and applying it checks every later
  write. Removing the rule is a migration too, and drops the triggers and index; changing it rebuilds them.
  A database created with `EnsureCreated` does not get it at all. Rows already in the table are not
  re-checked: find existing overlaps with a query before you add the rule.
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

Then search from the read face or a context:

```csharp
await Product.Search(query).Where(p => p.Active).Take(20);
await db.Set<Product>().Search(query).CountAsync();
await Product.Search(query).Skip(20).Take(20);      // a table's second page, best match first
```

`Search(text)` keeps every row containing all of the typed words — any order, any case, diacritics
ignored, the last word as a prefix — **best match first**, and keeps composing; a later `OrderBy`
replaces the rank order. The text is always words, never query syntax, so nothing a user types can break
the query. `FullText.Highlight(p.Name)` and `FullText.Snippet(p.Description)` inside a `Select` return the
matched terms marked, for the page to wrap in `<mark>` ([how](full-text-search.md#showing-why-each-row-matched)).

It runs on **SQLite** (`UseRaskSqlite`, or a plain `UseSqlite` plus `UseRaskFullTextSearch()`), **in the
browser** on the same FTS5 index, and on **PostgreSQL** (`UseRaskPostgres`, a generated `tsvector` column with a
GIN index). The database keeps the index current, so raw SQL and other processes are searchable too. On SQL
Server `AddRaskData<TContext>` **refuses to boot** rather than letting the first search fail. How it works on
each, the tokenizers and the costs: **[Full-text search](full-text-search.md)**.

A filter on a value inside a JSON column gets an index the same way — `HasJsonIndex(o => o.Meta.Status)`, on
SQLite: see [Rask.SQLite](sqlite.md#indexing-a-value-inside-a-json-column).

## Migrations apply themselves on start

Change the model, add a migration, start the app:

```bash
rask db add AddProducts     # writes Migrations/…_AddProducts.cs — commit it with the code
dotnet run                  # applies it, then starts
```

A `RaskApp` applies its pending migrations when it starts — to `RaskAppDbContext`, or to your own context if you
registered one — before any battery's worker runs and before the web server listens, so `/health` never answers
Healthy over a database that isn't migrated yet. A fresh clone or a fresh deploy needs `dotnet run` and nothing else.
A `rask dev` session that is already running picks the new migration up the next time it restarts.

What it refuses to guess:

- **No migrations at all** — the app starts, leaves the database alone and logs *"RaskAppDbContext has no migrations
  yet, so the database was left as it is. Create the first one with `rask db add Init`…"*. It never creates the schema
  with `EnsureCreated`, which no later migration could evolve. (`rask new` adds `Init` for you.)
- **A model changed since the last migration** — the start fails: *"The RaskAppDbContext model has changed since its
  last migration, so the database cannot be brought up to date. Add a migration for the change with
  `rask db add <Name>` and start the app again…"*. Silently running on an un-migrated model is the bug this prevents.
- **A migration that fails** stops the start with EF's error and the migration's name, rather than serving on half a
  schema.

Two instances starting together are safe: EF Core takes a migration lock on PostgreSQL and SQL Server, and a SQLite app
is one process.

When something else applies migrations — a release pipeline running `rask db update` or an idempotent SQL script —
turn it off:

```csharp
app.Configure(c => c.MigrateOnStart = false);
```

or set `Rask:Database:MigrateOnStart` to `false` (`Rask__Database__MigrateOnStart=false` in the environment). The code
line wins over the setting. A host wired by hand (`AddRask()` + `MapRask<TApp>()`) never migrates on start.

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
- **An app scaffolded by `rask new`** is a `RaskApp` on `RaskAppDbContext`, so the setting and a connection string
  are the whole switch — `Rask.Server` already carries both server providers. Its `appsettings.json` has a
  `Rask:Snapshots` section, so delete that, as above, and regenerate the migrations against the new provider.
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
SQL through the provider), [multi-tenancy](multi-tenancy.md), and the jobs, mail, outbox and cache batteries,
whose leased claim is proven against a real server. [Full-text search](full-text-search.md#postgresql) works
too — the same `HasFullTextSearch` and `Search(text)`, on a stored generated `tsvector` column with a GIN index
that PostgreSQL keeps current itself, with no triggers. Three things change:

- **Retrying refuses a transaction you open yourself** outside the execution strategy. Wrap a hand-written
  `BeginTransaction` in `context.Database.CreateExecutionStrategy().ExecuteAsync(...)`, or set
  `Rask:Postgres:Retry:Enabled` to `false`.
- **The file-shaped batteries do not apply.** Litestream and snapshots replicate or copy a SQLite file, and
  there is no file — back up with your provider's snapshots or `pg_dump`.
- **`HasJsonIndex` and non-overlapping ranges are SQLite-only for now.** A context that declares either on
  PostgreSQL is refused at boot rather than left to scan or to allow overlaps silently.

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
SqlClient resets them on every pooled open. Retrying (`Retry`) is SQL Server's own strategy. `SplitQueries`
loads several included collections as one statement each rather than one join — for every context that reads
the section, the generated read faces among them; a single query still picks with `AsSplitQuery()`. Each is
`Rask:SqlServer` in `appsettings.json` (`"LockTimeout": "00:00:03"`, `"Retry": { "MaxCount": 3 }`); a callback —
`UseRaskSqlServer(sp, s => …)` — runs after the section and wins.

Everything in this guide works unchanged except full-text search, `HasJsonIndex` and non-overlapping ranges,
which a context on SQL Server cannot declare — it is refused at boot, naming the declaration. Three things to
know, as on PostgreSQL:

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
