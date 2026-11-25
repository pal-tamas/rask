# Chapter 6 — Caching the catalog

> **Goal:** stop hitting the database for the product list on every page load — and forget it the moment the catalog
> changes, however it changed.
> **You'll write:** a cached read accessor, a domain event, and the handler that hears it.

The catalog changes rarely but is read constantly. `Rask.Cache` gives you a typed cache — again backed by the same
SQLite database, so there's nothing new to run — and the line you'll write most reads as what it does:
`await Cache.Remember(key, Load).For(5.Minutes)`.

## 1. Say when the catalog changed

A cache is only correct if it is forgotten when the data under it changes — and the one place that knows a product
changed is the product. So it says so, with a **domain event**. Here is `Features/Products/Product.cs` raising one:

```csharp
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shop.Features.Products;

public readonly partial record struct ProductId(Guid Value);

public sealed partial record ProductName(string Value) : Text(Value, MaxLength: 200);

public sealed partial record UnitPrice(decimal Value) : Number(Value, Min: 0, Max: 1_000_000, Scale: 2);

public sealed partial record StockLevel(long Value) : Integer(Value, Min: 0);

public sealed record CatalogChanged(ProductId Product) : IEvent;

public sealed partial class Product : Aggregate<ProductId, Timestamps, Versioned, Deletable>
{
    public ProductName Name { get; private set; } = null!;

    public UnitPrice Price { get; private set; } = null!;

    public StockLevel Stock { get; private set; } = null!;

    public bool Discontinued { get; private set; }

    public Attachment? Photo { get; private set; }

    private static Product Add(ProductName name, UnitPrice price, StockLevel stock)
    {
        var product = new Product { Id = ProductId.New(), Name = name, Price = price, Stock = stock };
        product.Raise(new CatalogChanged(product.Id));
        return product;
    }

    public void Change(ProductName name, UnitPrice price, StockLevel stock)
    {
        Refuse.When(Discontinued && price != Price, () => price, "A discontinued product keeps its last price.");
        (Name, Price, Stock) = (name, price, stock);
        Raise(new CatalogChanged(Id));
    }

    public void Discontinue()
    {
        Discontinued = true;
        Raise(new CatalogChanged(Id));
    }

    public void Picture(Attachment photo) => Photo = photo;

    private void OnDelete() => Raise(new CatalogChanged(Id));

    public static void Configure(EntityTypeBuilder<Product> b)
    {
        b.Operation(nameof(Add), roles: "admin");
        b.Operation(p => p.Change, roles: "admin");
        b.Operation(p => p.Discontinue, roles: "admin");
        b.Operation(p => p.Picture, roles: "admin");
        b.Operation(nameof(Delete), roles: "admin");
    }
}
```

- **`CatalogChanged` is a record marked `IEvent`** — what happened, named in the past tense, carrying the id.
- **`Raise(…)` inside the operation** puts the event on the aggregate; it is published after the save commits, and
  never for a save that rolled back. The factory raises it on the product it built.
