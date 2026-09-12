# Chapter 6 — Caching the catalog

> **Goal:** stop hitting the database for the product list on every page load.
> **You'll write:** a cached read accessor under `Features/Products/`.

The catalog changes rarely but is read constantly. `Rask.Cache` gives you a typed cache — again backed by
the same SQLite database, so there's nothing new to run — with the one method you'll reach for most:
`GetOrAddAsync`.

## 1. Write the cache accessor

Create `Features/Products/CatalogCache.cs`: a small class that owns **one cached value** — its key, its
expiry, how to compute it, and how to invalidate it.

That grouping is the point. The tempting shape is an inline `cache.GetOrAddAsync("catalog:all", …)` at
each call site, and it works right up until the day the data changes and one of the four places that
should have dropped the key didn't. A cache you can't find every use of is a cache you can't reason about.

```csharp
public sealed class CatalogCache(ICache cache)
{
    // Versioned rather than mutated in place: bump the suffix when the shape of the value changes and
    // stale entries under the old key are simply never read again.
    private const string Key = "catalogcache:v1";

    private static readonly DistributedCacheEntryOptions Lifetime =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };

    public Task<string> GetAsync(CancellationToken cancellationToken = default) => …
    public Task InvalidateAsync(CancellationToken cancellationToken = default) => …
}
```

Chapter 1's `rask new` already wrote `builder.Services.AddRaskCache<AppDbContext>()` and mapped the cache
table, which the first migration created — so this class is the only thing to write. Register it next to
that line in `Program.cs` and you can inject it anywhere:

```csharp
builder.Services.AddScoped<CatalogCache>();
```

## 2. Cache the product list

Fill in `GetAsync` with the read you actually want to avoid. Two notes on the shape:

- **Project to a record.** Cache values round-trip as JSON, and the `Product` entity's private setters don't
  survive that. A lightweight `ProductListItem` does — and `Select` reads only the columns it names.
- **Return the cached value or compute it.** That's the whole of `GetOrAddAsync`: hit, or run your
  factory, store the result, and return it.

```csharp
public sealed record ProductListItem(Guid Id, string Name, decimal Price, bool InStock, int Version);

public Task<IReadOnlyList<ProductListItem>> GetAsync(CancellationToken cancellationToken = default) =>
    cache.GetOrAddAsync<IReadOnlyList<ProductListItem>>(
        Key,
        async token => await Product
            .OrderBy(p => p.Name)
            .Select(p => new ProductListItem(p.Id, p.Name, p.Price, p.InStock, p.Version))
            .ToListAsync(token),
        Lifetime,
        cancellationToken);
```

`Version` rides along because the list's delete button sends it back (Chapter 2).

Then have `ProductsPage` read through the accessor instead of handing the grid a query. The page takes the
cache in its constructor, loads the list when it mounts, and reloads it after a delete:

```csharp
[Route("/products")]
public sealed partial class ProductsPage(CatalogCache catalog) : Component
{
    private IReadOnlyList<ProductListItem> _items = [];

    protected override async Task OnMountAsync() => await LoadAsync();

    private async Task LoadAsync() => _items = await catalog.GetAsync(CancellationToken);

    // … and in Render(), the grid takes the list, and a delete reloads it:
    //     UiDataGrid.Data(_items).RowKey(p => p.Id) …
    //     DeleteProduct.Id(p.Id).Version(p.Version).OnDeleted(LoadAsync)
}
```

The columns read `p.Name`, `p.Price` and `p.InStock` either way, so nothing else in `Render()` changes. What
does change is where the grid does its work: handed a list rather than a query, it sorts and pages **in
memory**. That's the right trade for a set small enough to cache — and being small enough to cache is the
reason you're here.

## 3. Invalidate when the catalog changes

A cache is only correct if you clear it when the underlying data changes. In the three components that write
(`CreateProduct.cs`, `UpdateProduct.cs`, `DeleteProduct.cs`), inject `CatalogCache` as `catalog` and, right
after the write succeeds — before navigating away:

```csharp
await catalog.InvalidateAsync(CancellationToken);
```

Invalidating at the point of the **write** — not on a timer, not on read — is what keeps the cache from
serving an answer you know is wrong. The expiry is a backstop for the cases you forget, not the mechanism.

Now the list is served from cache until it expires *or* someone edits the catalog, whichever comes first.

> **Trimming / AOT.** The simple `GetAsync`/`SetAsync`/`GetOrAddAsync` overloads use reflection-based JSON
> and are annotated `[RequiresUnreferencedCode]`. If you publish trimmed or AOT, use the overloads that take a
> `JsonTypeInfo<T>` from a source-generated `JsonSerializerContext` instead — same methods, no reflection.

## Verify

- Load `/products` twice — the second load doesn't run the `Select…ToListAsync` query (add a log line in the
  factory to see it fire only on a miss).
- Create or delete a product and reload — the list reflects the change immediately (the `InvalidateAsync`
  dropped the key), then is served from cache again.

**Learn more:** [cache](../cache.md) · [data grid](../data-grid.md)

Next → **[Chapter 7: Domain events + the outbox](07-outbox-events.md)**
