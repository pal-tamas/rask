# Chapter 6 — Caching the catalog

> **Goal:** stop hitting the database for the product list on every page load.
> **You'll write:** a cached read accessor under `Features/Products/`.

The catalog changes rarely but is read constantly. `Rask.Cache` gives you a typed cache — again backed by
the same SQLite database, so there's nothing new to run — and the line you'll write most reads as what it
does: `await Cache.Remember("products", Load).For(5.Minutes)`.

## 1. Write the cache accessor

Create `Features/Products/CatalogCache.cs`: a small class that owns **one cached value** — its key, its
expiry, how to compute it, and how to forget it.

That grouping is the point. The tempting shape is an inline `Cache.Remember("catalog:all", …)` at each
call site, and it works right up until the day the data changes and one of the four places that should
have dropped the key didn't. A cache you can't find every use of is a cache you can't reason about.

```csharp
namespace Shop.Features.Products;

public static class CatalogCache
{
    // Versioned rather than mutated in place: bump the suffix when the shape of the value changes and
    // stale entries under the old key are simply never read again.
    private const string Key = "catalogcache:v1";

    public static async Task<IReadOnlyList<ProductListItem>> Get() => …
    public static Task Forget() => …
}
```

Static, and nothing injected: `Cache` reaches the cache of whatever work is running — a render, a request,
a handler, a job — so there is no constructor to thread through and no registration line to remember.
The cache battery is on and its table is mapped, and the first migration created it. That is the whole of
the setup.

## 2. Cache the product list

Fill in `Get()` with the read you actually want to avoid. Two notes on the shape:

- **Project to a record.** Cache values round-trip as JSON, and the `Product` entity's private setters don't
  survive that. A lightweight `ProductListItem` does — and `Select` reads only the columns it names.
- **Say how long it keeps.** `Remember` returns the stored value or runs your loader and stores what it
  returns; `.For(5.Minutes)` is how long that answer stands. Nothing is read or loaded until you await it.

```csharp
public sealed record ProductListItem(Guid Id, string Name, decimal Price, bool InStock, DateTime UpdatedAt, int Version);

public static async Task<IReadOnlyList<ProductListItem>> Get() =>
    await Cache.Remember(Key, Load).For(5.Minutes);

private static async Task<IReadOnlyList<ProductListItem>> Load() =>
    await Product
        .OrderBy(p => p.Name)
        .Select(p => new ProductListItem(p.Id, p.Name, p.Price, p.InStock, p.UpdatedAt, p.Version));
```

The record goes at the top of the file, beside the class; the two methods go inside it. `Get` is `async`
because `Remember(…).For(…)` is something to await, not a `Task` to hand back. `UpdatedAt` is there for the
table's *Updated* column, and `Version` because the list's delete button sends it back (Chapter 2). The read
takes no token: like `Cache` itself, it is cancelled with the work in progress, with nothing passed in.

Then have `ProductsPage` read through the accessor instead of asking the database for each page. The page
loads the list when it mounts, and reloads it after a delete. Here is the whole file, with chapter 3's
`Authorize` gates still in it:

