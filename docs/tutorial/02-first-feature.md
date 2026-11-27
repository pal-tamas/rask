# Chapter 2 — Your first feature

> **Goal:** go from an empty app to a working, database-backed **Products** catalog — list, add, edit, delete —
> persisted in SQLite, and proved by tests that run on SQLite in memory.
> **You'll write:** a vertical slice under `Features/Products/` — an aggregate and one page — its tests under
> `Shop.Tests/Features/Products/`, then run `rask db add`.

This chapter sets the pattern every later feature repeats — **aggregate → page → tests → migrate**. Do it once here
and the rest of the tutorial is variations on it.

There is one way to write a feature in Rask, and this chapter is it:

- **The business is the aggregate.** What a product *is* and what may happen to it is one class. There is no
  service layer, no repository and no hand-written form model.
- **Its properties are value objects or typed ids.** A name is a `ProductName`, a price a `UnitPrice`; each says its
  own rule once, and every form, every save and every message reads it from there.
- **A write is one of its methods.** `Change`, `Discontinue`. Rask generates the entry that loads it, runs the method
  and saves it in one transaction.
- **The form is the operation's.** `Form.For(Product.Change, id)` is a form whose fields are the method's parameters,
  checked by the method's own rules as they are typed.

> **You don't name a database.** Chapter 1's `rask new` already wired one. Rask's own context,
> `RaskAppDbContext`, maps every aggregate you declare, so there is no context to edit when you add one, and
> an app keeps **one** database and one set of migrations however many features you add.

## 1. The aggregate

`Features/Products/Product.cs`:

```csharp
namespace Shop.Features.Products;

public readonly partial record struct ProductId(Guid Value);

public sealed partial record ProductName(string Value) : Text(Value, MaxLength: 200);

public sealed partial record UnitPrice(decimal Value) : Number(Value, Min: 0, Max: 1_000_000, Scale: 2);

public sealed partial record StockLevel(long Value) : Integer(Value, Min: 0);

public sealed partial class Product : Aggregate<ProductId>, IHasTimestamps, IVersioned, IDeletable
{
    public ProductName Name { get; private set; } = null!;

    public UnitPrice Price { get; private set; } = null!;

    public StockLevel Stock { get; private set; } = null!;

    public bool Discontinued { get; private set; }

    public Attachment? Photo { get; private set; }

    private static Product Add(ProductName name, UnitPrice price, StockLevel stock) =>
        new() { Id = ProductId.Create(), Name = name, Price = price, Stock = stock };

    public void Change(ProductName name, UnitPrice price, StockLevel stock)
    {
        Refuse.When(Discontinued && price != Price, () => price, "A discontinued product keeps its last price.");
        (Name, Price, Stock) = (name, price, stock);
    }

    public void Discontinue() => Discontinued = true;

    public void Picture(Attachment photo) => Photo = photo;
}
```

That is the whole feature's business code. Read it from the top.

