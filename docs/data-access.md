# Data access (EF Core + SQLite)

Rask has no data layer of its own — you use whatever .NET gives you. This guide shows the
idiomatic way to wire **EF Core + SQLite** into a Rask **Server** app: register the context, load
data in the component lifecycle, and run forms against persisted state. The runnable reference is
[`samples/Rask.Example.EfCore`](../samples/Rask.Example.EfCore).

> WASM note: this is a Server-side pattern. EF Core's SQLite provider isn't a fit for the trimmed
> browser runtime — keep data access behind the server (a Server host, or an API the WASM app calls).
> (The [playground tutorial](playground.md#the-guided-tutorial) does run EF Core in the browser, on a
> deliberately untrimmed, natively relinked build, so you can *learn* this without installing anything.
> It's a sandbox — [here's why it isn't an architecture](sqlite.md#sqlite-in-the-browser-wasm).)

## Register the DbContext with a factory, not a scope

A Rask Server session is **long-lived**: it lives for as long as the browser holds the WebSocket
open. A `DbContext`, by contrast, is meant to be short-lived and is not thread-safe. So do **not**
register a scoped `DbContext` and inject it into a component — it would outlive every unit of work
and be shared across overlapping renders. Register an `IDbContextFactory<T>` instead and open a
fresh context per operation:

```csharp
builder.Services.AddDbContextFactory<CatalogDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));
```

> Production tip: swap `UseSqlite` for `UseRaskSqlite` (the standalone `Rask.SQLite` package) to apply
> the production pragma set — WAL, `foreign_keys=ON`, a `busy_timeout`, `synchronous=NORMAL`
> — on every connection. Pass `configureRetry:` for an opt-in fair-interval busy-retry on `SaveChanges`,
> and use the raw-path `ExecuteInImmediateTransactionAsync` for a genuinely non-blocking `BEGIN IMMEDIATE`
> write. See [SQLite production pragmas](sqlite.md#transactions-begin-immediate--a-non-blocking-fair-interval-retry).

```csharp
await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);
var products = await db.Products.AsNoTracking().OrderBy(p => p.Id).ToListAsync(CancellationToken);
```

`AddDbContextFactory` registers the factory as a singleton; each `CreateDbContextAsync` hands back a
brand-new context you dispose at the end of the unit of work. Always pass `Component.CancellationToken`
to the async EF calls so navigating away mid-query cancels cleanly.

## Load in the lifecycle, render the result

Read data in an async lifecycle hook and store it in a field. The async-hook continuation
re-renders automatically when the `await` completes — no explicit `StateHasChanged()` needed (see
[lifecycle.md](lifecycle.md)). Use `OnMountAsync` for a one-time load, `OnPropsChangedAsync` to
reload when a route/query param changes.

```csharp
public sealed class ListProductsPage(IDbContextFactory<CatalogDbContext> dbContextFactory) : Component
{
    private IReadOnlyList<Product> _products = [];
    private bool _loaded;

    protected override async Task OnMountAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);
        _products = await db.Products.AsNoTracking().OrderBy(p => p.Id).ToListAsync(CancellationToken);
        _loaded = true;
    }

    // ... Render() shows a spinner until _loaded, then the rows.
}
```

For an event-handler mutation (e.g. a delete button), just do the work and reload — no
`StateHasChanged()` needed. An awaited handler re-renders on completion exactly like the async
lifecycle hook above, so the reloaded list paints automatically:

```csharp
private async Task DeleteAsync(int id)
{
    await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);
    await db.Products.Where(p => p.Id == id).ExecuteDeleteAsync(CancellationToken);
    await LoadAsync();
}
```

You only call `StateHasChanged()` by hand for state that changes **outside** the handler-dispatch
window — a timer tick, a fire-and-forget continuation, or an external event/observable
subscription (e.g. `OnMount() => store.Changed += StateHasChanged`). See
[lifecycle.md](lifecycle.md) for the auto-re-render rule.

## How the sample is organised

The sample uses **vertical slices**: code is grouped by use case (`ListProducts`, `CreateProduct`,
`EditProduct`), each slice owning its page, its own form model, and its own EF Core access — no
shared repository or service layer. The only shared things are the app shell, a form-error template,
and the Catalog domain.

The domain applies **DDD tactical patterns**: `Product` is an aggregate root with private setters
that only changes through `Create` / `Update`, and `Money` / `ProductName` / `StockLevel` are value
objects. Each value object **owns its validation rule** as a static `Validate` method shaped exactly
like Rask's inline validator (`Func<T, IEnumerable<string>>`), so the form and the domain enforce the
same rule from one source:

```csharp
// In the value object:
public static IEnumerable<string> Validate(decimal amount) { /* the one rule */ }

// In the form (reused as a method group — see forms.md §3 for inline validation):
Input.Bind(() => _form.Price).Validate(Money.Validate).Id("p-price").Class("form-control")
```

The EF Core mapping lives in an `IEntityTypeConfiguration<Product>` (applied with
`ApplyConfigurationsFromAssembly`), keeping the domain free of persistence attributes. Value objects
map onto columns with value converters.

## Does SQLite support `decimal`? (the money gotcha)

No — SQLite has no native decimal type. EF Core maps a `decimal` to a **TEXT** column and falls back
to **REAL** for `ORDER BY` and aggregates, which is lossy and sorts lexicographically. The sample
sidesteps this by modelling `Money` as **integer minor units (cents)** and mapping it to an `INTEGER`
column with a converter:

```csharp
entity.Property(p => p.Price)
    .HasConversion(money => money.Cents, cents => Money.FromCents(cents));
```

Integer cents are exact, correctly sortable, and the conventional way to represent money anyway. The
sample's integration tests assert the column is `INTEGER` and that values round-trip exactly.

## Schema & seeding

The sample calls `EnsureCreatedAsync()` on startup and seeds a few rows through the aggregate's
`Create` factory (so even seed data honours the invariants). `EnsureCreated` is the simplest correct
choice for a sample with no schema history — a real app with an evolving schema would use
`Database.MigrateAsync()` with EF Core migrations instead.

## Testing

Data-access logic is testable without a browser:

- **Unit-test** the value objects and aggregate (validation rules, invariants) directly.
- **Integration-test** the EF mapping against a real SQLite file: create the context, save a
  `Product`, reload it in a new context, and assert the round-trip (and the storage shape).

See `tests/Rask.Example.EfCore.Tests`. The end-to-end CRUD flow over the live connection is covered
by a Playwright smoke test in `tests/Rask.Examples.E2E.Tests/EfCoreCrudTests.cs`.

## Run it

```bash
dotnet run --project samples/Rask.Example.EfCore
# then open the printed URL at /products
```
