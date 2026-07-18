# Chapter 6 — Caching the catalog

> **Goal:** stop hitting the database for the product list on every page load.
> **You'll add:** `AddRaskCache<ProductsDbContext>()` and one `GetOrCreateAsync` call.

The catalog changes rarely but is read constantly. `Rask.Cache` gives you a typed cache — again backed by
the same SQLite database, so there's nothing new to run — with the one method you'll reach for most:
`GetOrCreateAsync`.

## 1. Wire it up

In `Program.cs`:

```csharp
builder.Services.AddRaskCache<ProductsDbContext>();
```

Map the cache table in `ProductsDbContext.OnModelCreating`:

```csharp
modelBuilder.AddRaskCache();        // ← the CacheEntry table
```

Migrate:

```bash
rask db add AddCache
rask db update
```

## 2. Cache the product list

Open the generated list query in `Features/Products/ProductsPage.cs`. Today `ListProductsQueryHandler`
returns `IReadOnlyList<Product>` straight from the database on every call. We'll make two small changes:
project to a lightweight record (cache values are JSON, and the `Product` entity's private setters don't
round-trip cleanly), and wrap the read in `ICache.GetOrCreateAsync` — which returns the cached value if
present, otherwise runs your factory, stores the result, and returns it.

Add a record and update the query + handler:

```csharp
public sealed record ProductListItem(Guid Id, string Name, decimal Price, bool InStock);

public sealed record ListProductsQuery : IQuery<IReadOnlyList<ProductListItem>>;

public sealed class ListProductsQueryHandler(
    IDbContextFactory<ProductsDbContext> dbContextFactory,
    ICache cache) : IQueryHandler<ListProductsQuery, IReadOnlyList<ProductListItem>>
{
    public Task<IReadOnlyList<ProductListItem>> HandleAsync(ListProductsQuery query, CancellationToken ct) =>
        cache.GetOrCreateAsync(
            "catalog:all",
            async token =>
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                return (IReadOnlyList<ProductListItem>)await db.Products
                    .AsNoTracking()
                    .OrderBy(p => p.Id)
                    .Select(p => new ProductListItem(p.Id, p.Name, p.Price, p.InStock))
                    .ToListAsync(token);
            },
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(5) });
}
```

Then update the page's field to match the new type — `private IReadOnlyList<ProductListItem> _items = [];`.
The rest of `ProductsPage` already reads `x.Id`, `x.Name`, `x.Price`, `x.InStock`, so nothing else changes.

(`DistributedCacheEntryOptions` lives in `Microsoft.Extensions.Caching.Distributed` — add the `using` if the
IDE prompts for it.)

## 3. Invalidate when the catalog changes

A cache is only correct if you clear it when the underlying data changes. In the create / update / delete
handlers (`CreateProduct.cs`, `UpdateProduct.cs`, `DeleteProduct.cs`), remove the key after saving:

```csharp
await cache.RemoveAsync("catalog:all", ct);
```

Now the list is served from cache until it expires *or* someone edits the catalog, whichever comes first.

> **Trimming / AOT.** The simple `GetAsync`/`SetAsync`/`GetOrCreateAsync` overloads use reflection-based JSON
> and are annotated `[RequiresUnreferencedCode]`. If you publish trimmed or AOT, use the overloads that take a
> `JsonTypeInfo<T>` from a source-generated `JsonSerializerContext` instead — same methods, no reflection.

## Verify

- Load `/products` twice — the second load doesn't run the `Select…ToListAsync` query (add a log line in the
  factory to see it fire only on a miss).
- Create or delete a product and reload — the list reflects the change immediately (the `RemoveAsync`
  invalidated the key), then is served from cache again.

**Learn more:** [cache](../cache.md)

Next → **[Chapter 7: Domain events + the outbox](07-outbox-events.md)**
