# Rask.Cache

A **developer-facing cache** for a Rask app — stored in the app's own database, with no broker or Redis.

- Implements the standard **`IDistributedCache`**, so it drops straight into ASP.NET session state, output
  caching, and anything else built on the abstraction.
- A typed **`ICache`** convenience layer adds `GetOrCreateAsync<T>` read-through, plus `GetAsync<T>` /
  `SetAsync<T>` / `RemoveAsync` (JSON under the hood).
- Entries carry **absolute** and **sliding** expirations; a read renews a sliding entry and an expired entry is
  evicted lazily. A background **`CachePurger`** sweeps expired rows on an interval.

## Use

```csharp
// Program.cs
builder.Services.AddRaskCache<AppDbContext>();

// AppDbContext.OnModelCreating:  modelBuilder.AddRaskCache();
// then:  rask db add AddCache && rask db update
```

```csharp
// read-through: the factory runs once on a miss, then the value is served from the DB.
var rates = await cache.GetOrCreateAsync(
    $"rates:{date:yyyyMMdd}",
    ct => exchange.FetchRatesAsync(date, ct),
    new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) });
```

Register your context as an `IDbContextFactory<AppDbContext>` (Rask Server sessions are long-lived) and run
**one purger per app** — SQLite is single-writer.

## Already running Redis?

`ICache` works over any `IDistributedCache`, so point it at the one you have — no `CacheEntry` table, no
purge worker, no migration:

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379");
builder.Services.AddRaskCache();   // no <AppDbContext>
```

There is deliberately no `Rask.Cache.Redis` package —
`Microsoft.Extensions.Caching.StackExchangeRedis` is the standard .NET API for this. The overload takes no
`CacheOptions`, because both of them are implemented by the database-backed store and would silently do
nothing against another one.

> **Trim / AOT:** the typed `GetOrCreateAsync<T>`/`GetAsync<T>`/`SetAsync<T>` overloads use reflection-based
> `System.Text.Json`. In a trimmed or AOT app, use the `JsonTypeInfo<T>` overloads with a source-generated
> `JsonSerializerContext`. The `IDistributedCache` (`byte[]`) surface is fully trim-safe.