- **`OnDelete()`** is the one hook a delete has: the generated `Product.Delete` and the editor's *Delete* have no
  method body of yours to raise in, so the aggregate says it here, once, for every way it is deleted
  ([what a delete raises](../domain.md#what-a-delete-raises-ondelete)).

Every write the catalog will ever have — the editor's forms, its *Delete*, a job, the stock an order takes in
chapter 7 — now says so, and none of them knows the cache exists.

## 2. The cache accessor

`Features/Products/CatalogCache.cs`: one class that owns **one cached value** — its key, its expiry, how to compute
it, and how to forget it — and the handler that forgets it:

```csharp
namespace Shop.Features.Products;

public sealed record ProductListItem(ProductId Id, string Name, decimal Price, long Stock, DateTime UpdatedAt);

public static class CatalogCache
{
    // Versioned rather than mutated in place: bump the suffix when the shape of the value changes and
    // stale entries under the old key are simply never read again.
    private const string Key = "catalogcache:v1";

    public static async Task<IReadOnlyList<ProductListItem>> Get() =>
        await Cache.Remember(Key, Load).For(5.Minutes);

    public static Task Forget() => Cache.Forget(Key);

    private static async Task<IReadOnlyList<ProductListItem>> Load() =>
        await Product
            .OrderBy(p => p.Name)
            .Select(p => new ProductListItem(p.Id, p.Name, p.Price, p.Stock, p.UpdatedAt));
}

// Every save that changes the catalog raises CatalogChanged; this hears it once the save commits.
public sealed class ForgetTheCatalog : IEventHandler<CatalogChanged>
{
    public Task Handle(CatalogChanged e) => CatalogCache.Forget();
}
```

- **One place for the key.** An inline `Cache.Remember("catalog", …)` at each call site works right up until the day
  one of them forgets to drop it. A cache you can find every use of is one you can reason about.
- **Project to a record.** Cache values round-trip as JSON; a `ProductListItem` does, and `Select` reads only the
  columns it names. Its members are the read face's: `Name` a `string`, `Price` a `decimal`.
- **`Remember(…).For(5.Minutes)`** returns the stored value, or runs `Load` and stores what it returns. Nothing is read
  until it is awaited, which is why `Get` is `async`.
- **`IEventHandler<CatalogChanged>`** runs in memory, straight after the save that raised it commits — the right
  kind for a cache: if the process dies at that moment, the cache is gone with it. Nothing registers it; it is
  found at build time. Chapter 7 meets the other kind, for what must not be lost.

Static, and nothing injected: `Cache` reaches the cache of whatever work is running — a render, a request, a
handler, a job. The cache battery is on, its table is mapped, and the first migration created it.

## 3. Read the catalog through it

`ProductsPage` reads the cache instead of asking the database for each page. Here is the whole file, with
chapter 3's roles still in it:

```csharp
namespace Shop.Features.Products;

[Route("/products")]
public sealed partial class ProductsPage(RouteState route) : Component
{
    private const int PageSize = 20;

    private Listing _listing = new(Page: 1, SortBy: "name", Descending: false);

    [QueryParam("edit")] public string? Edit { get; set; }

    protected override string? PageTitle => "Products";

    protected override Component? Render()
    {
        // The whole catalog, from the cache: refetched after any Product write, which has forgotten it by then.
        var catalog = QueryClient.Query(QueryKey.For<Product>("catalog"), () => CatalogCache.Get());
        var items = catalog.Data ?? [];

        return
        [
            Div.Class("flex flex-wrap items-center gap-3")[
                Ui.Heading.Level(1).Xl[$"Products ({items.Count})"],
                Ui.Spacer,
                Product.MayAdd ? Ui.Button.Primary.Href(route.With("edit", UiEditorDialog.New))["New product"] : null
            ],
            Ui.Table.Class("w-full")
                .Paginate(Ui.Pagination
                    .Paginator(new UiPaginator { Page = _listing.Page, PerPage = PageSize, Total = items.Count })
                    .OnPage(page => { _listing = _listing with { Page = page }; }))[
                Ui.TableColumns[
                    Sortable("name", "Name"),
                    Sortable("price", "Price", "w-32"),
                    Ui.TableColumn.Class("w-28")["Stock"],
                    Ui.TableColumn.Class("w-52")["Updated"],
                    Ui.TableColumn.Class("w-16")[""]
                ],
                Ui.TableRows[
                    OnePage(items).Select(p => Ui.TableRow.Key(p.Id)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Class("truncate")[p.Name],
                        Ui.TableCell[$"{p.Price:C}"],
                        Ui.TableCell[$"{p.Stock}"],
                        Ui.TableCell[$"{p.UpdatedAt:g}"],
                        Product.MayChange ? Ui.RowActions.Edit(route.With("edit", p.Id.ToString())) : Ui.TableCell
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

    private Component Sortable(string column, string label, string? width = null) =>
        Ui.TableColumn
            .Class(width)
            .Sortable()
            .Sorted(_listing.SortBy == column)
            .Direction(_listing.Descending ? Ui.TableColumnDirection.Desc : Ui.TableColumnDirection.Asc)
            .OnSort(() => { _listing = new(1, column, _listing.SortBy == column && !_listing.Descending); })[label];

    // The sort and the page chapter 2 asked SQL for, over the cached list.
    private IEnumerable<ProductListItem> OnePage(IReadOnlyList<ProductListItem> items)
    {
        var sorted = (_listing.SortBy, _listing.Descending) switch
        {
            ("price", false) => items.OrderBy(p => p.Price),
            ("price", true) => items.OrderByDescending(p => p.Price),
            (_, false) => items.OrderBy(p => p.Name),
            (_, true) => items.OrderByDescending(p => p.Name),
        };

        return sorted.Skip((_listing.Page - 1) * PageSize).Take(PageSize);
    }

    private sealed record Listing(int Page, string SortBy, bool Descending);
}
```

The page still asks through a **query** keyed by `Product`, so a product saved on this screen refetches it — and by
then the handler has already forgotten the cache, so the refetch loads the new catalog. With a list rather than a
query, the page sorts and pages it **in memory**: the right trade for a set small enough to cache, which is the
reason you're here.

## 4. Prove it

Two things change in the tests. First, every save of a product now runs `ForgetTheCatalog`, and a test database has
no cache battery of its own, so `Cache` would refuse: *Cache was called outside any work in progress*. The
batteries a handler reaches are faked, once, in `Shop.Tests/FakeBatteries.cs`:

```csharp
namespace Shop.Tests;

// The batteries a save's handlers reach, faked for one test: a TestDatabase runs every handler a save raises, and has
// no cache of its own. Each fake records what it was asked and does nothing outside the test.
internal sealed class FakeBatteries : IDisposable
{
    public CacheFake Cache { get; } = Rask.Caching.Cache.Fake();

    public void Dispose() => Cache.Dispose();
}
```

…and every test that starts a database fakes them next — add this line after each `TestDatabase.Start` in
`ProductTests`, `OrderTests` and `ReceiptTests` (an order's line makes a product):

```csharp
using var batteries = new FakeBatteries();
```

A fake records what it was asked, reaches nothing outside the test, and puts the real one back when it is disposed.

Second, the cache itself — `Shop.Tests/Features/Products/CatalogCacheTests.cs`:

```csharp
using Shop.Features.Products;

namespace Shop.Tests.Features.Products;

public sealed class CatalogCacheTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_catalog_is_loaded_once_and_forgotten_when_a_product_changes()
    {
        await using var database = await TestDatabase.Start(cancellationToken: Ct);
        using var batteries = new FakeBatteries();
        using var admin = Signed.As("admin");
        var product = await Test.Make<Product>();
        batteries.Cache.Clear();   // what making the product did

        await CatalogCache.Get();
        await CatalogCache.Get();
        await Product.Discontinue(product.Id, product.Version, Ct);

        batteries.Cache.Loaded().Once();
        batteries.Cache.Forgotten().Once();
    }
}
```

Two reads, one load; one change, one forget.

> **Trimming / AOT.** Cached values round-trip as JSON, which by default means reflection. If you publish trimmed or
> AOT, name a source-generated context once in `Program.cs` —
> `app.Configure(c => c.Cache.Configure(o => o.Json = AppJson.Default))` — and every `Remember`, `Set` and `Get` stays
> exactly as written.

## Verify

- Load `/products` twice — the second load doesn't run the `Select` (a log line in `Load` fires only on a miss).
- Change a product's price, or delete one, and the list shows it at once: the event forgot the key.
- `dotnet test` passes.

**Learn more:** [cache](../cache.md) · [domain events](../domain.md#domain-events) · [tables](../ui-kit.md#tables)

Next → **[Chapter 7: Domain events + the outbox](07-outbox-events.md)**