```csharp
namespace Shop.Features.Products;

[Route("/products")]
public sealed partial class ProductsPage : Component
{
    private const int PageSize = 20;

    // Which page is on screen and what it is sorted by. The table draws these; it sorts and pages nothing itself.
    private Listing _listing = new(Page: 1, SortBy: "name", Descending: false);

    // The whole catalog, from the cache. Sorted and paged below, in memory.
    private IReadOnlyList<ProductListItem> _items = [];

    protected override string? PageTitle => "Products";

    protected override async Task OnMount() => await Load();

    private async Task Load() => _items = await CatalogCache.Get();

    protected override Component? Render() =>
    [
        Div.Class("flex flex-wrap items-center gap-3")[
            Ui.Heading.Level(1).Xl[$"Products ({_items.Count})"],
            Ui.Spacer,
            Authorize[
                Ui.Button.Primary.Href(Routes.NewProductPage())["New product"]
            ]
        ],
        Ui.Table.Class("w-full")
            .Paginate(Ui.Pagination
                .Paginator(new UiPaginator { Page = _listing.Page, PerPage = PageSize, Total = _items.Count })
                .OnPage(page => { _listing = _listing with { Page = page }; }))[
            Ui.TableColumns[
                Sortable("name", "Name"),
                Sortable("price", "Price", "w-32"),
                Ui.TableColumn.Class("w-28")["In stock"],
                Ui.TableColumn.Class("w-52")["Updated"],
                Ui.TableColumn.Class("w-44")[""]
            ],
            Ui.TableRows[
                OnePage().Select(p => Ui.TableRow.Key(p.Id)[
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Class("truncate")[p.Name],
                    Ui.TableCell[$"{p.Price:C}"],
                    Ui.TableCell[p.InStock ? "Yes" : "No"],
                    Ui.TableCell[$"{p.UpdatedAt:g}"],
                    Ui.TableCell.Class("py-0")[
                        Authorize[
                            Ui.Button.Ghost.Sm.Href(Routes.EditProductPage(p.Id))["Edit"]
                        ],
                        Authorize.Role("admin")[
                            DeleteProductButton.Id(p.Id).Version(p.Version).OnDeleted(Load)
                        ]
                    ]
                ])
            ]
        ]
    ];

    // A heading the reader can sort by. A second click on the sorted one turns it round.
    private Component Sortable(string column, string label, string? width = null) =>
        Ui.TableColumn
            .Class(width)
            .Sortable()
            .Sorted(_listing.SortBy == column)
            .Direction(_listing.Descending ? Ui.TableColumnDirection.Desc : Ui.TableColumnDirection.Asc)
            .OnSort(() => { _listing = new(1, column, _listing.SortBy == column && !_listing.Descending); })[label];

    // The same sort and page chapter 2 asked SQL for, over the cached list.
    private IEnumerable<ProductListItem> OnePage()
    {
        var sorted = (_listing.SortBy, _listing.Descending) switch
        {
            ("price", false) => _items.OrderBy(p => p.Price),
            ("price", true) => _items.OrderByDescending(p => p.Price),
            (_, false) => _items.OrderBy(p => p.Name),
            (_, true) => _items.OrderByDescending(p => p.Name),
        };

        return sorted.Skip((_listing.Page - 1) * PageSize).Take(PageSize);
    }

    private sealed record Listing(int Page, string SortBy, bool Descending);
}
```

The cells read the same columns as before, so the table itself does not change, and nor do the sort
headings or the pager. What does change is where the work happens: with a list rather than a query, the page
sorts and pages it **in memory**, and it no longer refreshes itself after a write, which is why the delete
button now reloads it with `OnDeleted(Load)`. That's the right trade for a set small enough to cache — and being small enough to cache is the
reason you're here.

## 3. Forget it when the catalog changes

A cache is only correct if you clear it when the underlying data changes. Finish the accessor with the
other half of the pair:

```csharp
public static Task Forget() => Cache.Forget(Key);
```

Then in the three components that write (`NewProductPage.cs`, `EditProductPage.cs`, `DeleteProductButton.cs`), right
after the write — after `Product.Create`/`Product.Update` inside the save, before navigating away, and after
`delete.Send` in the button, before `OnDeleted`:

```csharp
await CatalogCache.Forget();
```

Forgetting at the point of the **write** — not on a timer, not on read — is what keeps the cache from
serving an answer you know is wrong. The expiry is a backstop for the cases you forget, not the mechanism.

Now the list is served from cache until it expires *or* someone edits the catalog, whichever comes first.
The finished `Features/Products/CatalogCache.cs`:

```csharp
namespace Shop.Features.Products;

public sealed record ProductListItem(Guid Id, string Name, decimal Price, bool InStock, DateTime UpdatedAt, int Version);

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
            .Select(p => new ProductListItem(p.Id, p.Name, p.Price, p.InStock, p.UpdatedAt, p.Version));
}
```

> **Trimming / AOT.** Cached values round-trip as JSON, which by default means reflection. If you publish
> trimmed or AOT, name a source-generated context once in `Program.cs` —
> `app.Configure(c => c.Cache.Configure(o => o.Json = AppJson.Default))` — and every `Remember`, `Set` and `Get` stays exactly as written.

## Verify

- Load `/products` twice — the second load doesn't run the `Select` query (add a log line in `Load` to see it
  fire only on a miss).
- Create or delete a product and reload — the list reflects the change immediately (the `Forget` dropped
  the key), then is served from cache again.

**Learn more:** [cache](../cache.md) · [tables](../ui-kit.md#tables)

Next → **[Chapter 7: Domain events + the outbox](07-outbox-events.md)**
