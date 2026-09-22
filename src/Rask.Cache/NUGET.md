# Rask.Cache

A **developer-facing cache** for a Rask app — stored in the app's own database, with no broker or Redis.

- Implements the standard **`IDistributedCache`**, so it drops straight into ASP.NET session state, output
  caching, and anything else built on the abstraction.
- A typed cache you reach the way you say it — `await Cache.Remember("rates", LoadRates).For(10.Minutes)` —
  plus `Set`, `Get` and `Forget`, with nothing injected (JSON under the hood).
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
// remember: the loader runs once on a miss, then the value is served from the DB.
var rates = await Cache.Remember($"rates:{date:yyyyMMdd}", () => exchange.FetchRates(date)).Sliding(10.Minutes);

await Cache.Set("greeting", "hello").For(1.Hour);
await Cache.Forget("greeting");
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
`Microsoft.Extensions.Caching.StackExchangeRedis` is the standard .NET API for this. Of `CacheOptions` only
`Json` applies there — the purge and the default expiry belong to the database-backed store.

> **Trim / AOT:** register your source-generated `JsonSerializerContext` once —
> `AddRaskCache<AppDbContext>(o => o.Json = AppJson.Default)` — and every call site stays as it is. The
> `IDistributedCache` (`byte[]`) surface is fully trim-safe.
