# Rask.Data

A tiny, provider-agnostic data layer for **Entity Framework Core** apps — the DDD building blocks the
[Rask tutorial](https://github.com/pal-tamas/rask/blob/main/docs/tutorial/02-first-feature.md) builds its CRUD slices on, packaged for reuse.

- **`Entity<TId>`** — a base entity with `Id`, audit stamps (`CreatedAt`/`UpdatedAt`), and a
  domain-events buffer.
- **Opt-in markers** — implement `ISoftDeletable` (adds `DeletedAt`) or `IVersioned` (adds a `Version`
  concurrency token) on your entity to turn on the behavior.
- **Three `ISaveChangesInterceptor`s** — auditing timestamps, **transparent soft delete** (a `Remove`
  becomes a `DeletedAt` stamp behind a global query filter), and **after-commit domain-event publication**
  through [Rask.Cqrs](https://www.nuget.org/packages/Rask.Cqrs).

## Use

```csharp
public sealed class Product : Entity<Guid>, ISoftDeletable, IVersioned
{
    public string Name { get; private set; } = "";
    public DateTime? DeletedAt { get; private set; }
    public int Version { get; private set; }

    public static Product Create(string name) => new() { Id = Guid.NewGuid(), Name = name };
}

// Program.cs
builder.Services.AddRaskCqrs();
builder.Services.AddRaskData();
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseSqlite("Data Source=app.db")
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

// AppDbContext.OnModelCreating
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    modelBuilder.ApplyRaskConventions();
}
```

`db.Remove(product)` now soft-deletes; deleted rows drop out of queries (use `IgnoreQueryFilters()` to
restore); a save against a stale `Version` throws `DbUpdateConcurrencyException`; and any
`INotification` raised on the entity is published after the change commits.

Part of the [Rask](https://github.com/pal-tamas/rask) framework. MIT licensed.
