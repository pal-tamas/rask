# Chapter 6 — Caching the catalog

> **Goal:** stop hitting the database for the product list on every page load.
> **You'll write:** one line in `ProductsPage`, and one in each component that changes the catalog.

The catalog changes rarely but is read constantly. `Rask.Cache` gives you a typed cache — again backed by
the same SQLite database, so there's nothing new to run — and you reach it the way you say it:
**remember the catalog for five minutes**.

```csharp
_items = await Cache.Remember("catalog", LoadProducts).For(5.Minutes);
```

Nothing to inject and nothing to register: Chapter 1's `rask new` already wrote
`builder.Services.AddRaskCache<AppDbContext>()` and mapped the cache table, which the first migration created.

## 1. Remember the product list

Give `ProductsPage` the read you want to avoid, and have it load through the cache when it mounts. Two notes
on the shape:

- **Project to a record.** Cache values round-trip as JSON, and the `Product` entity's private setters don't
  survive that. A lightweight `ProductListItem` does — and `Select` reads only the columns it names.
- **Remember, or load.** That is the whole of `Remember`: a hit returns what was stored; a miss runs
  `LoadProducts`, stores the result for five minutes, and returns it.

```csharp
public sealed record ProductListItem(Guid Id, string Name, decimal Price, bool InStock, int Version);

[Route("/products")]
public sealed partial class ProductsPage : Component
{
    private IReadOnlyList<ProductListItem> _items = [];

    protected override async Task Mount() => await Load();

    private async Task Load() => _items = await Cache.Remember("catalog", LoadProducts).For(5.Minutes);

    private static async Task<IReadOnlyList<ProductListItem>> LoadProducts() =>
        await Product
            .OrderBy(p => p.Name)
            .Select(p => new ProductListItem(p.Id, p.Name, p.Price, p.InStock, p.Version))
            .ToListAsync();

    // … and in Render(), the grid takes the list, and a delete reloads it:
    //     UiDataGrid.Data(_items).RowKey(p => p.Id) …
    //     DeleteProduct.Id(p.Id).Version(p.Version).OnDeleted(Load)
}
```

`Version` rides along because the list's delete button sends it back (Chapter 2). Neither call takes a
cancellation token: both run inside the page's own work and are cancelled with it.

The columns read `p.Name`, `p.Price` and `p.InStock` either way, so nothing else in `Render()` changes. What
does change is where the grid does its work: handed a list rather than a query, it sorts and pages **in
memory**. That's the right trade for a set small enough to cache — and being small enough to cache is the
reason you're here.

## 2. Forget it when the catalog changes

A cache is only correct if you clear it when the underlying data changes. In the three components that write
(`CreateProduct.cs`, `UpdateProduct.cs`, `DeleteProduct.cs`), right after the write succeeds — before
navigating away:

```csharp
await Cache.Forget("catalog");
```

Forgetting at the point of the **write** — not on a timer, not on read — is what keeps the cache from
serving an answer you know is wrong. The five minutes are a backstop for the cases you forget, not the
mechanism.

Now the list is served from cache until it expires *or* someone edits the catalog, whichever comes first.

> **One key, four places.** `"catalog"` is written in the page and in each of the three writers. When the
> shape of `ProductListItem` changes, change the key with it (`"catalog:v2"`) so an entry stored in the old
> shape is simply never read again.

> **Trimming / AOT.** Values are stored as JSON. A trimmed or AOT app registers its source-generated
> `JsonSerializerContext` once — `AddRaskCache<AppDbContext>(o => o.Json = AppJson.Default)` — and every
> `Remember`, `Set` and `Get` stays exactly as written here.

## Verify

- Load `/products` twice — the second load doesn't run the `Select…ToListAsync` query (add a log line in
  `LoadProducts` to see it fire only on a miss).
- Create or delete a product and reload — the list reflects the change immediately (`Forget` dropped the
  key), then is served from cache again.

**Learn more:** [cache](../cache.md) · [data grid](../data-grid.md)

Next → **[Chapter 7: Domain events + the outbox](07-outbox-events.md)**
