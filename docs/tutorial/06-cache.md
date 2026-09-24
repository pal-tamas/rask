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
public static class CatalogCache
{
    // Versioned rather than mutated in place: bump the suffix when the shape of the value changes and
    // stale entries under the old key are simply never read again.
    private const string Key = "catalogcache:v1";

    public static Task<IReadOnlyList<ProductListItem>> Get() => …
    public static Task Forget() => …
}
```

Static, and nothing injected: `Cache` reaches the cache of whatever work is running — a render, a request,
a handler, a job — so there is no constructor to thread through and no registration line to remember.
Chapter 1's `rask new` already wrote `builder.Services.AddRaskCache<AppDbContext>()` and mapped the cache
table, which the first migration created. That is the whole of the setup.

## 2. Cache the product list

Fill in `Get()` with the read you actually want to avoid. Two notes on the shape:

- **Project to a record.** Cache values round-trip as JSON, and the `Product` entity's private setters don't
  survive that. A lightweight `ProductListItem` does — and `Select` reads only the columns it names.
- **Say how long it keeps.** `Remember` returns the stored value or runs your loader and stores what it
  returns; `.For(5.Minutes)` is how long that answer stands. Nothing is read or loaded until you await it.

```csharp
public sealed record ProductListItem(Guid Id, string Name, decimal Price, bool InStock, int Version);

public static Task<IReadOnlyList<ProductListItem>> Get() =>
    Cache.Remember(Key, Load).For(5.Minutes);

private static async Task<IReadOnlyList<ProductListItem>> Load() =>
    await Product
        .OrderBy(p => p.Name)
        .Select(p => new ProductListItem(p.Id, p.Name, p.Price, p.InStock, p.Version))
        .ToListAsync(Current.Cancellation);
```

`Version` rides along because the list's delete button sends it back (Chapter 2). `Current.Cancellation` is
the same ambient idea as `Cache` itself: the cancellation of the work in progress, with nothing passed in.

Then have `ProductsPage` read through the accessor instead of handing the grid a query. The page loads the
list when it mounts, and reloads it after a delete:

```csharp
[Route("/products")]
public sealed partial class ProductsPage : Component
{
    private IReadOnlyList<ProductListItem> _items = [];

    protected override async Task OnMount() => await Load();

    private async Task Load() => _items = await CatalogCache.Get();

    // … and in Render(), the grid takes the list, and a delete reloads it:
    //     Ui.DataGrid.Data(_items).RowKey(p => p.Id) …
    //     DeleteProduct.Id(p.Id).Version(p.Version).OnDeleted(Load)
}
```

The columns read `p.Name`, `p.Price` and `p.InStock` either way, so nothing else in `Render()` changes. What
does change is where the grid does its work: handed a list rather than a query, it sorts and pages **in
memory**. That's the right trade for a set small enough to cache — and being small enough to cache is the
reason you're here.

## 3. Forget it when the catalog changes

A cache is only correct if you clear it when the underlying data changes. Finish the accessor with the
other half of the pair:

```csharp
public static Task Forget() => Cache.Forget(Key);
```

Then in the three components that write (`CreateProduct.cs`, `UpdateProduct.cs`, `DeleteProduct.cs`), right
after the write succeeds — before navigating away:

```csharp
await CatalogCache.Forget();
```

Forgetting at the point of the **write** — not on a timer, not on read — is what keeps the cache from
serving an answer you know is wrong. The expiry is a backstop for the cases you forget, not the mechanism.

Now the list is served from cache until it expires *or* someone edits the catalog, whichever comes first.

> **Trimming / AOT.** Cached values round-trip as JSON, which by default means reflection. If you publish
> trimmed or AOT, name a source-generated context once — `AddRaskCache<AppDbContext>(o => o.Json =
> AppJson.Default)` — and every `Remember`, `Set` and `Get` stays exactly as written.

## Verify

- Load `/products` twice — the second load doesn't run the `Select…ToListAsync` query (add a log line in
  `Load` to see it fire only on a miss).
- Create or delete a product and reload — the list reflects the change immediately (the `Forget` dropped
  the key), then is served from cache again.

**Learn more:** [cache](../cache.md) · [data grid](../data-grid.md)

Next → **[Chapter 7: Domain events + the outbox](07-outbox-events.md)**
