# Rask.Cache

A **developer-facing cache** for a Rask app — stored in the app's own database, with no broker or Redis.

- Implements the standard **`IDistributedCache`**, so it drops straight into ASP.NET session state, output
  caching, and anything else built on the abstraction.
- A typed surface that reads as a sentence, with nothing injected: `Cache.Remember(key, load).For(10.Minutes)`
  read-through, plus `Cache.Get<T>` / `Cache.Set(key, value).For(…)` / `Cache.Forget` (JSON under the hood).
  `ICache` words the same calls for a hosted service or a timer.
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
var rates = await Cache.Remember($"rates:{date:yyyyMMdd}", ct => exchange.FetchRatesAsync(date, ct))
    .Sliding(10.Minutes);
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

**Isolated per tenant.** In a multi-tenant app the database-backed store scopes each key to the tenant in
flight, so every tenant gets its own value under the same name and cannot read another's; with no tenant the
key is stored as given, which keeps anonymous session and output caching working.

> **Trim / AOT:** the typed `Remember<T>`/`Get<T>`/`Set<T>` surface round-trips values through
> `System.Text.Json`, which by default means reflection. In a trimmed or AOT app, name a source-generated
> `JsonSerializerContext` once — `o.Json = AppJson.Default` — and every call stays as it is. The
> `IDistributedCache` (`byte[]`) surface is fully trim-safe.
