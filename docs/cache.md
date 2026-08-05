# Rask.Cache — a developer-facing cache on your database

> **In practice:** [Tutorial Ch 6](tutorial/06-cache.md) · recipe [cache an expensive query](recipes.md#cache-an-expensive-query) · [cheat sheet](cheatsheet.md).

`Rask.Cache` caches computed values in the app's own database — no message broker, no Redis. It implements
the standard **`IDistributedCache`** (so it drops straight into ASP.NET session state, output caching, and
anything else built on the abstraction) and adds a typed **`ICache`** convenience layer with a read-through
`GetOrCreateAsync<T>`. Entries carry **absolute** and **sliding** expirations; a background worker sweeps
expired rows.

```bash
dotnet add package Rask.Cache
```

## Why cache on the database

A cache is usually the first thing that pushes a solo app onto a second piece of infrastructure — a Redis
instance to run, secure, and back up. But most apps don't need a separate cache server: they need to avoid
recomputing an expensive value or re-calling a slow upstream on every request. SQLite, already on the box with
WAL enabled, is fast enough for that — and keeping the cache in the same database means one thing to operate,
one thing to back up.

`Rask.Cache` persists each entry to a table and a hosted worker purges expired rows, so there's nothing else
to run. Because it implements `IDistributedCache`, framework features that expect that abstraction work
unchanged.

## Use

```csharp
// Program.cs
builder.Services.AddRaskCache<AppDbContext>();
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite("Data Source=app.db"));
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    modelBuilder.AddRaskCache();   // maps the CacheEntry table
}
```

Add a migration for the new table before running — `rask db add AddCache && rask db update`
(or `dotnet ef migrations add AddCache` directly). Then cache from anywhere `ICache` is injected:

```csharp
// read-through: the factory runs once on a miss, then the value is served from the DB.
var rates = await cache.GetOrCreateAsync(
    $"rates:{date:yyyyMMdd}",
    ct => exchange.FetchRatesAsync(date, ct),
    new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) });

await cache.SetAsync("greeting", "hello", new DistributedCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
});

var greeting = await cache.GetAsync<string>("greeting");
await cache.RemoveAsync("greeting");
```

Or use the standard `IDistributedCache` directly (bytes in, bytes out) — for ASP.NET session state, register
it with `builder.Services.AddSession()` and `AddRaskCache` provides the store.

## How it works

- **`RaskDistributedCache<TContext>`** — the `IDistributedCache`. Reads and writes go through your
  `IDbContextFactory<TContext>` (a Rask Server session is long-lived, so each operation gets a fresh
  short-lived context). It stores one `CacheEntry` row per key: the bytes, an optional absolute deadline, an
  optional sliding window, and the effective **`ExpiresAt`**. A read past `ExpiresAt` is a **miss** and the row
  is evicted lazily; a read of a sliding entry **renews** `ExpiresAt` (capped by any absolute deadline).
- **`ICache` / `Cache`** — the typed layer. `GetOrCreateAsync<T>`, `GetAsync<T>`, `SetAsync<T>`, `RemoveAsync`,
  serializing `T` with `System.Text.Json`. `GetOrCreateAsync` runs the factory **once** on a miss, stores the
  result, and returns it; a concurrent second caller may also run the factory (the cache is not a lock), so
  keep factories idempotent.
- **`CachePurger<TContext>`** — a hosted `BackgroundService` that bulk-deletes rows past `ExpiresAt` on
  `PurgeInterval` (default 5 minutes). Reads already evict lazily; the sweep is the backstop for entries that
  are simply never read again.

## Trim / AOT

The typed `GetOrCreateAsync<T>` / `GetAsync<T>` / `SetAsync<T>` overloads use reflection-based
`System.Text.Json` and are annotated `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`. In a trimmed or
AOT app, use the `JsonTypeInfo<T>` overloads with a source-generated `JsonSerializerContext`:

```csharp
await cache.SetAsync("k", widget, AppJsonContext.Default.Widget);
var widget = await cache.GetAsync("k", AppJsonContext.Default.Widget);
```

The `IDistributedCache` (`byte[]`) surface is fully trim-safe.

## If you already run Redis

The argument above is about not standing Redis *up* for a cache. It is not an argument against using one you
already operate — and if you already have it, or you are running several app instances and would rather the
database didn't carry the cache traffic, `ICache` works over any `IDistributedCache`:

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379");
builder.Services.AddRaskCache();   // no <AppDbContext> — the store is Redis
```

That is the whole change. `ICache` and `GetOrCreateAsync` behave identically, because the typed layer only
ever talks to `IDistributedCache`; nothing about your calling code moves.

There is **no `Rask.Cache.Redis` package**, and there shouldn't be:
[`Microsoft.Extensions.Caching.StackExchangeRedis`](https://www.nuget.org/packages/Microsoft.Extensions.Caching.StackExchangeRedis)
is the standard .NET API for this and wrapping it would only add a layer to keep in step.

Note the overload takes **no `CacheOptions`**. Both of them — `PurgeInterval` and
`DefaultSlidingExpiration` — are implemented by the database-backed store, so against Redis they would be
settings that silently did nothing. Expiry is Redis's own business: configure it there, or pass a
`DistributedCacheEntryOptions` per call. Using the `<AppDbContext>` overload with a Redis store registered
would still work, but it also registers the purge worker and so keeps needing the `CacheEntry` table and a
migration for it — which is exactly what this overload removes.

## Notes

- **Server-side.** The store is your EF Core database and the purger is a hosted service — this is not a
  browser/WASM concern. (For client-side browser storage, see [`apis/storage.md`](apis/storage.md).)
- **No `ShutdownGracePeriod`, on purpose.** Jobs, the outbox and mail each take one, because they run *your*
  code and cancelling it halfway is destructive. The purger's only in-flight work is a single bulk delete of
  expired rows: cancel it and either the statement rolls back (the next sweep redoes it) or it committed (the
  work is done). There is no user code, no external side effect and no per-row state to lose, so the purge is
  abort-safe by construction and a knob would be pure surface area. See
  [Shutdown and redeploy](configuration.md#shutdown-and-redeploy).
- **SQLite is single-writer**, so writes serialize. Use [`UseRaskSqlite`](sqlite.md) (WAL + a `busy_timeout`)
  on your context so a concurrent write waits for the lock instead of failing with `SQLITE_BUSY`. Run **one
  purger per app**.
- **What to cache.** This is a value cache for expensive computations and slow upstream calls — not a hot
  per-request key/value store handling tens of thousands of writes a second. For that scale, reach for a
  dedicated cache; for the one-server app it's designed for, the database is enough.
