# Upgrading an app to the domain way

> **The target:** [domain modelling](domain.md) · [a form over an operation](forms.md#a-form-over-an-operation) ·
> [testing the domain](testing.md#testing-the-domain) · [writing many rows](data-writes.md).

An app written before Rask.Data had aggregates reads and writes the database through an injected
`IDbContextFactory<T>`: a service per feature loads an entity, checks a few `if`s, sets its properties and calls
`SaveChangesAsync`; a page holds a hand-written form class and copies it across; the tests run against a SQL Server
fixture, one at a time. This page moves such an app onto the [one way](domain.md) — every write an aggregate's own
method, every property a value object or a typed id, the form the operation's, the tests on SQLite in memory — a
feature at a time, with the app shipping between every step.

It is written from moving real production apps, each with dozens of aggregates, a live SQL Server database and
years of stored rows. The order below is the one that kept every step small and every release safe.

## The order

| Step | What changes | Breaks the schema? |
|---|---|---|
| 1. [Provider-neutral code](#1-provider-neutral-code) | SQL and provider APIs out of the app | no |
| 2. [Tests on SQLite in memory](#2-tests-on-sqlite-in-memory) | the test fixture | no |
| 3. [Value objects and typed ids](#3-value-objects-and-typed-ids) | property types, rules out of forms and services | no — same columns |
| 4. [`Versioned`](#4-versioned) | a `Version` column on every edited aggregate | one migration |
| 5. [Operations instead of services](#5-operations-instead-of-services) | services dissolve into aggregate methods | no |
| 6. [Forms over the operations](#6-forms-over-the-operations) | hand form classes go | no |
| 7. [Events instead of cross-aggregate writes](#7-events-instead-of-cross-aggregate-writes) | the remaining two-aggregate saves | no |
| 8. [Stored data: indexes, data steps](#8-stored-data-indexes-data-steps) | unique indexes, link tables, moved columns | migrations, duplicates checked first |
| 9. [Strict](#9-strict) | warnings become errors | no |

One aggregate per pull request from step 3 on: the aggregate, its pages and its tests move together, and nothing
else does. The build's analyzers find every site of each step; their quick-fixes (the lightbulb, or `dotnet format
analyzers` for a whole solution) do the mechanical part, and what is left is the part that needs a decision.

## 1. Provider-neutral code

The app's tests are going to run on SQLite, so the app must stop saying things only SQL Server understands. Every
such site is [RASK177](diagnostics.md#rask177) — a suggestion by default, so turning it up is how you find them:

```xml
<!-- Directory.Build.props, for the duration of the move -->
<PropertyGroup><RaskStrict>warnings</RaskStrict></PropertyGroup>
```

Each finding has a neutral form ([code written for one database](data.md#code-written-for-one-database)); the ones that
came up in every app:

| Before | After |
|---|---|
| `b.Property(x => x.Price).HasColumnType("decimal(18, 4)")` | `UnitPrice(decimal Value) : Number(Value, Scale: 4)` on the value object |
| `EF.Functions.Collate(x.Name, "Hungarian_100_CI_AS")`, `UseCollation(…)` | `c.OrderTextBy("Hungarian_100_CI_AS")` once in the `IRaskModel`, `x.Name.InTextOrder()` in the query |
| `EF.Functions.Like(x.Name, $"%{typed}%")` | `x.Name.Loosely().Contains(typed)` (case and accents aside) or `x.Name.Contains(typed)` |
| `EF.Functions.DateDiffDay(a, b)` | `b - a` |
| `ExecuteSqlRawAsync("EXEC sp_getapplock …")`, a hand-written `UPDLOCK` | `await using var turn = await Db.Lock("pump").For(30.Seconds);`, `X.Where(…).ForUpdate()` |
| `catch (SqlException e) when (e.Number is 2601 or 2627)` | `HasIndex(…).IsUnique("That number is taken.")` — the refusal lands under the field |
| `HasDefaultValueSql("GETUTCDATE()")`, `HasComputedColumnSql(…)`, `HasFilter("[X] IS NULL")` | `b.Default(() => Clock.Wall)`, `b.Computed(…)`, `HasIndex(…).Where(x => x.X == null)` |
| `IncludeProperties(…)` | `.Covering(…)` |
| a T-SQL seed script | the seed through the aggregates' own factories |
| `UseSqlServer(…)` scattered in `Program.cs` and a design-time factory | `UseRaskDatabase(sp)`, the provider read from `Rask:Database:Provider` |

A migration is exempt — it is written for the database it migrates — and so is the one call that selects the
provider. A database the app's migrations did not create is taken over with
[`OwnsSchema`](data.md#taking-over-an-existing-schema), which replaces a hand-written "schema owner" class.

## 2. Tests on SQLite in memory

The old fixture — a SQL Server container, a snapshot restored per class, a hand-built context with two of the
interceptors, parallelism switched off — is replaced by one line per test:

```csharp
// Before
[Collection(nameof(OneAtATime))]
public sealed class SupplierTests(SqlServerFixture db)
{
    [Fact]
    public async Task A_blocked_supplier_keeps_its_name()
    {
        await db.Reset();
        var service = new SupplierService(db.Factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Rename(db.BlockedSupplierId, "New", default));
    }
}

// After — the Supplier of step 5
public sealed class SupplierTests
{
    [Fact]
    public async Task A_blocked_supplier_keeps_its_name()
    {
        await using var database = await TestDatabase.Start<AppDbContext>();
        var id = await Supplier.Register(Test.Make<SupplierName>());
        var supplier = (await Supplier.Find(id))!;
        supplier.Block();
        await supplier.Save();

        var refusal = await Test.Refuses(() => Supplier.Rename(id, supplier.Version, Test.Make<SupplierName>()));

        refusal.Only("Name");
    }
}
```

`TestDatabase.Start()` with no type argument is the same for an app on Rask's own context.

- **A database per test**, made from the app's model in memory and copied through SQLite's backup API, so the tests
  run in parallel and a start costs well under a millisecond after the first. Delete the `OneAtATime` collection.
- **The whole interceptor chain runs** — timestamps, versions, domain events, the outbox — so a test sees what
  production does. Hand-built test contexts that skipped some of it go.
- **Why it is safe to trust:** Rask runs one conformance suite on SQLite, SQL Server and PostgreSQL for everything it
  translates (text order, decimals, unique and filtered indexes, locks, JSON columns), and step 1 removed what it
  does not translate. A "reads only" comparison test against the real server is no longer needed.
- **Test data from the rules:** `Test.Make<T>()` replaces the hand-kept builders — a value object its rules accept, or
  a stored aggregate made through its own factory with what it refers to made first
  ([`Test.Make`](testing.md#a-valid-value-object-testmake)).
- A `TestDatabase` runs at `OneAggregatePerTransaction = Throw`, which is how step 7's sites announce themselves.

Only an end-to-end gate before a release still needs the real server
([a real database server](testing.md#a-real-database-server-when-the-machine-has-one)).

## 3. Value objects and typed ids

A rule that lives in a form (`.Validate(v => …)`), a service (`if (name.Length > 200) throw …`) or an attribute on
a model class moves into the type the property is. The columns do not change: a one-value value object is stored as
its value, and a typed id as the key it wraps.

```csharp
// Before
public sealed class Supplier
{
    public int Id { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [Range(0, 100)] public decimal Discount { get; set; }
}

// After
public readonly partial record struct SupplierId(int Value);
public sealed partial record SupplierName(string Value) : Text(Value, MaxLength: 200);
public sealed partial record Discount(decimal Value) : Number(Value, Min: 0, Max: 100, Scale: 2);

public sealed partial class Supplier : Aggregate<SupplierId, Timestamps>
{
    public SupplierName Name { get; private set; } = null!;
    public Discount Discount { get; private set; } = null!;
}
```

What the quick-fixes do, and what they leave:

| Lightbulb | Does |
|---|---|
| [RASK130](diagnostics.md#rask130) | declares the value object from a property's rules (`[StringLength(200)]`, a FluentValidation `MaximumLength(200)`), types the property as it and removes the rules its base now says |
| [RASK127](diagnostics.md#rask127) | `new KmRange(a, b)` → `KmRange.Create(a, b)`, a `with` → a new `Create` |
| [RASK084](diagnostics.md#rask084) / [RASK085](diagnostics.md#rask085) | public setters → `private set`; a mutable child collection → a private list behind `IReadOnlyCollection<T>` |
| [RASK150](diagnostics.md#rask150) | a made-up starting value (`= SupplierName.Create("x")`) → `= null!`, so a new form starts empty |
| [RASK140](diagnostics.md#rask140) | `DateTime.Now` → `Clock.Wall`, `DateTime.UtcNow` → `Clock.Now.UtcDateTime` |

**By hand:** choosing which ids get a type (all of them — an `int` passed where another table's `int` was meant is
the bug this removes), a rule that reads another aggregate (an async `Validate()` on the aggregate), and a value
object of several values (`Range`, or `Value` with its own `Rules()`). An existing column whose length differs from
the new `MaxLength` is a decision per value object: keep the old length on the value object, or migrate the column.

**Old rows still load.** A value object is not validated as it is read, and a save asks a property's rules only once
that property changes, so a stored value a tightened rule would refuse opens and is put right when next edited.
Before a unique index over such rows goes in, [step 8](#8-stored-data-indexes-data-steps) checks them.

## 4. `Versioned`

Every aggregate a person edits names `Versioned`, so two people saving one record cannot silently overwrite each
other. It is one migration — `Version int NOT NULL DEFAULT 0` on each table — and from then on every operation's
entry takes the version the form was read at, and a stale one is refused at the top of the form. An aggregate only
the system writes (an ingest, a log) can leave it off; its entries take no version.

## 5. Operations instead of services

The service method becomes the aggregate's method; the load, the transaction and the save become its generated entry.

```csharp
// Before: a service, a context, the rule as an exception
public sealed class SupplierService(IDbContextFactory<AppDbContext> contexts)
{
    public async Task Rename(int id, string name, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var supplier = await db.Suppliers.FindAsync([id], ct) ?? throw new KeyNotFoundException();
        if (supplier.Blocked) throw new InvalidOperationException("A blocked supplier keeps its name.");
        supplier.Name = name.Trim();
        await db.SaveChangesAsync(ct);
    }
}

// After: the method is the write
public sealed partial class Supplier : Aggregate<SupplierId, Timestamps, Versioned>
{
    public SupplierName Name { get; private set; } = null!;
    public bool Blocked { get; private set; }

    private static Supplier Register(SupplierName name) => new() { Name = name };   // a factory: Supplier.Register(…)

    public void Block() => Blocked = true;

    public void Rename(SupplierName name)
    {
        Refuse.When(Blocked, () => Name, "A blocked supplier keeps its name.");
        Name = name;
    }
}

await Supplier.Rename(id, version, SupplierName.Create(typed));   // generated: Find(id, version) → Rename → Save()
```

The service class, its interface and its DI registration are deleted. A page reads through the read face
(`Supplier.Where(…)`), never a context.

| Lightbulb | Does |
|---|---|
| [RASK184](diagnostics.md#rask184) | `if (c) throw new InvalidOperationException(m)` → `Refuse.When(c, m)`; an `ArgumentException(m, nameof(x))` → `Refuse.When(c, () => x, m)` |
| [RASK186](diagnostics.md#rask186) | `Supplier.Update(id, s => s.Rename(n), version: v)` → `Supplier.Rename(id, v, n)` |
| [RASK133](diagnostics.md#rask133) | `db.Add(Supplier.Register(n)); await db.SaveChangesAsync()` → `await Supplier.Register(n)` |
| [RASK187](diagnostics.md#rask187) | `db.Pairings.Where(…).ExecuteDeleteAsync()` → `Pairing.DeleteWhere(…)`, `ExecuteUpdateAsync` → `UpdateWhere` |
| [RASK185](diagnostics.md#rask185) | a load outside a `Db.Transaction` moved in, as its first statement |

**By hand:** deciding which aggregate a service's rule belongs to — the one it is about; a rule that reads another
aggregate reads its read face, inside the operation. A service that did nothing but load and save disappears; one
that coordinated several aggregates is [step 7](#7-events-instead-of-cross-aggregate-writes). A pure calculation (a
matcher, an allocation) stays a plain static class with its own unit tests — it decides nothing and writes nothing.

Writes over many rows have their own shapes, each going through the aggregate: `X.UpdateEach(…)` for a change a rule
is about, `X.Upsert(…)` for "make it or change it", `X.Import(rows, …)` for a loader, and `UpdateWhere`/`DeleteWhere`
for a flag no rule is about ([writing many rows](data-writes.md#writes-over-many-rows)). A job or an event handler calls
the entry without a version — the aggregate as it now stands.

## 6. Forms over the operations

The hand-written form class, the copy into it, the copy out of it, the `.Validate(…)` on each field and the
`Outcome` mapped to a toast all go: the form is the operation's.

```csharp
// Before
private readonly SupplierForm _form = new();          // a class of its own, a copy of the rules

Form.Model(_form).OnSubmit(async f =>
{
    try { await suppliers.Rename(Id, f.Name!, CancellationToken); Toast.Show("Saved"); }
    catch (InvalidOperationException e) { _error = e.Message; }
})[
    Ui.Input.Bind(() => _form.Name).Label("Name").Validate(v => v?.Length > 200 ? "Too long." : null),
    Ui.Button.Primary.Submit["Save"],
]

// After
Form.For(Supplier.Rename, Id)[f => [
    Ui.Field.Bind(() => f.Name).Label("Name"),          // maxlength, required and the Refuse, from the domain
    Ui.Button.Primary.Submit["Save"],
]]
```

- The fields are the operation's parameters, filled from the aggregate, checked by its rules as they are typed (the
  operation's dry run), and saved through the entry at the version the form was read at.
- A list parameter is an editable list of rows, a value object of several values a group of fields, a `Change<T>` a
  field that keeps the stored value when left empty, an `Attachment` a file picker
  ([a form over an operation](forms.md#a-form-over-an-operation)).
- A list's create and edit pages move into a [`Ui.EditorDialog`](ui-kit.md#the-editor-over-its-list) over the list,
  `.Delete(Supplier.Delete)` adding the delete with its `ValidateDelete()` asked first.
- `Test.FormShows(…)` is the one line per feature that proves the form says what the domain says
  ([`Test.FormShows`](testing.md#the-form-says-what-the-domain-says-testformshows)).

A hand form stays only where there is no domain behind it — a search box, a filter.

## 7. Events instead of cross-aggregate writes

What is left after step 5 is the code that changes two aggregates at once. Each site is found twice: by
[RASK178](diagnostics.md#rask178) at build, and by a `TestDatabase`, which throws `ManyAggregatesException` at the
first transaction that writes a second aggregate without saying why. Decide each in this order:

1. **Draw the boundary again.** Two things that always change together are one aggregate — a part that never stands
   alone is a [child](domain.md#children).
2. **Follow from an event.** The first aggregate raises what happened; the second reacts on itself, one line:

   ```csharp
   // Before: one service saving both
   order.Status = OrderStatus.Placed;
   stock.Reserved += order.Quantity;
   await db.SaveChangesAsync(ct);

   // After
   public void Place() { Status = OrderStatus.Placed; Raise(new OrderPlaced(Id, Stock, Quantity)); }   // on Order

   static Task On(OrderPlaced e) => StockItem.Reserve(e.Stock, e.Quantity);                           // on StockItem
   ```

   `static Task On(E e)` is a durable handler through the outbox, in a transaction of its own and retried until it
   succeeds ([domain events](domain.md#domain-events)). A save hook that wrote elsewhere after a save becomes a declared
   [`Projects(…)`](domain.md#declared-projections-projects) or [`Numbered(…)`](data-writes.md#a-running-number).
3. **The last resort: both in one transaction**, the reason written where the decision is made —
   `Db.Transaction(async () => …, manyAggregates: "why")`, or once on the operation,
   `b.Operation(x => x.M, manyAggregates: "why")`. RASK178's quick-fix writes `manyAggregates: "TODO: why"`; a site
   whose honest reason will not fit in a sentence is usually a boundary drawn wrong.

In the apps this page is written from, only a handful of sites per app stayed `manyAggregates:` — a filling and the
withdrawals that are only valid together, a distribution that must never sell the last unit twice. Once every site
is decided, production can run at `OneAggregatePerTransaction = Throw` too.

## 8. Stored data: indexes, data steps

Years of rows were written before the rules existed. Before an index the stored data might break goes live, ask the
data — on the production server, read only, reporting ids and counts and never values. The order for every new
unique index:

1. Declare it in the model — `b.HasIndex(d => new { d.TenantId, d.CardNumber }).IsUnique("That card is taken.")` —
   but do not release the migration yet.
2. Ship a read-only duplicate-check SQL with it, for the owner to run against production before the deploy:

   ```sql
   SELECT COUNT(*) AS Rows, MIN(Id) AS FirstId, MAX(Id) AS LastId
   FROM Drivers
   GROUP BY TenantId, CardNumber
   HAVING COUNT(*) > 1;
   ```

   Each group it returns is a decision: merge, fix or retire the rows — through the aggregates' own operations, from
   a job or a data step, never a hand-run `UPDATE`.
3. Run it again until it returns nothing, then release the migration.

**Data a migration moves is C#.** A column replaced by a link table, a value split in two, a status a state now
implies: the migration's `Up` adds a data step that runs through the aggregates after the schema is migrated, once,
recorded in `__RaskDataSteps`:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.CreateTable(/* …what `rask db add TankPistols` wrote… */);
    migrationBuilder.Data(async () =>
        await Tank.UpdateEach(_ => true, async t => t.Draw(await Pistol.Where(p => p.TankId == t.Id).Select(p => p.Id))));
}
```

Keep the old column for one release after the step moved it, so a rollback still has it; drop it in the release
after ([moving data in C#](data-writes.md#moving-data-in-c-with-bdata)). Run every such migration on a copy of
production first.

## 9. Strict

With every aggregate moved, `<RaskStrict>errors</RaskStrict>` makes the analyzers errors, so the next feature is
written the domain way or does not build: no public setters, no writes on a context, no provider API, no transaction
over two aggregates without a reason ([strict mode](code-analysis.md#strict-mode-for-your-app-raskstrict)).

## What it bought

The write side of a typical feature — value objects, aggregate, service, form class, save hooks — shrank to roughly a
third: the rules are said once, in the types and the methods, and the form, the save, the transaction and the error
mapping are Rask's. The tests went from a serial SQL Server fixture to parallel SQLite in memory, and the
continuous-integration time with them.
