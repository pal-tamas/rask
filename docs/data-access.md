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
> and use the raw-path `InImmediateTransactionAsync` for a genuinely non-blocking `BEGIN IMMEDIATE`
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
public sealed partial class ListProductsPage(IDbContextFactory<CatalogDbContext> dbContextFactory) : Component
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

## Does SQLite support `decimal`?

SQLite has no native decimal type, but a `decimal` works correctly through Rask — including the part
that is broken upstream.

EF Core stores a `decimal` as **TEXT** in a culture-invariant fixed-point format (`19.95`, never
`19,95`, on every locale), so the value round-trips exactly. It never uses `REAL`, which would round —
`0.1 + 0.2 ≠ 0.3`. Arithmetic, comparisons and `Sum`/`Average`/`Min`/`Max` all translate to SQL, via
managed helper functions EF registers on the connection (`ef_add`, `ef_compare`, `ef_sum`, …). Sorting
text numerically needs a *collating sequence*, so EF emits `ORDER BY "Price" COLLATE EF_DECIMAL`.

**The upstream bug Rask fixes.** EF Core registers that collation as
`decimal.Compare(decimal.Parse(x), decimal.Parse(y))` — with no `IFormatProvider`, so it parses the
invariant text using the machine's `CurrentCulture`. The consequences depend on the server's locale:

| Locale | `ORDER BY` on a `decimal` |
| --- | --- |
| `.` is the decimal separator (`en-US`, invariant) | correct |
| `.` is the **group** separator (`de-DE`, `fr-FR`) | **silently mis-sorted** — `"19.95"` reads as `1995` |
| `.` is neither (`en-HU`, `hu-HU`) | **the process dies** — `FormatException` |

That last row is not a caught query error. The throw happens inside a native SQLite comparison
callback, which a managed exception cannot be unwound across, so it terminates the process. The same
crash occurs on any locale if a non-numeric value ever reaches the column — and SQLite's [dynamic
typing](sqlite.md) permits exactly that.

`UseRaskSqlite` re-registers `EF_DECIMAL` on every connection open with an invariant, total,
non-throwing comparison, which is what EF's own generated SQL then uses. Nothing in the database file
changes — no column type, no collation in the DDL, no migration — so ordering, `GROUP BY` and
`DISTINCT` on a `decimal` are correct on every locale, and unparseable text sorts after the numbers
instead of killing the app. You write `public decimal Price { get; set; }` and nothing else.

**What the collation costs, and how to avoid paying it.** Correct is not free. Every comparison in the
sort is a managed callback that marshals two strings out of SQLite, and a sort makes O(n log n) of them.
Measured by [`SqliteDecimalOrderingBenchmarks`](../benchmarks/Rask.Benchmarks/SqliteDecimalOrderingBenchmarks.cs)
over one WAL database, ordering every row of a table:

| Rows | `decimal` (TEXT, collated) | integer minor units (`INTEGER`, indexed) |
| ---: | ---: | ---: |
| 1,000 | 2.0 ms · 699 KB allocated | 0.24 ms · 768 B |
| 10,000 | 28.8 ms · 9.9 MB | 0.52 ms · 768 B |
| 100,000 | 156 ms · 125 MB | 4.5 ms · 768 B |

The time is ~35× at 100k rows; the allocation is the part that will find you first, since it is garbage
the collection has to walk.

**Index the collation and the cost disappears.** SQLite can serve an `ORDER BY` from an index only when
the index sorts by the *same* collating sequence — otherwise the planner reports
`USE TEMP B-TREE FOR ORDER BY` and does the sort anyway. Declaring the collation on the property puts
`COLLATE EF_DECIMAL` in the column definition, and an index over that column inherits it, so the
ordering is answered by an index scan with no comparisons at query time:

```csharp
entity.Property(p => p.Price).UseCollation("EF_DECIMAL");
entity.HasIndex(p => p.Price);
```

One caveat, and it is the reason this is not the default: a column or index carrying `COLLATE
EF_DECIMAL` in its DDL can only be read by a connection that has registered that collation. Rask's do;
the `sqlite3` CLI does not, and will answer `no such collation sequence: EF_DECIMAL` for any query that
uses it — and once an *index* carries it, writes from such a connection fail too. Plain `SELECT`s,
`.schema`, `.dump` and Litestream are all unaffected, since they never invoke it.

**Or prefer integer minor units.** For money on a large, frequently sorted table, model the amount as an
**integer minor unit** count and map it to an `INTEGER` column, which sorts and aggregates natively, is
indexable by any tool, and keeps the file portable:

```csharp
entity.Property(p => p.Price)
    .HasConversion(money => money.Cents, cents => Money.FromCents(cents));
```

The sample does this, and its integration tests assert the column is `INTEGER` and that values
round-trip exactly. It is also the conventional way to represent money. Reach for it when the shape of
the data calls for it — not, any longer, to dodge a correctness problem.

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
