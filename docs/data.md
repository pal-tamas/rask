# Rask.Data — storage: queries, conventions and the database

> **In practice:** [Tutorial Ch 2](tutorial/02-first-feature.md) · recipe [add a feature to an existing database](recipes.md#add-a-feature-to-an-existing-database) · [cheat sheet](cheatsheet.md).
> **The rest of Rask.Data:** [domain modelling — aggregates, entities, value objects, operations](domain.md) · [writing many rows](data-writes.md).

`Rask.Data` is a layer over **Entity Framework Core** with one goal: **you declare aggregates, and that is
all**. No `DbContext` class, no `DbSet` property, no `IEntityTypeConfiguration`, no registration, and
no context injected into a page that reads a row. The aggregates themselves — their value objects, children and
operations — are [domain modelling](domain.md); this page is the storage side: how a query reads them, how they map
to tables, and how one model runs on SQLite, SQL Server and PostgreSQL alike.

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

## Code written for one database

An app's code names what it means, and Rask writes it for the database the app runs on. There is no SQL in an app
and no provider API — the same model, the same queries and the same tests run on SQLite, SQL Server and PostgreSQL:

| Say | Not | Where |
|---|---|---|
| `OrderTextBy("Hungarian_100_CI_AS")` once, `x.Name.InTextOrder()` in a query; `x.Name.Loosely().Contains(typed)` for a search | `EF.Functions.Collate`, `UseCollation` | [app-wide model rules](#app-wide-model-rules-one-iraskmodel) |
| the value object's `Scale`/`MaxLength` (`Number(Value, Scale: 9)`), or `HasPrecision` | a store type written in SQL | [decimals](#decimals-what-each-database-keeps) |
| `.Covering(x => x.Amount)` | `IncludeProperties` | [an index that answers the query by itself](#an-index-that-answers-the-query-by-itself) |
| `b.Computed(…)`, `b.Default(…)`, `b.StoredDefault(…)`, `HasIndex(…).Where(x => …)` | `HasComputedColumnSql`, `HasDefaultValueSql`, `HasFilter` | [computed columns, defaults and filtered indexes](#computed-columns-defaults-and-filtered-indexes-in-c) |
| `end - start` | `EF.Functions.DateDiff*` | [how long between two dates](#how-long-between-two-dates-in-a-query) |
| `IsUnique("…")`, `OnDelete(…)` with a message | catching a driver's `SqlException` | [a unique index's violation](#what-a-unique-indexs-violation-says) |
| `Db.Lock(name)`, `X.Where(…).ForUpdate()`, `LockRead.ForUpdate` | `sp_getapplock`, `UPDLOCK`, `FOR UPDATE` | [Db.Lock](#one-holder-at-a-time-across-hosts-dblock), [rows read for update](#rows-read-for-update-forupdate), [LockedBy](domain.md#a-row-closed-by-its-parent-lockedby) |
| a LINQ query, a change through the aggregate | `FromSql`, `ExecuteSql*`, `SqlQuery` | [the read face](#reading-the-read-face), [operations](domain.md#operations-what-the-aggregate-does) |

Code only one database understands is [RASK177](diagnostics.md#rask177) — a suggestion, a warning under
`<RaskStrict>` — and what Rask writes instead is proven the same on all three by [the conformance
suite](#the-conformance-suite). So an app's tests run on SQLite in memory and mean what they say on the server
production runs on ([testing a model](domain.md#testing-a-model)). The one place an app names its database is the
call that selects the provider — `UseRaskDatabase(sp)` reads it from `Rask:Database:Provider` — and a migration,
which is written for the database it migrates.

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

### A picker's rows: `X.Choice`, `X.Choices`, `X.ChoicePage`

A select over another table — a company, a driver, a vehicle — asks the read face for its rows, and every app used to
write the same three helpers for it: the row the form holds, a page of rows for what was typed, and how many there are.
The read face of every aggregate of the app has them, and so does every child entity keyed by an `Id` — a shooter's
pistol, a supply's part — read, as every read of a child is, [through its parent's tenant](multi-tenancy.md#a-child-belongs-to-its-roots-tenant)
and soft delete (a battery package's own tables, built `RaskReadFacesOnly`, do not — nothing picks a stored file or a
queued job, and Rask.Storage must not reference Rask.Core):

```csharp
await Company.Choice(id, c => c.Name);                          // ChoiceRow<int>? — null for no row, or another tenant's
await Company.ChoicePage(c => c.Name, typed, skip: 0, take: 50, picked: [held]);
IChoiceSource<int> companies = Company.Choices(c => c.Name, c => c.City);    // the text, and a muted line under it
IChoiceSource<int> pistols = Pistol.Choices(p => p.Serial);    // a child's: only those under the tenant's shooters
await Company.Where(c => c.Active).Choices(c => c.Id, c => c.Name).Page(typed, 0, 50);  // any query of a read face
```

- **The page** is the rows whose text — or detail — holds what was typed, case and accents aside (`Loosely()`, where
  the provider has the collation: `UseRaskSqlite`, `UseRaskSqlServer`, `UseRaskPostgres`; case aside elsewhere), in
  the app's text order (`InTextOrder()` under [`OrderTextBy`](localization.md); the column's own order without it), then by id, so
  the pages follow on and no row is on two. `Total` is how many the search finds; it is read only when the page
  cannot say it. `Picked` names the rows the picker holds whether or not they are on this page, in the order given.
- **Through the tenant in flight and the soft delete**, as every read is: an id of another tenant's row — a post, a
  stale link — is answered with nothing, by `Choice` and in `Picked`. So `Choice(id) is null` is also the check that
  an id is one the reader may pick; a save asks the same of every reference it sets
  ([multi-tenancy.md](multi-tenancy.md#a-reference-names-a-row-of-the-same-tenant)).
- **`IChoiceSource<TKey>`** (namespace `Rask`, in the `Rask` package) is what a picker reads: `Page(typed, skip, take,
  picked)` → `ChoiceRowPage<TKey>` (`Rows`, `Total`, `Picked`) and `Find(id)` → `ChoiceRow<TKey>?` (`Id`, `Text`,
  `Detail`). The read face's is one; a source of your own — rows worked out in C#, another service — implements the
  same two members.
- The text and the detail run in the database, so they are columns and what SQL can make of them
  (`c.Code + " " + c.Name`). Words only C# can write — a date formatted for the reader — are a page you read yourself.

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
| `ProductId? ProductId` (a [typed id](domain.md#strongly-typed-ids) some entity is keyed by) | `ProductId? ProductId` — **kept typed**, nullable as declared, with its `ProductRead? Product` navigation |
| `Quantity Quantity` (a one-value value object) | `long Quantity` — the value it holds |
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
| `ProductId ProductId` + a `Product : Aggregate<ProductId>` | exact name match, typed key | `Product` — and the column stays a `ProductId` |
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
`Provider.Configure` — counts the same. The one a root holds its children by —
`b.HasMany(c => c.Countries).WithOne().HasForeignKey(row => row.ForeignTaxProductCategoryId)` — is the child's way back to the root (`row.ForeignTaxCategory`),
on that column, and gives it no second navigation. One joined on a `HasPrincipalKey`, or on several columns, gets no navigation
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
the aggregate — [an operation's entry](domain.md#an-operation-run-as-one-write), [`Find` and
`Save`](domain.md#find-change-save) or [a write on the type](domain.md#writing-create-update-delete) — and each loads
the aggregate it is about to change.

For a shape this does not wrap — a group-by, a join, an aggregate — `Query` hands you the live
`IQueryable` inside a managed context:

```csharp
var byMonth = await Product.Query((q, ct) =>
    q.GroupBy(p => p.CreatedAt.Month)
     .Select(g => new { Month = g.Key, Total = g.Sum(p => p.PriceAmount) })
     .ToListAsync(ct));
```

### Rows read for update: `ForUpdate()`

A check and the write it guards — "is the month still open?", then a claim into it — need the rows to stay as they
were read until the write commits. `ForUpdate()` reads them under an update lock held to the end of the
`Db.Transaction` the read runs in, so a second writer of the same rows waits for the commit instead of reading what
the first is about to change:

```csharp
await Db.Transaction(async () =>
{
    var month = await Month.Where(m => m.Id == id).ForUpdate().Single();
    Refuse.When(month.Closed, "The month is closed.");
    await Claim.Into(id).Save();
});
```

- It is written in each database's own words: `WITH (UPDLOCK, HOLDLOCK)` on the query's first table on SQL Server,
  `FOR UPDATE OF` it on PostgreSQL, and as it is on SQLite, whose one writer serializes the transactions already.
  Every terminal reads under it — awaiting the query, `First`, `Single`, a `Select(…)` — and the rows of a table it
  joins (an `Include`) are not locked: lock them by a query of their own.
- **Outside a `Db.Transaction` it is refused** where it runs: the lock would end with the read and hold nothing.
- **A `Count()` or `Any()` of a locked query is refused** on every database — a lock is held on the rows a read
  hands back, and PostgreSQL holds none for an aggregate. Read the rows, and count them in memory.
- An operation that only needs its own aggregate's row locked says so where it is declared —
  `b.Operation(x => x.M, forUpdate: true)`, or `await id.Load(version, forUpdate: true)` ([operations](domain.md#operations-what-the-aggregate-does)).

**Code generic over the type** — one lock for four kinds of refund — has no read face to call it on, so the same step
is on any query of a set, `IQueryable<T>.ForUpdate()`:

```csharp
public static Task Lock<TRefund>(DbContext db, int id) where TRefund : class, IRefund =>
    db.Set<TRefund>().Where(r => r.Id == id).ForUpdate().ToListAsync();

await Db.Transaction(async () =>
{
    await using var db = await contexts.CreateDbContextAsync();
    await Lock<GasRefund>(db, id);
    …
});
```

It is the read face's lock, in the same words on each database, refused outside a `Db.Transaction` where it is called,
and a `Count`, `LongCount`, `Any` or `All` of it is refused as the query is compiled. It runs on a Rask context — a
`RaskDbContext`, or one built with `AddRaskData()`'s interceptors — which is what writes the lock; an app's own tags
(`TagWith`) before it are kept.

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
`OrderBy(o => o.Customer.Name)` needs nothing declared. Where there is no navigation — a bare id — another read
face can be used inside the LINQ as a lookup, and it runs as a subquery of the same SQL statement, its own
filters (soft delete, tenant) included:

```csharp
Vehicle.AsQueryable().OrderBy(v => VehicleGroup.AsQueryable()
    .Where(g => g.Id == v.VehicleGroupId).Select(g => g.Name).FirstOrDefault());
```

The same works inside a model query's own `Where`/`OrderBy`/`Select`. A lookup EF Core cannot translate throws
an `InvalidOperationException` naming the part it could not translate.

The sort becomes `ORDER BY` and the page becomes `LIMIT`/`OFFSET`, in the database — the table never reaches
memory, however large it is. A synchronous `Count()` and an awaited `ToListAsync()` both work against it;
each is its own short-lived context. The [tutorial's product list](tutorial/02-first-feature.md#2-the-page)
does the same through a query, so the read stays off the render and a write refreshes it.

**EF Core's own operators go on first.** `Include`, `IgnoreQueryFilters` and `AsSplitQuery` are EF Core
extension methods, and EF applies them only to its own query provider — called on the queryable
`AsQueryable()` returns, they do nothing, silently. Put them on the model query, before the hand-off:

```csharp
Product.Include(p => p.Reviews).AsQueryable();        // ✓ travels with the queryable
Product.IgnoreQueryFilters().AsQueryable();           // ✓ soft-deleted rows too

Product.AsQueryable().Include(p => p.Reviews);        // ✗ compiles, loads no reviews
```

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
| `Current.UserId` | the signed-in user's id, or `null` when the work is for nobody — a `Guid?`, or your user's own number ([`int?` / `long?`](#an-app-whose-users-are-numbered)) |
| `Current.RequiredUserId` | the same, or an `InvalidOperationException` when there is none |
| `Current.Principal` | the session's or request's `ClaimsPrincipal` — roles, email, any claim |
| `Current.Claim(type)` | the value of the principal's first `type` claim (`string?`), or `null` when it has none or nobody is signed in |
| `Current.IsInRole(role)` | whether the signed-in user holds `role`; `false` when nobody is signed in |
| `Current.User` / `Current.RequiredUser` | the signed-in user's own row — your `User`'s read face — once it says `IUser` (below) |
| `Current.Tenant` / `Current.RequiredTenant` | the tenant's own row, once your tenant aggregate says `ITenant` — see [your tenant row](multi-tenancy.md#your-tenant-row-currenttenant) |
| `Current.UseUser(id)` | a scope that runs the work for `id` — a test, or a tool acting for a user |
| `Current.UsePrincipal(principal)` | a scope signed in as `principal` — its claims, roles, user and tenant answer everything above; a test with no session around it |

**Where it is set.** For a live session's work (rendering, event handlers), for every HTTP request (a minimal API,
a controller, a CQRS endpoint), and for a background job — which runs for the user who enqueued it, because the
job row records `UserId` and the runner re-enters it. Anywhere else (a hosted service's own loop, startup) it is
`null`, and the `Required…` forms throw rather than let an anonymous write through looking like a signed-in one.

**The user's own row is `Current.User`.** Say which aggregate is your user — Rask.Auth's `Authenticatable` says it
already, so a `User : Authenticatable` needs nothing — and the build generates `Current.User` (its read face,
`UserRead?`) and `Current.RequiredUser`:

```csharp
public sealed class Member : Aggregate<Guid>, IUser { … }   // an app that keeps its users itself

var greeting = $"Hello, {Current.RequiredUser.DisplayName}";
```

It is looked up by `Current.UserId` and is not a query: the row is read as each request, live event and background
job starts, and held per user for the process (per database: two apps or test databases in one process never
share a row) — a save of the user drops it, and another server sees the change
within 60 seconds. A signed-in user whose row is gone throws rather than reading as nobody; read `Current.UserId`
where a missing row is an answer you handle. It is read-only — to change the user, load the aggregate:
`var me = await User.Find(Current.RequiredUserId);`. Two aggregates that say `IUser` are
[RASK113](diagnostics.md#rask113).

Inside a component, inject `IUserProvider` as before — it also raises `Changed` so the UI re-renders on sign-in.
`Current` is for the code that has no constructor to inject into.

### An app whose users are numbered

Users keyed by an `int` or a `long` — an app that kept its own users table long before Rask — need no new line:
the key on the aggregate that says `IUser` is the declaration, and the build generates `Current.UserId` as that
number.

```csharp
public sealed class User : Aggregate<int, PerTenant<int>>, IUser { … }
```

| Member | With `User : Aggregate<int>, IUser` |
|---|---|
| `Current.UserId` | `int?` — the signed-in user's number, or `null` for nobody |
| `Current.RequiredUserId` | `int`, or an `InvalidOperationException` when nobody is signed in |
| `Current.UseUser(12)` | a scope that runs the work for user 12 |

Before, the number was read off the principal by hand; now it is the same `Current` every other app reads:

```csharp
// before
var me = int.Parse(Current.Principal!.FindFirst("id")!.Value);

// after
var me = Current.RequiredUserId;
```

**Set `ClaimTypes.NameIdentifier` to the user's number on every request** — `"12"`, written out — which is where
`Current.UserId` reads it from, in a live session, an HTTP request and a background job alike. The framework's own
rows (a job's, a queued mail's, an outbox message's, a push subscription's) keep recording a `Guid`: the number
travels inside it by the same fixed rule as a [numbered tenant](multi-tenancy.md) — user 12 is
`00000000-0000-0000-0000-00000000000c` — so a job enqueued by user 12 runs as user 12 and no battery table changes.
An app whose users are `Guid`s keeps its `Guid?` `Current.UserId`, and reads a numeric claim as nobody.

`Current.User` reads the user's row across tenants: it is found by the user's own key, so an administrator working
in another tenant (`Tenant.Use(other)`) is still themselves, even when the users table is `PerTenant`.

The generated members live in the global namespace, which C# searches before the namespaces a file imports — so
they win over Rask.Data's `Guid?` one wherever `using Rask.Data;` sits at the top of the file, the placement every
Rask template enforces. A `using` written *inside* a namespace is searched first, and binds the `Guid?` one.

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

## Mapping rules the conventions don't cover

An index, a relationship, a lock, an operation's roles — put them in a **static `Configure`** on the entity
itself. No attribute, no interface, no separate class. (A length, a scale or a required column is not here: the
[value object](domain.md#value-objects) says it, and the column follows.)

```csharp
public sealed class Product : Aggregate<ProductId>
{
    public Sku Sku { get; private set; } = null!;
    public CategoryId Category { get; private set; }

    public static void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasIndex(p => p.Sku).IsUnique("A product with this SKU already exists.");
        builder.HasIndex(p => p.Category).Covering(p => p.Sku);
    }
}
```

**The key.** An entity is keyed by the `Id` it inherits. A `Guid`, or a typed id over one, is the application's —
made in the factory with `XId.New()`, a time-ordered version-7 `Guid`, and Rask's recommended default
([choosing the key](domain.md#choosing-the-key-a-typed-id-over-a-guid)); an `int` or a `long`, or a typed id over one,
is the database's identity, produced on insert, unless `Configure` says `b.Property(x => x.Id).ValueGeneratedNever()`.

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
        c.Properties<decimal>().HavePrecision(32, 16);
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
        c.OrderTextBy("Hungarian_100_CI_AS");
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
- **`ColumnNames`** and **`TableNames`** name every column and table the model did not name itself — what a schema
  another tool made needs, said once instead of property by property:

  ```csharp
  c.ColumnNames(n => n.CamelCase).TableNames(t => t.Class);   // a Prisma schema: countedAt, kmStart; Fueling
  c.ColumnNames(n => n.SnakeCase).TableNames(t => t.SnakeCase); // a PostgreSQL one: counted_at, km_start; delivery_note
  ```

  A column's `n.Path` is the property after the value objects and owned types that hold it (`["Km", "Start"]`), and
  `n.CamelCase` (`kmStart`, `vatNumber` for `VATNumber`), `n.SnakeCase` (`km_start`) and `n.AsDeclared` (EF Core's
  `Km_Start`) are its ready names; `n.Table` is the table it is in. A table's are `t.Class` (the class name, whatever
  its `DbSet` is called), `t.CamelCase`, `t.SnakeCase` and `t.AsDeclared` (EF Core's: the `DbSet`'s name). Return an
  empty string to keep EF Core's. What they leave alone:

  | | named? |
  |---|---|
  | a `HasColumnName`, a [`Columns(n => n.Km, "{Member}Km")` pattern](domain.md#declared-from-a-base-record), `ToTable`, `[Column]` or `[Table]` in `Configure` | **no** — it wins |
  | a value object's columns (`Km_Start` → `kmStart`) | yes, after the whole path |
  | a JSON column (`ToJson()`, a value-object collection) | the column yes, the properties inside it no |
  | an owned type in its owner's table | yes, after its navigation (`depotStreet`); its key stays its owner's column |
  | a hierarchy in one table, its discriminator | the table once, after its root; `discriminator` too |
  | an owned type in a table of its own | yes, after its class (`t.Class` is `Leg`) |
  | a one-value value object inside another (`Price_Currency`) | yes, like its siblings (`priceCurrency`) |
  | a view (`ToView` always names it) | no |
  | the tables of Rask's own batteries (jobs, mail, auth, logs, outbox, …) | **no** — each battery's configuration records its tables, and they keep the names their migrations have; the app's own user class (`AddRaskAuth<TUser>`) is the app's table and is named |

  Both run as the model is finalized but **before** every other rule that reads a name — so `ConstraintNames`, a
  unique index's filter and a full-text table see `countedAt` whatever order `Conventions` states them in. They work
  on every provider.
- **`DeleteBehavior(children:, others:)`** says what a delete does: `children` for an aggregate's
  [children](domain.md#children) (Rask's own rule is `Cascade`), `others` for every other foreign key. `ClientCascade` keeps
  the children going with their aggregate while the database's key says `NO ACTION` — what a schema another tool made
  usually has.
- **`OrderTextBy(collation)`** names, once, how the app's database compares text — by its SQL Server collation — and
  a query asks for that order with `InTextOrder()`. It runs in the database, through `ToList`, `Count` and a grid's
  paging alike; a list already in memory orders the same way with `TextOrder.In("hu")`
  (see [Localization](localization.md#dates-numbers-and-text-order)). What each database does with it:

  | | `OrderBy(x => x.Name.InTextOrder())` | `Where(x => x.Name == "csaba")`, a unique index, `Contains`/`StartsWith`/`EndsWith` |
  |---|---|---|
  | **SQL Server** | `ORDER BY [Name] COLLATE Hungarian_100_CI_AS` | as the database's own collation says — no column is given one, so no migration |
  | **SQLite** (`UseRaskSqlite`, or a browser's `UseSqlite(…).UseRaskFullTextSearch()`) | `ORDER BY "Name" COLLATE "RASK_TEXT"` | as SQL Server would: case-insensitive, accent-sensitive, Hungarian |
  | **PostgreSQL 18+** (`UseRaskPostgres`) | `ORDER BY "Name" COLLATE rask_text` | as SQL Server would, through a nondeterministic ICU collation |
  | a plain `UseNpgsql` | `ORDER BY "Name" COLLATE "hu-HU-x-icu"` | case-sensitive, as the column is |
  | a plain `UseSqlite` with nothing of Rask's | by the column as it is | as SQLite compares |

  On SQLite every connection `UseRaskSqlite` opens registers the collation's language and sensitivities (Hungarian →
  `hu-HU`; `CI` ignores case; `AS` keeps `á` apart from `a`; no `KS`/`WS` ignores kana and width) as
  [`RASK_TEXT`](sqlite.md#text-that-compares-as-sql-server-does), every text column of the model is created with it,
  and `instr` and `LIKE` — what EF writes `Contains`, `StartsWith` and `EndsWith` as — compare the same way. So a test
  on SQLite finds `Csaba` for `"csaba"`, `Árvíztűrő` for `Contains("árv")`, and a unique index refuses `CSABA` beside
  `Csaba` — what the app's SQL Server does. A column given a collation of its own (`UseCollation`) keeps it. A
  collation name Rask cannot read (an unknown language) is refused as the model is built, off SQL Server.

  On PostgreSQL through `UseRaskPostgres`, a collation that ignores case or accents becomes a *nondeterministic* ICU
  collation, `rask_text` (`CREATE COLLATION rask_text (PROVIDER = icu, LOCALE = 'hu-HU-u-ks-level2', DETERMINISTIC =
  false)` — `ks-level2` ignores case, `ks-level1` accents too), which the migration (or `EnsureCreated`) creates before
  the first table that uses it, and every text column is created with it: `=`, a unique index, `ORDER BY` and `LIKE`
  (`Contains`, `StartsWith`, `EndsWith`) compare as SQL Server's do. **It needs PostgreSQL 18 or later**: `LIKE` refuses
  a nondeterministic collation before 18, so on an older server the migration stops at that statement and says so,
  rather than every search failing later. The collation is made `IF NOT EXISTS` — a database whose collation was made
  under another locale keeps it until it is dropped. **A text key is binary**: a primary or alternate key column and a
  foreign key column of text get `COLLATE "C"` instead, so an id compares byte for byte, its index deduplicates, and a
  foreign key matches its principal by an equality no ICU rule can make ambiguous — ids are generated, never typed in
  two cases. A trailing space is not padded away as SQL Server pads it (`'ABC' = 'ABC '` is true there, false under
  `rask_text` and SQLite's `RASK_TEXT`), so a text that ends in one compares differently there. A collation that tells case and accents apart
  (`Hungarian_100_CS_AS`) needs none: `InTextOrder()` orders by the language's ICU collation (`"hu-HU-x-icu"`), as it
  does through a plain `UseNpgsql`, which every server built with ICU (the official images are) imports.

  PostgreSQL refuses every substring search by a nondeterministic collation but `LIKE` — `strpos`, `replace`, the
  regular expressions and `ILIKE` fail with *nondeterministic collations are not supported* — and Npgsql writes several
  string members as those. So on a column in `rask_text` (or `rask_search`, `x.Loosely()`'s), `UseRaskPostgres` writes
  them as below; *the language's collation* is its deterministic ICU one (`"hu-HU-x-icu"`; `"und-x-icu"` for
  `rask_search`). A column given a deterministic collation of its own is left to Npgsql, and so is a plain `UseNpgsql`.

  | On PostgreSQL through `UseRaskPostgres` | written as | compares | vs SQL Server / SQLite (`Hungarian_100_CI_AS`) |
  |---|---|---|---|
  | `Contains` / `StartsWith` / `EndsWith` of a constant or a parameter | Npgsql's `LIKE @p`, the value's `%`, `_` and `\` escaped | by `rask_text`: case aside, accents as the collation says | the same |
  | the same of another column or an expression | `LIKE '%' \|\| (escaped value COLLATE rask_text) \|\| '%' ESCAPE '\'` — not `strpos`, `left`, `right` | by `rask_text`, `%`/`_` in the value literal | the same |
  | `IndexOf(string)` / `IndexOf(char)` | `strpos(lower(x COLLATE "hu-HU-x-icu"), lower(v COLLATE "hu-HU-x-icu")) - 1` (no `lower` when the collation tells case apart) | case aside, **accents always counted** | the same for an `_AS` collation; under an `_AI` one SQL Server and SQLite also ignore accents |
  | `Replace` | `replace(x COLLATE "hu-HU-x-icu", …) COLLATE rask_text` | the exact value, **case counted** | as SQLite; SQL Server's `REPLACE` matches case aside |
  | `Regex.IsMatch` / `Replace` / `Count`, `EF.Functions.ILike` | Npgsql's, the text `COLLATE "hu-HU-x-icu"` | as .NET's `Regex` and PostgreSQL's `ILIKE` do | PostgreSQL only |
  | `==`, `ToUpper()`/`ToLower()`, `Trim`, `Substring`, `Length`, `OrderBy` | unchanged | by `rask_text` | the same |

  A text `Replace` or a regular expression answers is given `rask_text` back, so `x.Name.Replace(a, b) == "KOVÁCS"`
  still compares case aside. `lower()` maps `İ` to two characters, so `IndexOf` past one is off by one — Hungarian has
  none.

  ```csharp
  // before — the collation repeated in every query
  .OrderBy(x => EF.Functions.Collate(x.Name, "Hungarian_100_CI_AS"))
  // after — named once, in Conventions; the query says what it means
  c.OrderTextBy("Hungarian_100_CI_AS");
  .OrderBy(x => x.Name.InTextOrder())
  ```
- **`x.Loosely()`** is the text compared **case and accents aside**, in no one language's order — what a search box
  wants beside the app's own order: `Driver.Where(d => d.Name.Loosely().Contains(typed))` finds `Kovács` for
  `"kovacs"`, and `"c"` in `Csaba`, which the Hungarian order (where `cs` is one letter) would not. It needs no
  convention and changes only the query it is written in — no column, no index, no migration on SQL Server and SQLite:

  | | written as | `Contains` / `StartsWith` / `EndsWith` |
  |---|---|---|
  | **SQL Server** | `[Name] COLLATE Latin1_General_100_CI_AI` | by that collation |
  | **SQLite** (`UseRaskSqlite`, or a browser's `UseRaskFullTextSearch()`) | `"Name" COLLATE "RASK_SEARCH"`, registered on every connection | `rask_search(…)`, the same comparison (SQLite's `instr`/`LIKE` see no collation) |
  | **PostgreSQL 18+** (`UseRaskPostgres`) | `"Name" COLLATE rask_search`, a nondeterministic ICU collation (`und-u-ks-level1`) | `LIKE` by it |
  | anything else | refused, naming the options that have it | |

  `==` and `OrderBy` on it compare the same way. On PostgreSQL the collation is created `IF NOT EXISTS` at the start of
  every migration (and `EnsureCreated`); a database migrated before Rask had it gets it with its next migration.

  ```csharp
  // before — one database's collation, which SQLite has never heard of
  .Where(d => EF.Functions.Collate(d.Name, "Latin1_General_100_CI_AI").Contains(typed))
  // after
  .Where(d => d.Name.Loosely().Contains(typed))
  ```
- `ConstraintNames` and `DeleteBehavior` are applied as the model is finalized, so they see every table — **and an
  entity's `Configure` still wins**: a `HasDatabaseName`, `HasConstraintName` or `OnDelete` written there is kept as
  written. A unique index's [message](#what-a-unique-indexs-violation-says) and a
  [referenced delete's sentence](#what-deleting-a-referenced-row-says) find their index or key under the name the rule
  gave it.
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
`Save()`, `Create`, `Update`, and plain `SaveChangesAsync` — and an `ExecuteUpdateAsync` that writes a duplicate,
whose rows are no form's, so its failure is the root's: over the index's fields.

It carries ONE failure for the index (`Failures`, the shape a form reads): the message, over every property the
index names in index order — `TenantId` aside, which is never a field — and the index's name as its source.
`Errors`, the shape that crosses the wire, repeats the message under each of those fields.

| The index is over | `Failures` | `Errors` |
|---|---|---|
| `(Name, TenantId)` | one, over `Name` | `"Name"` |
| `(Year, Number)` | one, over `Year` and `Number` | `"Year"` and `"Number"`, the same message under both |
| `Km.Start`, a member of a complex property | one, over `Km.Start` | `"Km.Start"` |
| `Number` on a child row of an aggregate | one, over that row: `Pistols[1].Number` | `"Pistols[1].Number"` |
| `NormalizedPlate`, declared `.Field("Plate")` | one, over `Plate` | `"Plate"` |

- **The field is the form's name for it.** A member of a complex property is named by its path (`Km.Start`, not
  `Start`). A child row an aggregate holds is named by its place in the collection, as a form's list names its rows
  (`Pistols[1].Number`) — so the message is shown under that row, not in a toast. When the error does not say which
  of several new or changed rows collided, the message goes under each of them. A plain EF Core graph that no
  aggregate holds keeps the bare property name.
- **`Field("…")` says it outright**, for an index whose column is not what the form edits — a normalised copy, a
  computed key — or one over several properties of which the form shows one:

  ```csharp
  builder.HasIndex(v => v.NormalizedPlate).IsUnique("A vehicle with this plate already exists.").Field("Plate");
  ```

  It replaces the index's properties as the field — a child row still carries its row — and the checks
  [before a save](#when-the-index-may-not-be-there) name the same field.

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
- an index filter that only says its own columns are `NOT NULL` counts as no filter; an index filtered with
  [`Where(…)`](#computed-columns-defaults-and-filtered-indexes-in-c) is asked over exactly the rows its C# keeps, and
  a row it leaves out (a revoked grant) is not checked at all; an index with any other filter is skipped, because its
  condition is SQL that Rask will not guess at
- two rows of one save that break a rule between them are refused too
- the row being saved does not collide with itself

The check and the save share one transaction — the caller's when there is one, otherwise one opened for the
save (not on a context whose execution strategy retries, which refuses a transaction it did not start; there
the check runs just before the save). **It is a check, not a lock:** two saves at the same instant can both
pass it, and only a real unique index closes that. Where the index does exist the database still has the last
word, reported the same way. An index declared with plain `IsUnique()` has no message to fail with and is not
checked.

### An index that answers the query by itself

A query that finds rows by one column and reads a few others can be answered from the index alone, if the index
carries them. Say which, on the index — once, for every database:

```csharp
builder.HasIndex(p => p.PayeeId).Covering(p => new { p.Amount, p.Note });   // or Covering(p => p.Amount)
```

- **SQL Server** writes `CREATE INDEX … ([PayeeId]) INCLUDE ([Amount], [Note])` — EF Core's own
  `IncludeProperties`, so the model snapshot and the migration read exactly as if you had written that.
- **PostgreSQL** writes `… ("PayeeId") INCLUDE ("Amount", "Note")`.
- **SQLite** has no included columns: the index is its key columns alone, and nothing of the declaration reaches
  the model or a migration — an app that runs its tests on SQLite carries no SQL Server call into them.
- The covered columns are carried, not ordered by: they are no part of the index's key, and a unique index stays
  unique over its keys alone.
- A value object of several members stored in columns of its own is covered by each of them:
  `Covering(l => l.Km)` over a `KmRange(int Start, int End)` named `StartKm` and `EndKm` writes
  `INCLUDE ([EndKm], [StartKm])` — the columns as they are named, `Columns(…)` patterns included. One member of it
  is `Covering(l => l.Km.Start)`, or `Covering("Km.Start")`.
- A covered member that is not a mapped property of the entity (a navigation, a value object stored as JSON, an
  ignored member), or one that is already a key of the index, fails the model build with its name.

It is read as the model is finalized by every `RaskDbContext`. A plain `DbContext` gets it by calling
`ModelRegistry.ApplyConventions(configurationBuilder)` in its `ConfigureConventions`.

### Computed columns, defaults and filtered indexes, in C#

A model that says `HasComputedColumnSql("CONCAT(Year,'.',Month)")`, `HasDefaultValueSql("CURRENT_TIMESTAMP")` or
`HasFilter("[TeaorId] IS NOT NULL")` is written in one database's SQL, and the SQLite the tests run on reads it
differently or not at all ([RASK177](diagnostics.md#rask177) says so under `<RaskStrict>`, as it does for every other
API only one database understands). Say the same three things in C#, and Rask writes each provider's own SQL as the
model is finalized:

```csharp
static void Configure(EntityTypeBuilder<Fueling> b)
{
    b.Computed(f => f.CountedAt, f => f.ModifiedPistol ?? f.Pistol);   // a column the database computes
    b.Computed(f => f.Period, f => f.Year + "." + f.Month);
    b.Default(f => f.Size, 1000);                                       // what a row written without it gets
    b.Default(f => f.CreatedAt, () => Clock.Now);                       // the database's clock, UTC
    b.Default(f => f.SentAt, () => Clock.Wall);                         // its wall clock, in the server's zone
    b.StoredDefault(f => f.HardwareNumber, 0);                          // as the column stores it, past the value object
    b.HasIndex(f => f.Partner).IsUnique("This partner has a live grant.").Where(f => f.RevokedAt == null);
}
```

**`Computed(property, value)`** is a stored computed column: `PERSISTED` on SQL Server, `GENERATED ALWAYS AS (…)
STORED` on PostgreSQL and SQLite. The database keeps it with the row, it can be indexed, and a save reads it back
into the entity (give the property a `private set`). The value is a closed subset, chosen because it means the same
on all three databases as it does in C#:

| Takes | Written as |
|---|---|
| the entity's mapped members, a value object's too (`f.Amount.Value`, a one-value one stored in a column of its own included) | the column |
| constants, captured fields, enums, strongly-typed ids | a literal, as the column stores it |
| `a ?? b`, `x.GetValueOrDefault()`, `x.GetValueOrDefault(v)` | `COALESCE` |
| `+ - * / %` and unary `-` on whole numbers and doubles; `Math.Abs` | the same operators; `ABS` |
| text joined with `+` (or `string.Concat`), whole numbers among it | `+` on SQL Server, `\|\|` elsewhere; a number `CAST` to text, a null as no text — as C# joins it |
| `== != < <= > >=`, `&&`, `\|\|`, `!`, `x.HasValue` | a condition that is true or false, never unknown: `a == b` is true for two nulls and `a != 5` for a null, as in C# |
| `c ? a : b`, and a condition in a value's place | `CASE WHEN … END` |

Anything else fails the model build, naming the node and the declaration: a method call (`ToUpper()`), `.Value` off a
nullable (C# throws on a null, SQL does not — write `??`), arithmetic on a `decimal` (SQLite computes it as a double),
a `double` or a date joined into text (each database writes it its own way), and a property outside the row
(`DateTime.Now` would be read once, as the model is built). Text compares as the column's collation does
([`OrderTextBy`](#app-wide-model-rules-one-iraskmodel)); integer overflow and division by zero are the database's.

**`Default(property, value)`** is the database's default: it fills a row written by something other than Rask — a
script, another app — and the rows a migration finds when it adds the column. **Rask always writes the value the
entity holds**: a `Size` of `0` is saved as `0`, where EF Core's own `HasDefaultValue` would leave a column holding
its type's default to the database and save `1000`. The entity's own starting value is its initializer or its
factory's. The constant is written as its column stores it — an enum's number, a strongly-typed id's key, a value
object's value — and it is written as **SQL**, `HasDefaultValueSql("1000")`, never EF Core's `HasDefaultValue`: a
model moving from a hand-written `HasDefaultValueSql("1000")` (or `"0"` for a SQL Server `bit`, which Rask writes bare)
to `Default(x => x.Size, 1000)` makes a migration with nothing in it. On SQL
Server, where a default is a named constraint, an app's
[`ConstraintNames`](#app-wide-model-rules-one-iraskmodel) rule names it (`ConstraintKind.Default`, the clock's too), so
`{table}_{column}_df` needs no `HasDefaultConstraintName`; PostgreSQL and SQLite defaults have no name.

**`StoredDefault(property, stored)`** gives the default as the column stores it, for the one a value object would
refuse — a legacy schema's `0` for a `HardwareNumber(int Value) : Integer(Value, Min: 1)`:
`b.StoredDefault(p => p.HardwareNumber, 0)`. The type is checked as the model is built: an `int` column takes an `int`.

`Default(x => x.CreatedAt, () => Clock.Now)` (or `() => Clock.Now.UtcDateTime` for a `DateTime`) is **the database's
clock, in UTC**: `SYSUTCDATETIME()` on SQL Server (`TODATETIMEOFFSET(…, 0)` for a `DateTimeOffset`), `now()` on
PostgreSQL, `strftime('%Y-%m-%d %H:%M:%f', 'now')` on SQLite — the text a `DateTime` is stored as there. It is the
moment the database writes the row, on the database server; `Clock.Fake` does not move it. A row Rask writes takes
its moment from the entity, which reads `Clock.Now` — an aggregate's `Timestamps` are stamped that way.

`Default(x => x.SentAt, () => Clock.Wall)` is **the database's wall clock**, for a `DateTime` column (or a value object
over one, `WallMoment(DateTime Value)`) that keeps moments as the clock showed them: `CURRENT_TIMESTAMP` on SQL Server
(what a legacy schema's default usually says), `LOCALTIMESTAMP` on PostgreSQL, `strftime(…, 'now', 'localtime')` on
SQLite. It is the local time **of the zone the database server runs in** — the database does not know the app's
`Clock.Zone`, so it is the app's wall clock only when the server runs in that zone (`TZ=Europe/Budapest`). A
`DateTimeOffset` refuses it, as the model is built. Any other lambda is refused where it is written; `Clock.Today` too.

**`HasIndex(…).Where(rows)`** is a filtered index on SQL Server and a partial one on PostgreSQL and SQLite; a unique
one holds among the rows the filter keeps — one live grant per partner, however many revoked. The filter is the
plainest condition, because SQL Server's filtered index takes no other:

- columns compared with constants or `null`, joined by `&&`; a boolean column as itself (`x => x.Active`) or with `!`
- a one-value value object is its column: `x => x.Number.Value > 0` compares the number it holds, and
  `x => x.Number != FuelingNumber.Unset` writes the constant as the column stores it — both `[Number] > 0`-shaped, the
  same text a hand-written `HasFilter` had
- `||`, two columns compared, and a `!=` on a nullable column fail the model build — C# keeps the nulls for
  `x.Level != 3`, a filtered index leaves them out, so say it in both: `x.Level != null && x.Level != 3`
- on SQL Server a unique index also leaves out the rows with a NULL in a nullable key column, as EF Core's own unique
  index there does, so NULLs never collide on any of the three

All three are read as the model is finalized by every `RaskDbContext` (a plain `DbContext` calls
`ModelRegistry.ApplyConventions`), and the model a migration is made from carries the provider's SQL alone —
`HasComputedColumnSql`, `HasDefaultValueSql`, `HasFilter` — as if you had written it. A provider other than the
three refuses the model; say it in that provider's own SQL there.

### A key from a sequence: `FromSequence`

```csharp
static void Configure(EntityTypeBuilder<Provider> b) => b.Property(p => p.Id).FromSequence("Providers", start: 1000);
```

The key of each new row is taken from a database sequence as the row is added — before its insert — so an aggregate
that derives something from its own key (`"p" + Id`) saves once, and a key the app worked out as `MAX + 1` and
retried on a collision is no longer needed. It is a real sequence on SQL Server and PostgreSQL, made by the
migration (the name defaults to the entity's and `Ids`); SQLite has none, so there it is a row of Rask's own
`__RaskSequences` table, advanced in one statement under SQLite's writer lock and started above the highest key the
table already holds. An `int` or `long` key, or a strongly-typed id over one.

### A member behind a gate: `Gated`

```csharp
static void Configure(EntityTypeBuilder<Driver> b) => b.Gated(d => d.Card, Module.Tachograph);

services.AddRaskGate<Module>((scope, module) => scope.GetRequiredService<Modules>().IsOn(module));
```

A screen that hides a field from someone without the module must not clear what someone with it stored. With the
gate shut for the work in flight, every save — a generated `Update(id, model)`, an operation, an import — leaves a
gated member as it was stored, and a row added then stores nothing there; the rest of the row is written as usual.
Only an optional member can be gated. Whether a gate is open is the app's to say, once, per kind of gate; a save
that writes a gated member with no rule for its kind is refused rather than guessed at.

The generated forms leave it out too. While the gate is shut, `Ui.Field.Bind(() => m.Card)` draws nothing — on the
aggregate's form model (`Driver.Create(model)`, `Update(id, model)`) and on a `Form.For` over an operation whose
parameter writes the gated member (`Describe(name, card)`, matched by name) — and the operation's form does not read
what the field holds: a `Change<T>` parameter is kept, any other is given `null`, and the save keeps what is stored
either way. A form asks as it renders (`GeneratedGates.IsShut`), so a gate that opens for the signed-in tenant shows
the field on the next render. A read face hands back no gated member while its gate is shut — `Driver.Where(…)`
empties `Card` on every row, so neither a page nor the wire to a browser carries it (a `Select` projection is its
own shape and is not touched). The gated members are declared at startup from the `Configure` as written — a gate
that is an enum member or a reachable static — so all of this holds before the EF model is first built.

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
the context does this: `Trailer.Delete(id)`, plain `db.Remove(…)` + `SaveChangesAsync`, and
`db.Set<Trailer>().Where(…).ExecuteDeleteAsync()`.

Which sentence, in this order:

| The database says | The failure carries |
|---|---|
| which constraint (PostgreSQL, SQL Server), and it is a relationship in the model | that relationship's sentence, else the entity's `WhenReferenced` |
| which constraint, and the model does not know it — a table that is not mapped | the entity's `WhenReferenced` |
| nothing (SQLite) — Rask asks which mapped relationship still holds a row, and one does | that relationship's sentence, else the entity's `WhenReferenced` |
| nothing (SQLite), and no mapped relationship holds a row — a table that is not mapped | the entity's `WhenReferenced` |
| anything, and no sentence fits | the provider's exception, untouched |

- **SQLite answers as SQL Server and PostgreSQL do**, so an app whose tests run on SQLite alone sees the same
  sentence and the same `Source` (the constraint's name). SQLite's `FOREIGN KEY constraint failed` names no
  constraint, so after a refused delete Rask asks, on the same connection and in the same transaction, ONE
  statement with a cheap `EXISTS` per relationship that can refuse (not `Cascade`, not `SetNull`) — those with a
  sentence first — over the keys of the rows the delete was removing. Only a failed delete pays for it.
- **An `ExecuteDelete` is answered the same way**, from the statement that failed: the table it deleted from, and
  — on SQLite — its own `WHERE`, run again to select the keys it was deleting.

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
- **The other side — a key that points at no row of the tenant in flight — is refused before the database is
  asked**, for a `PerTenant` row: a field error under the key, in the relationship's `WhenMissing("…")`, else the
  referenced entity's `b.WhenMissing("…")`, else the catalog's `ValidationNotFound`
  ([multi-tenancy.md](multi-tenancy.md#a-reference-names-a-row-of-the-same-tenant)).

### How long between two dates, in a query

`end - start` between two `DateTime`s or two `DateTimeOffset`s is a `TimeSpan` in C#, and a query can order by it,
compare it with a `TimeSpan` and read its `TotalHours` the same way on every database:

```csharp
// before — SQL Server's own function, which SQLite and PostgreSQL do not have
.OrderBy(n => EF.Functions.DateDiffSecond(n.StartDate, n.EndDate))
// after — the length of the trip, on every provider
.OrderBy(n => n.EndDate - n.StartDate)

var slow = await DeliveryNote.Where(n => n.EndDate - n.StartDate > 90.Minutes);
var hours = await DeliveryNote.AsQueryable().Select(n => (n.EndDate - n.StartDate).TotalHours).ToListAsync();
```

PostgreSQL's provider translates all of it itself. SQL Server's and SQLite's translate none of it, so
`UseRaskSqlServer` and `UseRaskSqlite` add the translation (a plain `UseSqlServer`/`UseSqlite` does not):

| Database | `n.EndDate - n.StartDate` is written | Exact to |
|---|---|---|
| SQL Server | `DATEDIFF_BIG(microsecond, [StartDate], [EndDate])` | the microsecond |
| SQLite | `CAST(round((unixepoch([EndDate], 'subsec') - unixepoch([StartDate], 'subsec')) * 1000000.0) AS INTEGER)` | the millisecond |
| PostgreSQL | `"EndDate" - "StartDate"` (an `interval`) | the microsecond |

The length is a count of microseconds, so a `TimeSpan` on the other side of a comparison is sent as the same count,
and a projection of the length itself reads a `TimeSpan` back. `TotalDays`, `TotalHours`, `TotalMinutes`,
`TotalSeconds` and `TotalMilliseconds` divide it down in the database. A `DateTimeOffset` is measured in UTC, as its
subtraction is; a nullable date makes a nullable length, which no comparison matches. SQLite stores a date as text and
reads three fractional digits of it, hence the millisecond. A date column stored through a value converter of the
app's own (ticks, Unix time) is left untranslated — EF Core says so — rather than measured wrong.

### Dates and moments: what each database keeps

A `DateTime` and a `DateTimeOffset` mapped by EF Core come back, compare and order the same on SQL Server and on SQLite
through `UseRaskSqlite` — which is what lets an app whose production database is SQL Server test on SQLite in memory.
PostgreSQL differs in three ways it cannot be made not to. The parity suite (`DateTimeParityTests` in
`tests/Rask.Providers.E2E.Tests`) pins every row of this table on all three:

| | SQL Server (`datetime2(7)`, `datetimeoffset(7)`) | SQLite (`UseRaskSqlite`) | PostgreSQL (`timestamptz`) |
|---|---|---|---|
| Smallest step kept | a tick (100 ns) | a tick (stored as text, seven fractional digits) | a microsecond: the seventh digit is rounded away |
| A `DateTime` reads back as | `Kind = Unspecified` | `Kind = Unspecified` | `Kind = Utc`, and writing one that is not UTC is refused |
| A `DateTimeOffset` keeps its offset | yes | yes | no: it is written in UTC alone (Npgsql refuses another offset, in a value **or** a query parameter) and read back in UTC |
| Order, `=`, `<`, `Min`/`Max` on a `DateTimeOffset` | by instant | by instant | by instant |
| A unique index on a `DateTimeOffset` | refuses the same instant in another offset | refuses it too | refuses it too |
| A `Moment` value object (a wall-clock `DateTime`, `Kind = Unspecified`) | kept to the tick, orders by its time | kept to the tick, orders by its time | kept to the microsecond, orders by its time: `UseRaskPostgres` stores it as the UTC instant it is in `Clock.Zone` and reads it back onto the wall clock |

SQLite has no date type, and EF Core's provider stores a `DateTimeOffset` as text with its offset — then refuses
`OrderBy`, `<`, `>` and `Max` over it, because the text's order is not the time's. `UseRaskSqlite` keeps the text as
EF Core writes it (`2026-03-04 10:00:00.1234567+01:00`, every tick and the offset) and creates the column
`COLLATE "RASK_MOMENT"`, which compares two values by their UTC instant: `10:00+01:00` and `09:00+00:00` are one
moment, as on SQL Server. A property the model gives a converter of its own (`DateTimeOffsetToBinaryConverter`,
Unix time) keeps that column. A `DateTime` needs nothing: its text, written to the tick in one format, orders as the
time does. An existing SQLite database with `DateTimeOffset` columns gets the collation through one migration
(`rask db add MomentOrder`); the stored values do not change.

A `Moment` value object is the same moment on all three. Npgsql writes a `DateTime` to `timestamptz` only as UTC, so
`UseRaskPostgres` gives every column of one a conversion: the wall-clock time is turned into its UTC instant through
`Clock.Zone` as it is written, and back onto the wall clock as it is read — the value object and every query see the
wall-clock time (`Where(f => f.Moment.Value > since)` included), the column holds UTC. A wall time the clock skips
when summer time begins is stored as the instant just after the jump; one it shows twice in autumn, as its
standard-time instant. A bare `DateTime` property is left as Npgsql maps it.

### Decimals: what each database keeps

A `decimal` with a declared precision (`HasPrecision(18, 2)`, a value object's `Scale`/`Precision`) is held alike on
all three — `DecimalParityTests` in `tests/Rask.Providers.E2E.Tests` pins it:

| | SQL Server (`decimal(p, s)`) | SQLite (`UseRaskSqlite`) | PostgreSQL (`numeric(p, s)`) |
|---|---|---|---|
| Digits kept | to the scale | to the scale (stored as text) | to the scale |
| More places than the scale | rounded half away from zero (`1.005` → `1.01`) | rounded the same, as it is written | rounded the same |
| More whole digits than `p - s` | the save fails (`DbUpdateException`) | the save fails (`DbUpdateException`) | the save fails (`DbUpdateException`) |
| `Sum`, `Min`/`Max`, `+`, `*`, `<`, order | exact | exact | exact |

SQLite has no decimal type, and EF Core's provider keeps every digit of the text it writes; `UseRaskSqlite` gives such
a column a converter that rounds the number to its scale and refuses one too large, so a test on SQLite holds the
number production would. A column with no precision, a store type written for one database, or a converter of its own is left as EF Core
maps it.

### The conformance suite

`tests/Rask.Providers.E2E.Tests` holds one suite per behaviour, written once and run on SQLite, SQL Server and
PostgreSQL (`scripts/run-providers-local.sh`; the `providers` gate, in CI on Testcontainers — locally the server legs
skip unless `RASK_PROVIDERS_LOCAL=1`): text order and case-insensitive matching
(`TextOrderParityTests`), dates (`DateTimeParityTests`), durations (`DurationQueryTests`), decimals
(`DecimalParityTests`), JSON columns (`ValueCollectionParityTests`), computed columns, defaults and filtered unique
indexes (`NeutralModelTests`), `Db.Lock` (`DbLockTests`), a parent read `LockRead.ForUpdate`
(`ParentLockParityTests`), a read face's `ForUpdate()` (`ReadForUpdateParityTests`), and the sentences of a unique index and of a referenced delete — through a save,
`ExecuteUpdate` and `ExecuteDelete` (`UniqueViolationParityTests`, `ReferencedDeleteParityTests`). What passes there
is what lets an app run its own tests on SQLite in memory alone.

A server leg exists only where Rask writes provider-specific SQL or translates a provider's error: collations, row and
named locks, sequences, the DDL the conventions write, decimal scale, constraint violations, and migrations' and the
batteries' own SQL. What is the same on every provider — audit rows, the bulk writes' rules, read-face shapes, value
objects and collections EF Core translates — runs on SQLite alone.

## One holder at a time, across hosts: `Db.Lock`

Some work must never run twice at once however many hosts the app runs on — talking to one pump, closing one
month. `Db.Lock(name)` takes a lock the **database** keeps, held until you dispose it:

```csharp
await using var turn = await Db.Lock("pump:" + pumpId).For(2.Minutes);
await pump.Talk();
```

- **`.For(wait)`** is the most it waits for the current holder — a step at the end, as every timing in Rask is. With no
  step, `await Db.Lock("pump:7")` waits as long as it takes; `.For(TimeSpan.Zero)` does not wait at all. A wait that
  runs out throws a `TimeoutException` naming the lock and the wait — catch it to answer "busy, try
  again". The call's cancellation token (or the work it runs in) cancels the wait.
- **It is its own connection's**, kept open while the lock is held and never the one of a `Db.Transaction` in
  progress — so a lock taken inside a block outlives its commit or rollback, and a host that dies gives the lock back
  with its connection. Disposing twice lets go once.
- **SQL Server** takes it with `sp_getapplock` (owned by the session, names up to 255 characters), released with
  `sp_releaseapplock`. **PostgreSQL** takes `pg_advisory_lock(hashtextextended(name, 0))` — a 64-bit hash, so two names
  would share one lock only if they hashed alike (about once in 2⁶⁴; the 32-bit `hashtext` before it collided at a
  few tens of thousands of names) — bounded by `lock_timeout`, which is put back as it was. **SQLite** has no lock across
  connections and a SQLite app runs on one host, so there the lock is the process's own, one per database file and
  name. Any other provider throws `NotSupportedException`.

**Several names at once** — the chip codes a driver's save touches, say — go in one call, never in a loop:

```csharp
await using var chips = await Db.Lock(driver.Chips.Touched.Select(c => "chip:" + c.Code)).For(30.Seconds);
```

Rask takes them one after another in **ordinal order** (on PostgreSQL, the order of their `hashtextextended`, the
locks it really takes), whatever order you gave them in — so two saves that share some codes can never each hold one the other
waits for. A name given twice is taken once, and an empty list takes nothing. `.For(wait)` is the whole call's wait,
not each name's. All or none: when one is not granted, the ones already taken are let go before the
`TimeoutException`. Disposing the handle lets go of every one.

## Optimistic concurrency

A `Versioned` aggregate's `Version` is an EF Core concurrency token, bumped on every save that changes the
aggregate or any of its children. The writes check it for you, against the version the caller **read** — a freshly
loaded row's version is whatever the database holds now, which would make every check pass:

- an operation's entry takes it: `Supply.Change(id, version, …)` is `Find(id, version)`, the method and `Save()`;
- `Find(id, version)` refuses an aggregate that has moved on before anything is changed, and `Save()` compares the
  version `Find` read;
- `Update(id, model)` compares the `Version` the form model carries, and `Update(id, p => …)` and `Delete(id)` take a
  `version:`.

A save that lost the race writes nothing and is a `RaskValidationException` about the whole form — *Someone else
changed this in the meantime. Reload it and try again.* — which a form shows at its top with the reader's changes
still on screen ([find, change, save](domain.md#find-change-save)). A test proves it with
[`Test.Concurrently`](testing.md#two-at-once-testconcurrently).

## Using EF Core the usual way

An app that already has a `DbContext` of its own — a schema another tool made, tables a library maps — keeps it,
and its aggregates work through it exactly as through Rask's: the read faces, the operations' entries, the rules and
the interceptors. A class that does not derive from `Entity<TId>` stays an ordinary EF Core entity of that context.

Registering an `IDbContextFactory<YourContext>` is all it takes at the app level. Rask binds
the model surface and every database-backed battery to the context you registered, and
`RaskAppDbContext` is never constructed. Call `modelBuilder.ApplyRaskConventions(this)` from its
`OnModelCreating` to keep the query filters (soft delete where an aggregate opts in, and the
[tenant](multi-tenancy.md)) and the concurrency tokens, or `ModelRegistry.Apply(modelBuilder)` to keep every
declared entity mapped as well. A context with a tenant-scoped table implements `ITenantScoped`, as
`RaskDbContext` does; the parameterless `ApplyRaskConventions()` is for one without.

### Named sets: `db.Orders`

Every mapped entity also gets an accessor on `DbContext`, beside the `Set<T>()` that always worked:

```csharp
await using var db = await contexts.CreateDbContextAsync(ct);

var order = await db.Orders.FindAsync([id], ct);   // children come with it
if (order!.TotalAmount > limit) { order.Hold(); }

await db.SaveChangesAsync(ct);
```

`db.Orders`, `db.OrderLines` — children too, since they are in the write context, but a save that writes a child
it reached that way [without its root is refused](domain.md#children). These are extension members on `DbContext` itself, so an app that brings its
own context gets them without the context having to be `partial`.

The name comes from one documented rule — `s`, `es` after `s`/`x`/`z`/`ch`/`sh`, and `y` → `ies` after a
consonant — and nothing cleverer, because an irregular guess is worse than a predictable one. A name two
entities want, or one `DbContext` already declares, is [RASK090](diagnostics.md#rask090) rather than a silent
rename.

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
`Tenant.Use(id)`, or clear the tenant with `Tenant.None()`; `Current.UsePrincipal(principal)` signs a whole principal in
instead — claims, roles, user and tenant read from it as from a request's.

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
| `CreatedAt` and `UpdatedAt` | neither, or one | name nothing, or `Created` / `Updated` for the one it has — on the root; a [child](domain.md#children) names it on `Entity<TId, …>` |
| a `Version` concurrency token | none | name nothing — a write then takes no version, and the last writer wins |
| a delete | a hard one | `Deletable`; `SoftDelete` only for a table with a `DeletedAt` column; neither, and there is no `Delete` |
| a table named after the class | a plural, or a name of its own | `b.ToTable("Destinations")` in `Configure` |
| an integer key the database numbers | a key the application numbers | `b.Property(d => d.Id).ValueGeneratedNever()` in `Configure` — the aggregate then takes its key in [`Create(id, model)`](domain.md#writing-create-update-delete) |
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

**Neither creates, migrates or checks anything** — until you say the context's migrations own the schema. No
connection is opened until the first read, and `Rask:Database` is not read: the database is the one the callback
names. Add `.OwnsSchema()` and the host applies its migrations as it starts, the way a `RaskApp` does — see
[taking over an existing schema](#taking-over-an-existing-schema).

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

- **`WritableChildrenInterceptor`** — first of all: hands a held collection EF Core can fill, and asks the domain's
  rules of the save ([what every save asks](domain.md#what-every-save-asks)) before any other interceptor touches a row.
- **`ProjectionInterceptor`** — next: raises each [declared projection](domain.md#declared-projections-projects)'s event onto
  the rows the save changed, before soft-delete rewrites a removal and before the outbox and the publish read the events.
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

Two instances starting together are safe: EF Core takes its migration lock on every provider — `sp_getapplock` on SQL
Server, a table lock on PostgreSQL, a lock table on SQLite — so each migration runs once.

When something else applies migrations — a release pipeline running `rask db update` or an idempotent SQL script —
turn it off:

```csharp
app.Configure(c => c.MigrateOnStart = false);
```

or set `Rask:Database:MigrateOnStart` to `false` (`Rask__Database__MigrateOnStart=false` in the environment). The code
line wins over the setting — and it decides whether anything applies at start, so with it off a
`c.Data.OwnsSchema(…)` beside it takes nothing over either. A host wired by hand (`AddRask()` + `MapRask<TApp>()`)
migrates on start only when its `AddRaskData<TContext>(…)` says `.OwnsSchema()`.

The release step itself can be the app: `dotnet App.dll rask db migrate` builds the host without starting it, applies
the pending migrations the way a start would (`OwnsSchema` included), runs their [data steps](data-writes.md#moving-data-in-c-with-bdata),
and exits — with `MigrateOnStart` on or off. `dotnet App.dll rask db pending` lists the migrations the database has not
had, one a line, in the order they apply; one that rewrites what the rows hold (and so no older version of the app may
run beside) ends in ` exclusive` — what [`rask deploy`](deployment.md#one-command-deploy--rask-deploy) asks a new image
before it starts it.

### Taking over an existing schema

An app moving onto Rask has a live database its new migrations did not make. Their first migration — the baseline —
makes the schema that database already has, so there it must only be *written down* as applied; running it would fail
on tables that exist. `OwnsSchema` says so — on the Data battery of a `RaskApp`, which applies its migrations as it
starts anyway:

```csharp
app.Configure(c => c.Data.OwnsSchema(s => s.TakesOverFrom("Init").WhenTable("Users").Patience(10.Minutes)));
```

or on the context a hand-wired host registered — the same word and the same `s => …` steps:

```csharp
// Before: a SchemaOwner class of the app's own — sp_getapplock, a raw OBJECT_ID check, a hand-built history row,
// SetCommandTimeout — called from Program.cs after Build(), and an *AtDesignTime factory beside it.
builder.Services.AddRaskData<ShopContext>(o => o.UseSqlServer(connection));
await SchemaOwner.Take(app.Services.GetRequiredService<IDbContextFactory<ShopContext>>(), app.Logger);

// After
builder.Services.AddRaskData<ShopContext>(o => o.UseSqlServer(connection))
    .OwnsSchema(s => s.TakesOverFrom("WallClockMoments").WhenTable("Tenant").Patience(20.Minutes));
```

As the host starts — before any hosted service and before the web server — it takes EF Core's migration lock, so of a
blue and a green starting together only one writes anything, and:

| The database | What happens |
|---|---|
| does not exist, or has no `Tenant` table | made by the migrations from the first, as for any new database |
| has `Tenant`, and its history shows `WallClockMoments` applied | the baseline is written down as applied; the migrations after it run |
| has `Tenant`, but no `WallClockMoments` in its history | the start stops: its schema is not the one the baseline describes |
| already has the baseline in its history | nothing to take over; pending migrations run |

Each step is optional:

- **`WhenTable("Tenant")`** — a table the baseline creates. Alone, it takes over a database that has the table even
  with no migration history at all (a schema made by `EnsureCreated`, by hand, or by another tool).
- **`TakesOverFrom("WallClockMoments")`** — the last migration of the set the baseline replaces, by its full id or just
  its name (any moment matches). Alone, a history that shows it applied is proof enough; with `WhenTable`, it is the
  guard in the third row above.
- **`Patience(20.Minutes)`** — the command timeout each migration runs with, instead of the provider's thirty seconds:
  a migration that moves the data takes minutes.

On a hand-wired host, `.OwnsSchema()` with nothing in it just applies the pending migrations on start. The checks a
`RaskApp` makes hold here too: no migrations is a warning, a model changed since its last migration stops the start.

On a `RaskApp`, `c.Data.OwnsSchema(…)` says *how* the migrations apply and `MigrateOnStart` says *whether*: with
`c.MigrateOnStart = false` or `Rask:Database:MigrateOnStart=false`, nothing is applied or taken over at start — the
release step running `rask db update` owns the schema then. Beside `c.Data.Off()` (or `c.Cqrs.Off()`, which leaves the
app without a database too) it refuses the start, naming both: there is no schema to own.

### Migrations with plain `dotnet ef`

`rask db add Name` is `dotnet ef migrations add Name` with the project filled in, and nothing else: it hands EF no
provider and no connection string. So the EF Core tools work on their own too:

```bash
dotnet ef migrations add AddProducts
dotnet ef database update
```

How EF gets the context depends on whose it is:

- **`RaskAppDbContext`** — an app with no context of its own. EF runs Program.cs up to `Build()` and takes the
  context from the container. That is on purpose: its tables follow the batteries Program.cs switches on and off
  (`c.Jobs.Off()`), which only Program.cs can say. Nothing in it connects before `Run`, so this works on any machine.
- **A context of your own** — Rask generates an internal `IDesignTimeDbContextFactory<YourContext>` beside it, and EF
  uses that **without running Program.cs**. A host that cannot start on a developer's machine — a vault it reads, a
  database it migrates as it starts — still gets migrations.

The generated factory reads the configuration the app starts with, in the same order: appsettings.json,
appsettings.{Environment}.json, user secrets in Development, environment variables, and arguments after `--`.
`dotnet ef` runs as Development; `dotnet ef migrations add X -- --environment Production` reads Production's
settings instead. It opens the database through `UseRaskDatabase`, as the app's own registration does: the provider
`Rask:Database:Provider` names (SQLite when it names none), at `Rask:ConnectionStrings:App`, tuned by `Rask:Sqlite`,
`Rask:Postgres` or `Rask:SqlServer`. What goes wrong says what to set: an unknown provider, a missing connection
string, or `postgres` without the Rask.Postgres package each stop the command with the same sentence the app would
start with.

A host wired by hand chooses its provider in code — `AddRaskData<ShopContext>(o => o.UseSqlServer(connection))` — and
`dotnet ef` does not run that code. So there the factory requires the setting, and stops naming it rather than
writing SQLite migrations for a SQL Server database:

```csharp
// Before: a factory of the app's own, beside the context
public sealed class ShopContextAtDesignTime : IDesignTimeDbContextFactory<ShopContext>
{
    public ShopContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ShopContext>().UseSqlServer("Server=nobody;Database=nothing").Options);
}
```

```jsonc
// After: the class is deleted, and appsettings.json says what it said
{
  "Rask": {
    "Database": { "Provider": "sqlserver" },
    "ConnectionStrings": { "App": "Server=nobody;Database=nothing;Encrypt=false" }  // never opened by migrations add
  }
}
```

A factory you write yourself still wins: when the project declares an `IDesignTimeDbContextFactory<T>`, none is
generated for `T`. Keep one when design time needs more than configuration says — a SQL Server compatibility level,
an extension like NetTopologySuite, a `MigrationsAssembly` the callback sets.

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

`RaskApp` reads it for you. SQLite and SQL Server come with `Rask.Server`; **PostgreSQL is a package you add**,
`dotnet add package Rask.Postgres` ([below](#postgresql)), so an app on SQLite or SQL Server never restores Npgsql.
`Rask.Server` finds it by name rather than depending on it. With the setting on `postgres` and the package missing, the app fails at start with one sentence: add the Rask.Postgres package. An app with a context of its own
registers it with `UseRaskDatabase(sp)`, which opens whichever provider the setting names:

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
  are the whole switch — plus `dotnet add package Rask.Postgres` for PostgreSQL (`Rask.Server` carries SQL Server). Its `appsettings.json` has a
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

> **A package of its own, at Rask's version.** `Rask.Postgres` ships stable, at the same version as every other Rask
> package, on Npgsql's stable EF Core 11 provider. It stays out of `Rask.Server`, which finds it by name, so only a
> Postgres app restores Npgsql. The provider asks for EF Core `[11.0.0, 12.0.0)`, the line the rest of Rask is on, so
> a Postgres app restores without a warning to silence.
>
> | Package an app references itself | Pin | Forced by Rask's dependency ranges? |
> | --- | --- | --- |
> | `Microsoft.EntityFrameworkCore`, `.Relational`, `.Sqlite`, `.SqlServer`, `.Design`, `.Tools` | `11.0.0` | Yes, as a floor: Rask asks for at least `11.0.0`. Lower fails with NU1605. |
> | `Microsoft.Extensions.*`, `Microsoft.AspNetCore.*`, `Microsoft.Data.Sqlite` | `11.0.0` | A floor too; most `Microsoft.Extensions.*` abstractions now ship in .NET 11 itself and need no reference. |
> | `Npgsql.EntityFrameworkCore.PostgreSQL` (Postgres apps, through `Rask.Postgres`) | `11.0.0` | A floor: `Rask.Postgres` asks for at least `11.0.0`, and that provider takes any EF Core `11.x`. |

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

**The compatibility level is the server's.** EF Core writes its SQL for a compatibility level, and its own default
is a guess at the newest server — wrong for a database still at 150 on SQL Server 2019 or Azure SQL Edge, and behind
one at 170 on SQL Server 2025. `UseRaskSqlServer` asks the server instead, the first time it sees a connection string:
the database's own level (`sys.databases` for `DB_NAME()`), or, before the database exists, the level the server's
version gives a new one (2025 → 170, 2022 → 160, 2019 and Azure SQL Edge → 150). The answer is kept for the process,
so a context is never a round trip; two contexts starting together send one probe, on an unpooled connection with a
5-second connect timeout. A server it cannot reach — an offline `dotnet ef`, a host starting before its database —
leaves EF Core's default, is logged once as a warning, and is asked again 30 seconds later; a context built
meanwhile keeps the default, so `AddDbContextFactory`'s options, built once, keep it until the app restarts. Pin it
with `Rask:SqlServer:CompatibilityLevel` (`"CompatibilityLevel": 170`, or `s => s.CompatibilityLevel = 170`), which
asks nothing — the one choice for Azure SQL Database before its database exists, since it reports no version to map.
An app needs no `UseCompatibilityLevel` of its own. The level changes no column: a JSON column (a value collection, a
`ToJson()` type) stays `nvarchar(max)` at 170 too, where EF Core alone would switch it to the native `json` type.

Both providers' limits are made for an app's queries, not its schema: the migrations `MigrateOnStart` applies run
on one connection with them lifted (`statement_timeout` and `lock_timeout` 0 on PostgreSQL, `LOCK_TIMEOUT -1` on
SQL Server), so a migration that rewrites a table, or waits behind the old instance's queries, is not killed
partway. The pool puts the session back as it came once the migrations are done.

Everything in this guide works unchanged except full-text search, `HasJsonIndex` and non-overlapping ranges,
which a context on SQL Server cannot declare — it is refused at boot, naming the declaration. Three things to
know, as on PostgreSQL:

- **Retrying refuses a transaction you open yourself** outside the execution strategy — wrap it in
  `context.Database.CreateExecutionStrategy().ExecuteAsync(...)`, or set `Rask:SqlServer:Retry:Enabled` to `false`.
- **Litestream and snapshots do not apply.** Back up with `BACKUP DATABASE` or your provider's snapshots.
- **Open connections through EF** (`context.Database.OpenConnectionAsync()`) in hand-written ADO code, or the
  session settings are not sent.

### Guid keys sort by time on every database

`ORDER BY Id`, keyset paging (`Where(x => x.Id > last)` — a typed id over a number or a `Guid` has `<`, `>`, `<=`,
`>=` and `CompareTo`), `Min`/`Max`, a join and `Contains(ids)` answer the same on SQL Server, PostgreSQL and SQLite,
in the order .NET's `Guid.CompareTo` gives — so a version-7 id (`XId.New()`) sorts by the time it was made, and a
clustered key stays append-only. PostgreSQL's `uuid` and SQLite's text already compare that way. SQL Server compares
a `uniqueidentifier`'s bytes in another order (10–15, 8, 9, 6, 7, 4, 5, 0–3), so on SQL Server Rask stores every
`uniqueidentifier` column — every `Guid`, `Guid?` and typed id over a `Guid`, keys and foreign keys alike, Rask's own
tables included — with its bytes rearranged so SQL Server's order is the id's. The id the app reads, writes, prints
and serializes is unchanged; a column of text (`nvarchar`, or a Guid stored as text) and JSON are left as they are.
No default is added: Rask assigns every key in the app.

It applies where Rask's migrations do (`UseRaskSqlServer`, a `RaskApp`): the model carries a `Rask:GuidOrder`
annotation, and **the first migration you add after upgrading rewrites the rows an existing database holds**, once —
every `uniqueidentifier` column of every table the context's migrations own, with the foreign keys between them
switched off while it runs and checked again after. A database the first migration creates needs none. Until that
migration exists, `MigrateOnStart` refuses to start the app (a pending model change), so the new code never reads
rows in the old layout; [`rask deploy`](deployment.md#one-command-deploy--rask-deploy) stops the old version before
the new one applies it.

**Raw SQL sees the stored form** — `FromSql`, Dapper, SSMS. EF's own parameters, `BulkInsert` and Rask's own
statements convert for you. In T-SQL, the stored form of an id's text is

```sql
DECLARE @r binary(16) = CONVERT(binary(16), REPLACE(@id, '-', ''), 2);
SELECT CAST(SUBSTRING(@r, 13, 4) + SUBSTRING(@r, 11, 2) + SUBSTRING(@r, 9, 2) + SUBSTRING(@r, 7, 2)
          + SUBSTRING(@r, 1, 6) AS uniqueidentifier);
```

and a column `[c]` still in SQL Server's native layout is rewritten to it (what the generated migration does) with

```sql
-- @n = CAST([c] AS binary(16))
CAST(SUBSTRING(@n, 13, 4) + SUBSTRING(@n, 11, 2) + SUBSTRING(@n, 9, 2) + SUBSTRING(@n, 8, 1) + SUBSTRING(@n, 7, 1)
   + SUBSTRING(@n, 4, 1) + SUBSTRING(@n, 3, 1) + SUBSTRING(@n, 2, 1) + SUBSTRING(@n, 1, 1)
   + SUBSTRING(@n, 6, 1) + SUBSTRING(@n, 5, 1) AS uniqueidentifier)
```

A migration of your own that re-keys rows writes ids through EF (`UpdateData`, a data step) and gets the stored form
without either.

One model detail is handled for you: a SQL Server index key holds 450 `nvarchar` characters, and `Rask.Cache`
configures its key at 512 for the other providers. `UseRaskSqlServer` caps that key — and only that key — at 450.
A longer cache key cannot be stored there; `ICache` rejects it with an error naming the limit, so hash long keys.

## Notes

- **A real provider.** These interceptors run against a real EF Core provider (SQLite by default in Rask) —
  in a WebAssembly app too, over the browser's SQLite ([above](#wiring-when-rask-is-not-hosting)).
- **Trim/AOT-safe.** The model convention runs at startup (not the hot path) and uses no runtime handler
  reflection; domain-event dispatch goes through `Rask.Cqrs`' source-generated registry.
- **Durable delivery.** For at-least-once, crash-safe events, pair the entity with
  [`Rask.Outbox`](outbox.md), which persists events in the same transaction and drains them from a
  background worker (and disables the in-process dispatcher to avoid double delivery).
