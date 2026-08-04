# Chapter 6 — Caching the catalog

> **Goal:** stop hitting the database for the product list on every page load.
> **You'll run:** `rask generate cache CatalogCache --feature Products`

The catalog changes rarely but is read constantly. `Rask.Cache` gives you a typed cache — again backed by
the same SQLite database, so there's nothing new to run — with the one method you'll reach for most:
`GetOrCreateAsync`.

## 1. Scaffold the cache accessor

```bash
rask generate cache CatalogCache --feature Products
```

That writes `Features/Products/CatalogCache.cs`: a small class that owns **one cached value** — its key, its
expiry, how to compute it, and how to invalidate it.

That grouping is the point. The tempting shape is an inline `cache.GetOrCreateAsync("catalog:all", …)` at
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

If you scaffolded with `--cache` in [Chapter 1](01-scaffold.md), the package, the
`AddRaskCache<AppDbContext>()` registration and the `modelBuilder.AddRaskCache()` schema call are already
there. If not, the command prints exactly what to add — then `rask db add AddCache && rask db update`.

Register the accessor and you can inject it anywhere:

```csharp
builder.Services.AddScoped<CatalogCache>();
```

## 2. Cache the product list

Fill in the generated `GetAsync` with the read you actually want to avoid. Two notes on the shape:

- **Project to a record.** Cache values round-trip as JSON, and the `Product` entity's private setters don't
  survive that. A lightweight `ProductListItem` does.
- **Return the cached value or compute it.** That's the whole of `GetOrCreateAsync`: hit, or run your
  factory, store the result, and return it.

```csharp
public sealed record ProductListItem(Guid Id, string Name, decimal Price, bool InStock);

public Task<IReadOnlyList<ProductListItem>> GetAsync(CancellationToken cancellationToken = default) =>
    cache.GetOrCreateAsync(
        Key,
        async token =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(token);
            return (IReadOnlyList<ProductListItem>)await db.Products
                .AsNoTracking()
                .OrderBy(p => p.Id)
                .Select(p => new ProductListItem(p.Id, p.Name.Value, p.Price, p.InStock))
                .ToListAsync(token);
        },
        Lifetime,
        cancellationToken);
```

Then have `ListProductsQueryHandler` call `catalogCache.GetAsync(ct)` instead of querying directly, and
change the page's field to `private IReadOnlyList<ProductListItem> _items = [];`. The rest of
`ProductsPage` already reads `x.Id`, `x.Name`, `x.Price`, `x.InStock`, so nothing else changes.

## 3. Invalidate when the catalog changes

A cache is only correct if you clear it when the underlying data changes. In the create / update / delete
handlers (`CreateProduct.cs`, `UpdateProduct.cs`, `DeleteProduct.cs`), after saving:

```csharp
await catalogCache.InvalidateAsync(ct);
```

Invalidating at the point of the **write** — not on a timer, not on read — is what keeps the cache from
serving an answer you know is wrong. The expiry is a backstop for the cases you forget, not the mechanism.

Now the list is served from cache until it expires *or* someone edits the catalog, whichever comes first.

> **Trimming / AOT.** The simple `GetAsync`/`SetAsync`/`GetOrCreateAsync` overloads use reflection-based JSON
> and are annotated `[RequiresUnreferencedCode]`. If you publish trimmed or AOT, use the overloads that take a
> `JsonTypeInfo<T>` from a source-generated `JsonSerializerContext` instead — same methods, no reflection.

## Verify

- Load `/products` twice — the second load doesn't run the `Select…ToListAsync` query (add a log line in the
  factory to see it fire only on a miss).
- Create or delete a product and reload — the list reflects the change immediately (the `InvalidateAsync`
  dropped the key), then is served from cache again.
- **See it running:** [`samples/Rask.Example.Shop`](../../samples/Rask.Example.Shop)'s `/ops` page has a
  "Load cached value" button that reports `Computed fresh` on the first click and `Served from cache` on the
  second.

**Learn more:** [cache](../cache.md)

Next → **[Chapter 7: Domain events + the outbox](07-outbox-events.md)**