**A typed id, in one line.** `ProductId` wraps the `Guid` key, so an order line can hold a `ProductId` and the
compiler will never let you hand it an order's id by mistake. Rask completes it — `ProductId.Create()`,
`ProductId.Parse(…)`, its JSON and its route value — and converts it to its `Guid` column
([strongly-typed ids](../domain.md#strongly-typed-ids)).

**Value objects, in one line each.** `ProductName` is a text of at most 200 characters, `UnitPrice` a number from 0
to a million with two decimal places, `StockLevel` a whole number of 0 or more. The base record (`Text`, `Number`,
`Integer`) *is* the rule: a `ProductName` that breaks it can't be made — `ProductName.Create("")` is refused with
*This field is required.* — so a `Product` can never hold one. The same declaration gives the column its length and
precision and the form's input its `maxlength`, `min` and `step`. Rask generates `Create`, `TryCreate` and
`CreateOptional` beside each ([value objects](../domain.md#value-objects)). Only a `bool` (`Discontinued`) and an
`enum` stay primitive.

**The aggregate.** `Aggregate<ProductId, …>` comes from [Rask.Data](../domain.md). An **aggregate** is the unit your
app loads, changes and saves as one. Deriving from it is all the mapping there is — no `DbSet`, no configuration
class, no registration. The key comes first, and every name after it is a **trait** the table opts into:

- **`IHasTimestamps`** adds `CreatedAt` / `UpdatedAt`, stamped on every save.
- **`IVersioned`** adds `Version`, a concurrency token bumped on every save. It is what stops two people editing the
  same product from silently overwriting each other — section 5 proves it.
- **`IDeletable`** generates `Product.Delete(id, version)`. An aggregate whose deleted rows must stay recoverable names
  `ISoftDeletes` instead ([choosing what a table carries](../domain.md#choosing-what-a-table-carries)).

**A factory and three operations.** The class is `partial`, and that is what lets Rask write beside it:

- `Add` is a **factory** — a static method that returns a `Product`. It is `private` because Rask generates the
  public entry of the same name, `await Product.Add(name, price, stock)`, which builds the product, asks its rules,
  saves it in one transaction and hands back its `ProductId`.
- `Change`, `Discontinue` and `Picture` are **operations** — public methods that change it. Each gets a static entry,
  `await Product.Change(id, version, name, price, stock)`, that loads the product at the version you read, runs the
  method and saves it in one transaction ([operations](../domain.md#operations-what-the-aggregate-does)).

**A refusal names its field.** `Refuse.When(…, () => price, "…")` is how a method says no: the whole operation is
rolled back, and the message lands under the *Price* field of whatever form called it
([refusing an operation](../domain.md#refusing-an-operation-refuse)). You never throw an exception of your own for a
business rule.

**A file the product holds.** `Attachment? Photo` is a file stored with the row — the bytes in the app's file
storage, written in the save's transaction, and let go when it is replaced. `Picture` is an ordinary operation that
takes one ([file storage](../file-storage.md#a-file-the-aggregate-holds-attachment)).

Two rules shape the class, and the build enforces both:

- **Setters are `private set`.** A public setter on an aggregate is a build error
  ([RASK084](../diagnostics.md#rask084)): state changes only through its own methods.
- **No constructor.** The implicit one is what EF Core builds rows with. Creation is the factory's.

**Reading needs nothing more.** The build generates a **read face** beside the aggregate —
`Product.Where(…)`, `Product.OrderBy(…)`, `Product.Count()` — whose rows are `ProductRead`: the columns as plain
values (`Name` is a `string`, `Price` a `decimal`), no behaviour, nothing to save. Each read opens its own database
context and disposes it before it returns, so it is safe to call straight from a page, which lives as long as the
browser keeps its socket open. See [the read face](../data.md#reading-the-read-face).

## 2. The page

`Features/Products/ProductsPage.cs` — the list, with the editor over it:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Shop.Features.Products;

[Route("/products")]
public sealed partial class ProductsPage(RouteState route) : Component
{
    private const int PageSize = 20;

    // The rows on screen stay while the next page loads, so paging and sorting never flash an empty table.
    private static readonly QueryOptions KeepRows = new() { KeepPreviousData = true };

    // Which page is on screen and what it is sorted by. The table draws these; it sorts and pages nothing itself.
    private Listing _listing = new(Page: 1, SortBy: "name", Descending: false);

    // Which product the editor holds: ?edit=new for a new one, ?edit=<id> for a stored one, nothing when closed.
    [QueryParam("edit")] public string? Edit { get; set; }

    protected override string? PageTitle => "Products";

    protected override Component? Render()
    {
        // Cached for this session, and refetched by itself after any Product write, because its key is about Product.
        var count = QueryClient.Query(QueryKey.For<Product>("count"), () => Product.Count());
        var rows = QueryClient.Query(QueryKey.For<Product>("page"), _listing, Read, KeepRows);

        return
        [
            Div.Class("flex flex-wrap items-center gap-3")[
                Ui.Heading.Level(1).Xl[$"Products ({count.Data})"],
                Ui.Spacer,
                Ui.Button.Primary.Href(route.With("edit", UiEditorDialog.New))["New product"]
            ],
            Ui.Table.Class("w-full")
                .Paginate(Ui.Pagination
                    .Paginator(new UiPaginator { Page = _listing.Page, PerPage = PageSize, Total = count.Data })
                    .OnPage(page => { _listing = _listing with { Page = page }; }))[
                Ui.TableColumns[
                    Sortable("name", "Name"),
                    Sortable("price", "Price", "w-32"),
                    Ui.TableColumn.Class("w-28")["Stock"],
                    Ui.TableColumn.Class("w-52")["Updated"],
                    Ui.TableColumn.Class("w-16")[""]
                ],
                Ui.TableRows[
                    (rows.Data ?? []).Select(p => Ui.TableRow.Key(p.Id)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Class("truncate")[p.Name],
                        Ui.TableCell[$"{p.Price:C}"],
                        Ui.TableCell[$"{p.Stock}"],
                        Ui.TableCell[$"{p.UpdatedAt:g}"],
                        Ui.RowActions.Edit(route.With("edit", p.Id.ToString()))
                    ])
                ]
            ],
            Editor()
        ];
    }

    private UiEditorDialog Editor() =>
        Ui.EditorDialog.In("edit").Heading(Edit == UiEditorDialog.New ? "New product" : "Edit product")[
            Edit == UiEditorDialog.New ? NewProduct()
            : ProductId.TryParse(Edit, out var id) ? EditProduct(id)
            : null
        ].Delete(Product.Delete);

    private static OperationForm<ProductAddInput> NewProduct() =>
        Form.For(Product.Add)[f => [
            Ui.Field.Bind(() => f.Name).Label("Name"),
            Ui.Field.Bind(() => f.Price).Label("Price").Description("What a customer pays, before tax."),
            Ui.Field.Bind(() => f.Stock).Label("In stock"),
            Ui.Button.Primary.Submit["Add"]
        ]];

    // One form per operation: what the product is, its photo, and taking it off sale.
    private static Component EditProduct(ProductId id) =>
    [
        Form.For(Product.Change, id)[f => [
            Ui.Field.Bind(() => f.Name).Label("Name"),
            Ui.Field.Bind(() => f.Price).Label("Price").Description("What a customer pays, before tax."),
            Ui.Field.Bind(() => f.Stock).Label("In stock"),
            Ui.Button.Primary.Submit["Save"]
        ]],
        Form.For(Product.Picture, id)[f => [
            Ui.Field.Bind(() => f.Photo).Label("Photo"),
            Ui.Button.Submit["Upload"]
        ]],
        Form.For(Product.Discontinue, id)[f => [
            Ui.Button.Danger.Submit["Discontinue"]
        ]],
    ];

    // A heading the reader can sort by. A second click on the sorted one turns it round.
    private Component Sortable(string column, string label, string? width = null) =>
        Ui.TableColumn
            .Class(width)
            .Sortable()
            .Sorted(_listing.SortBy == column)
            .Direction(_listing.Descending ? Ui.TableColumnDirection.Desc : Ui.TableColumnDirection.Asc)
            .OnSort(() => { _listing = new(1, column, _listing.SortBy == column && !_listing.Descending); })[label];

    private static Task<List<ProductRead>> Read(Listing at, CancellationToken ct)
    {
        var sorted = (at.SortBy, at.Descending) switch
        {
            ("price", false) => Product.OrderBy(p => p.Price),
            ("price", true) => Product.OrderByDescending(p => p.Price),
            (_, false) => Product.OrderBy(p => p.Name),
            (_, true) => Product.OrderByDescending(p => p.Name),
        };

        return sorted.Skip((at.Page - 1) * PageSize).Take(PageSize).AsQueryable().ToListAsync(ct);
    }

    private sealed record Listing(int Page, string SortBy, bool Descending);
}
```

One page does the whole of create, read, update and delete. Here is what each part is doing.

**The forms are the operations'.** `Form.For(Product.Add)` is a form over the factory: its fields are `Add`'s
parameters, named as the parameters, capitalised — `name` is `f.Name`. `Form.For(Product.Change, id)` is a form over
the operation, filled from the stored product and carrying the version it was read at. Nothing in the page states
a rule, and every rule is there:

- A `ProductName` field is given `maxlength="200"` and is required; a `UnitPrice` field `min="0"` and two decimals.
  `Ui.Field.Bind` picks the control (a text box, a number, a file picker for the `Attachment`) and puts the label,
  the description and the field's own message around it.
- **As each field is left, the operation is tried** — a dry run, nothing written — so the `Refuse` in `Change` shows
  *A discontinued product keeps its last price.* under *Price* before the reader presses Save.
- **The submit calls the entry**: `Product.Change(id, version, name, price, stock)`. A version somebody else saved
  meanwhile is refused at the top of the form: *Someone else changed this in the meantime.*

See [a form over an operation](../forms.md#a-form-over-an-operation).

**The editor lives over the list, at the list's address.** `Ui.EditorDialog.In("edit")` is open while the address
holds `?edit=` — `UiEditorDialog.New` for a new product, its id otherwise — so a link, a reload or Back opens and
closes it, and the list keeps its sort and page behind it. A form inside it reports to it: once a form saves, the
dialog says *Saved* in a toast and closes. `.Delete(Product.Delete)` adds a **Delete** button for a stored product:
it tries the delete first (its rules may refuse it), asks in the page's `Ui.Confirm`, deletes through
`Product.Delete(id, version)`, says *Deleted* and closes. See [the editor over its list](../ui-kit.md#the-editor-over-its-list).

**The table sorts and pages in SQL.** [`Ui.Table`](../ui-kit.md#tables) is Flux's table, and it **sorts nothing
and pages nothing** itself: the page keeps which page is showing and what it is sorted by, and the query whose key
holds `_listing` reads that page — `OrderBy` becomes `ORDER BY` and `Skip`/`Take` become `LIMIT`/`OFFSET`, so the
database does the work however large the catalog grows. `Key(p.Id)` identifies each row when the page redraws.

**Nothing refreshes by hand.** Both reads are **queries**, keyed `QueryKey.For<Product>(…)`. Once any `Product` write
commits — an add, a change, a delete — every query about `Product` on this session's screen refetches. See
[queries and commands](../query.md).

`PageTitle` is what the page is called: the `App` that `rask new` wrote puts *Products | Shop* in the browser's tab.

**The dialog's toast and question are already on the page.** A save's *Saved* is said in a toast, which the host
draws for every app that places no `Ui.Toast` of its own, and a delete asks in the `Ui.Confirm` that `rask new` mounted
beside the router in `Features/Shared/App.cs` (`Render() => [Router, Ui.Confirm]`), so every page can reach both.

## 3. Already registered

There's nothing to register. `RaskApp.Create(args)` in `Program.cs` turned the database on, and it reads where the
database is from `appsettings.json`:

```jsonc
"Rask": {
  "ConnectionStrings": {
    "App": "Data Source=app.db"
  }
}
```

What that one line did for this slice:

- It registered `RaskAppDbContext` **as a factory**, never one shared context: every read and every write makes its
  own short-lived context. It opens SQLite with the production pragmas (WAL, `busy_timeout`, `foreign_keys`), so the
  writers of later chapters — jobs, mail, the outbox — share it without `database is locked`. A deploy overrides
  the connection string with `Rask__ConnectionStrings__App`.
- It pointed the model surface at that context, which is what lets `Product.Where(…)` and `Product.Change(…)` open a
  database with nothing injected.
- It added the interceptors that stamp times, bump versions, publish events and store attachments.

Nothing in the app injects that context: a page reads through the read face and writes through the aggregate's
own entries, one aggregate in one transaction ([the transaction boundary](../domain.md#the-transaction-boundary)).

## 4. Create the table

The code is ready, but `app.db` has no table for `Product` yet. EF Core **migrations** generate the schema from
your aggregates. `rask new` already created the first one — the batteries' tables — and `rask db` wraps the EF
tooling for every one after it:

```bash
rask db add AddProduct        # generate a migration for what changed in the model
```

`rask db add` writes into the `Migrations/` folder you commit alongside your code. There is no second command: the
app applies its pending migrations when it starts. Every time you change an aggregate later, it's the same:
`rask db add <Name>`, then (re)start the app.

## 5. Prove it

The rules are on the aggregate, so that is where they are tested — on a real database, but one that lives in memory
for one test. `Shop.Tests/Features/Products/ProductTests.cs`:

```csharp
using Shop.Features.Products;

namespace Shop.Tests.Features.Products;

public sealed class ProductTests
{
    [Fact]
    public async Task A_new_product_is_stored_with_what_it_was_given()
    {
        await using var database = await TestDatabase.Start();

        var id = await Product.Add(ProductName.Create("Anvil"), UnitPrice.Create(9.90m), StockLevel.Create(3));

        var stored = await Product.Where(p => p.Id == id).First();
        Assert.Equal("Anvil", stored.Name);
        Assert.Equal(9.90m, stored.Price);
    }

    [Fact]
    public async Task A_discontinued_product_keeps_its_price()
    {
        await using var database = await TestDatabase.Start();
        var product = await Test.Make<Product>();
        await Product.Discontinue(product.Id, product.Version);

        var refusal = await Test.Refuses(
            () => Product.Change(product.Id, product.Version + 1, product.Name, UnitPrice.Create(5m), product.Stock));

        refusal.Only("Price");
    }

    [Fact]
    public void A_price_below_zero_is_refused()
    {
        var refusal = Test.Refuses(() => UnitPrice.Create(-1m, "Price"));

        refusal.Only("Price");
    }

    [Fact]
    public async Task The_edit_form_says_a_discontinued_product_keeps_its_price()
    {
        await using var database = await TestDatabase.Start();
        var product = await Test.Make<Product>();
        await Product.Discontinue(product.Id, product.Version);

        await Test.FormShows(
            await ProductChangeInput.Of(product.Id), m => m.Price, 5m, "A discontinued product keeps its last price.");
    }

    [Fact]
    public Task The_new_product_form_says_a_name_is_too_long() =>
        Test.FormShows<ProductAddInput>(m => m.Name, new string('x', 201), "Enter at most 200 characters.");

    [Fact]
    public async Task Two_edits_of_one_version_keep_one_and_refuse_the_other()
    {
        await using var database = await TestDatabase.Start();
        var product = await Test.Make<Product>();

        var refusal = await Test.Concurrently(
            () => Product.Change(product.Id, product.Version, ProductName.Create("Anvil"), product.Price, product.Stock),
            () => Product.Change(product.Id, product.Version, ProductName.Create("Hammer"), product.Price, product.Stock));

        refusal.Whole();
    }
}
```

Each test is named as a sentence and shaped in three blocks — setup, action, checks — which the build holds you to
([RASK116](../diagnostics.md#rask116)). What each tool does:

- **`TestDatabase.Start()`** is a SQLite database in memory for this test alone, made from the app's own model, so a
  save runs exactly what it runs in production — the value objects' rules, the `Refuse`, the interceptors, the
  version check. Tests run side by side, each on its own database. Nothing is mocked.
- **`Test.Make<Product>()`** stores a product its rules accept, made through its own factory with a valid value for
  every parameter. The test says only what it cares about.
- **`Test.Refuses(…)`** asserts that something was refused, and `.Only("Price")` that it was refused under *Price*
  alone — the field the reader would see the message under.
- **`Test.FormShows(…)`** proves the reader is told: it puts the value in a real form over the operation, leaves the
  field, and checks the message under it. `ProductChangeInput.Of(product.Id)` fills the form exactly as
  `Form.For(Product.Change, id)` does on the page — read from the stored product, at its version — so only the field
  under test changes. A factory's input (`ProductAddInput`) starts empty, as its form does.
- **`Test.Concurrently(…)`** starts two saves of the same version at the same moment and checks that exactly one
  went through and the other was refused — the version check, proved.

No call is handed a `CancellationToken`: inside a test, every Rask call that takes one and is given none uses the
test's own (xUnit's `TestContext.Current.CancellationToken`), so a test that is stopped stops its database work too.
See [testing the domain](../testing.md#testing-the-domain).

## 6. Run it

```bash
dotnet test        # the tests, on SQLite in memory
rask dev           # the app, on app.db
```

Browse to **`/products`**. You get a sortable, paged list with **New product**, and an editor over it for each row —
its details, its photo, *Discontinue* and *Delete*. Create a product and refresh: it's still there, on disk in
`app.db`.

## Verify

- `Features/Products/` holds the aggregate and one page — no configuration class, no form model, no service.
- `dotnet test` passes, and the app builds with no warnings.
- After `rask db add AddProduct` and a start, `/products` renders.
- Discontinue a product, then type a new price into its form: the message appears under *Price* as you leave the
  field, before you save.
- Open the same product's editor in two tabs and save both: the second is told someone else changed it.

> **Troubleshooting.** `rask db` can't find the project → `cd` into `Shop` first. `/products` fails with
> `no such table` → you skipped `rask db add AddProduct`, or the app was already running — restart it. The IDE can't
> find `Product.Change`, `ProductAddInput` or `Product.Where` → those are generated; build once and it catches up.

**Learn more:** [domain modelling](../domain.md) · [forms over an operation](../forms.md#a-form-over-an-operation) ·
[testing the domain](../testing.md#testing-the-domain) · [tables](../ui-kit.md#tables) · [the `rask` CLI](../cli.md)

Next → **[Chapter 3: A second feature + locking it down](03-orders-and-auth.md)**
