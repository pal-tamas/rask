# Rask.Data — base entity + EF Core interceptors

`Rask.Data` is a tiny, provider-agnostic data layer for **Entity Framework Core** applications. It gives
your entities a shared base with identity, audit stamps, and a domain-events buffer, and drives four
production concerns — auditing, soft delete, optimistic concurrency, and domain-event publication —
through EF Core interceptors and a one-line model convention, so no feature has to re-implement them.

It is the foundation the [`rask generate feature`](cli.md) scaffolder emits for its
`--soft-delete` / `--concurrency` / `--events` flags, packaged for direct use.

```bash
dotnet add package Rask.Data
```

## The base entity

```csharp
public sealed class Product : Entity<Guid>, ISoftDeletable, IVersioned
{
    private Product() { } // EF materialization

    public string Name { get; private set; } = "";
    public DateTime? DeletedAt { get; private set; }  // ISoftDeletable
    public int Version { get; private set; }          // IVersioned

    public static Product Create(string name)
    {
        var product = new Product { Id = Guid.NewGuid(), Name = name };
        product.Raise(new ProductCreated(product.Id)); // a Rask.Cqrs INotification
        return product;
    }

    public void Rename(string name)
    {
        Name = name;
        Raise(new ProductRenamed(Id));
    }
}
```

`Entity<TId>` always carries `Id`, `CreatedAt`, `UpdatedAt`, and the domain-events collection
(`Raise` / `DomainEvents` / `ClearDomainEvents`). The two behaviors are **opt-in** — implement a marker
interface to turn one on:

| Interface | Adds | Effect |
|-----------|------|--------|
| `ISoftDeletable` | `DateTime? DeletedAt` | `Remove` becomes a `DeletedAt` stamp; a global query filter hides it. |
| `IVersioned` | `int Version` | Marked as the optimistic-concurrency token; bumped on every update. |

## Wiring

```csharp
builder.Services.AddRaskCqrs();   // domain-event dispatch
builder.Services.AddRaskData();   // the interceptors + a TimeProvider

builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseSqlite("Data Source=app.db")
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    modelBuilder.ApplyRaskConventions(); // query filters + concurrency tokens
}
```

## What the interceptors do

- **`AuditingInterceptor`** — stamps `CreatedAt`/`UpdatedAt` (UTC, from an injectable `TimeProvider`) and
  increments each `IVersioned.Version` on update, so the stored token changes (SQLite has no rowversion).
- **`SoftDeleteInterceptor`** — rewrites a `Deleted` `ISoftDeletable` to `Modified` + sets `DeletedAt`.
  Your handler just calls `db.Remove(entity)`; to restore, load with `IgnoreQueryFilters()` and clear
  `DeletedAt`.
- **`DomainEventInterceptor`** — after the change commits, publishes each entity's `DomainEvents`
  through `IDispatcher.PublishAsync` (in a fresh scope) and clears them. Any
  `INotificationHandler<T>` registered by `AddRaskCqrs()` reacts automatically.

## Optimistic concurrency

`IVersioned` makes `Version` an EF Core concurrency token. When two edits race, the second `SaveChanges`
throws `DbUpdateConcurrencyException`. In a web form, round-trip the original `Version` (a hidden field)
and set it as the tracked original value before saving:

```csharp
var product = await db.Products.FirstAsync(x => x.Id == id);
db.Entry(product).Property(x => x.Version).OriginalValue = form.Version;
product.Rename(form.Name);
await db.SaveChangesAsync(); // throws if someone else changed it since `form.Version`
```

## Notes

- **Server-side.** These interceptors run against a real EF Core provider (SQLite by default in Rask);
  they are not used on the WASM client.
- **Trim/AOT-safe.** The model convention runs at startup (not the hot path) and uses no runtime handler
  reflection; domain-event dispatch goes through `Rask.Cqrs`' source-generated registry.
- **Durable delivery.** For at-least-once, crash-safe events, pair the entity with
  [`Rask.Outbox`](outbox.md), which persists events in the same transaction and drains them from a
  background worker (and disables the in-process dispatcher to avoid double delivery).
