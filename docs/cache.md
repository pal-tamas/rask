# Rask.Cache — a developer-facing cache on your database

> **In practice:** [Tutorial Ch 6](tutorial/06-cache.md) · recipe [cache an expensive query](recipes.md#cache-an-expensive-query) · [cheat sheet](cheatsheet.md).

`Rask.Cache` caches computed values in the app's own database — no message broker, no Redis. It implements
the standard **`IDistributedCache`** (so it drops straight into ASP.NET session state, output caching, and
anything else built on the abstraction) and adds a typed cache you reach the way you say it —
**remember the rates for ten minutes**:

```csharp
var rates = await Cache.Remember("rates", LoadRates).For(10.Minutes);
```

Entries carry **absolute** and **sliding** expirations; a background worker sweeps expired rows.

> Included in the [`Rask`](../README.md) package — nothing to install. It is **on**; an app that does without it says so:
>
> ```csharp
> app.Configure(c => c.Cache.Off());
> ```

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
(or `dotnet ef migrations add AddCache` directly). Then cache from anywhere — a handler, a render, a
request, a job — with nothing injected:

```csharp
// remember: a hit returns what was stored; a miss runs the loader, stores it, and returns it.
var rates = await Cache.Remember($"rates:{date:yyyyMMdd}", () => exchange.FetchRates(date)).Sliding(10.Minutes);

await Cache.Set("greeting", "hello").For(1.Hour);
var greeting = await Cache.Get<string>("greeting");
await Cache.Forget("greeting");
```

How long an entry lives is a step at the end: **`.For(10.Minutes)`** from now, **`.Sliding(20.Minutes)`**
while it keeps being read, **`.Until(midnight)`** at a fixed moment. With no step it follows
`CacheOptions.DefaultSlidingExpiration`, and never expires when that is unset. Nothing runs until the line
is awaited.

No call takes a cancellation token: each is cancelled with the work it runs in. Where there is no such work
— a hosted service, a timer started at boot — inject `ICache`, which reads the same:

```csharp
public sealed class RatesWarmer(ICache cache) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) =>
        await cache.Remember("rates", LoadRates, stoppingToken).For(10.Minutes);
}
```

Or use the standard `IDistributedCache` directly (bytes in, bytes out) — for ASP.NET session state, register
it with `builder.Services.AddSession()` and `AddRaskCache` provides the store.

## How it works

- **`RaskDistributedCache<TContext>`** — the `IDistributedCache`. Reads and writes go through your
  `IDbContextFactory<TContext>` (a Rask Server session is long-lived, so each operation gets a fresh
  short-lived context). It stores one `CacheEntry` row per key: the bytes, an optional absolute deadline, an
  optional sliding window, and the effective **`ExpiresAt`**. A read past `ExpiresAt` is a **miss** and the row
  is evicted lazily; a read of a sliding entry **renews** `ExpiresAt` (capped by any absolute deadline).
- **`Cache` / `ICache`** — the typed layer: `Remember`, `Set`, `Get`, `Forget`, serializing `T` with
  `System.Text.Json`. `Remember` runs the loader **once** on a miss, stores the result, and returns it; a
  concurrent second caller may also run the loader (the cache is not a lock), so keep loaders idempotent.
- **`CachePurger<TContext>`** — a hosted `BackgroundService` that bulk-deletes rows past `ExpiresAt` on
  `PurgeInterval` (default 5 minutes). Reads already evict lazily; the sweep is the backstop for entries that
  are simply never read again.

## Trim / AOT

Values are stored as JSON. An untrimmed app serializes them with reflection and needs nothing. A trimmed or
AOT app registers its source-generated `JsonSerializerContext` **once**, and every call site stays exactly as
it is:

```csharp
builder.Services.AddRaskCache<AppDbContext>(o => o.Json = AppJson.Default);

[JsonSerializable(typeof(List<Rate>))]
internal sealed partial class AppJson : JsonSerializerContext;
```

A type the context does not list fails on first use with the attribute to add —
`[JsonSerializable(typeof(Rate))]` — rather than with a trim warning at every call site.

The `IDistributedCache` (`byte[]`) surface is fully trim-safe.

## If you already run Redis

The argument above is about not standing Redis *up* for a cache. It is not an argument against using one you
already operate — and if you already have it, or you are running several app instances and would rather the
database didn't carry the cache traffic, `ICache` works over any `IDistributedCache`:

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379");
builder.Services.AddRaskCache();   // no <AppDbContext> — the store is Redis
```

That is the whole change. `Cache.Remember` and the rest behave identically, because the typed layer only
ever talks to `IDistributedCache`; nothing about your calling code moves.

There is **no `Rask.Cache.Redis` package**, and there shouldn't be:
[`Microsoft.Extensions.Caching.StackExchangeRedis`](https://www.nuget.org/packages/Microsoft.Extensions.Caching.StackExchangeRedis)
is the standard .NET API for this and wrapping it would only add a layer to keep in step.

Of `CacheOptions`, only `Json` applies to this overload. The other two — `PurgeInterval` and
`DefaultSlidingExpiration` — are implemented by the database-backed store, which it does not register. Expiry
is Redis's own business: configure it there, or say it per call with `.For(…)` and `.Sliding(…)`. Using the `<AppDbContext>` overload with a Redis store registered
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
