# Data access (EF Core + SQLite)

An app's data code is its [domain](domain.md): aggregates read through their read face (`Product.Where(…)`) and
changed through their own operations (`Product.Reprice(id, version, price)`), with no `DbContext` to write and none
to inject. **Start there.** This page is what sits under it — how a context lives in a long-lived session, lazy
loading, SQLite's decimals — and the wiring for an app that brings a `DbContext` of its own (a schema another tool
made, a library's tables).

> WASM note: keep data access behind the server for most apps (a Server host, or an API the WASM app calls). EF
> Core *does* run in the browser, on a natively relinked build trimmed with EF Core's own assemblies rooted
> (Rask.Data and Rask.SQLite.EntityFrameworkCore are trim-safe and need nothing), and
> [`Rask.SQLite.Browser`](sqlite.md#rasksqlitebrowser--keeping-a-browser-database) keeps its database across
> reloads — [the recipe, and when it fits](sqlite.md#sqlite-in-the-browser-wasm).

## Register the DbContext with a factory, not a scope

A Rask Server session is **long-lived**: it lives for as long as the browser holds the WebSocket
open. A `DbContext`, by contrast, is meant to be short-lived and is not thread-safe. So a context is never
injected into a component — it would outlive every unit of work and be shared across overlapping renders. Rask
opens one per read and one per write, from a factory. An app with a context of its own registers that factory,
and Rask binds the read faces, the operations' entries and every battery to it:

```csharp
builder.Services.AddRaskData<CatalogDbContext>((sp, o) => o.UseRaskSqlite(sp));
```

`UseRaskSqlite` (the `Rask.SQLite.EntityFrameworkCore` package) applies the production pragma set — WAL,
`foreign_keys=ON`, a `busy_timeout`, `synchronous=NORMAL` — on every connection, and reads the connection string from
`Rask:ConnectionStrings:App`. Set `Rask:Sqlite:Retry:Enabled` for an opt-in fair-interval busy-retry on
`SaveChanges`. See [SQLite production pragmas](sqlite.md#transactions-begin-immediate--a-non-blocking-fair-interval-retry)
and [wiring, when Rask is not hosting](data.md#wiring-when-rask-is-not-hosting).

## Load in the lifecycle, render the result

Read data in an async lifecycle hook and store it in a field. The async-hook continuation
re-renders automatically when the `await` completes — no explicit `StateHasChanged()` needed (see
[lifecycle.md](lifecycle.md)). Use `OnMount` for a one-time load, `OnUpdated` to
reload when a route/query param changes. Each read opens and disposes its own context:

```csharp
public sealed partial class ListProductsPage : Component
{
    private IReadOnlyList<ProductRead> _products = [];
    private bool _loaded;

    protected override async Task OnMount()
    {
        _products = await Product.OrderBy(p => p.Name);
        _loaded = true;
    }

    // ... Render() shows a spinner until _loaded, then the rows.
}
```

For an event-handler change (e.g. a discontinue button), run the operation and reload — no
`StateHasChanged()` needed. An awaited handler re-renders on completion exactly like the async
lifecycle hook above, so the reloaded list paints automatically:

```csharp
private async Task Discontinue(ProductId id, int version)
{
    await Product.Discontinue(id, version);   // one aggregate, one transaction
    await Load();
}
```

You only call `StateHasChanged()` by hand for state that changes **outside** the handler-dispatch
window — a timer tick, a fire-and-forget continuation, or an external event/observable
subscription (e.g. `OnMount() => store.Changed += StateHasChanged`). See
[lifecycle.md](lifecycle.md) for the auto-re-render rule.

## Lazy loading is off

Every context Rask configures has EF Core's lazy loading switched off (`ChangeTracker.LazyLoadingEnabled = false`), the
proxies (`UseLazyLoadingProxies`) and the injected `ILazyLoader` alike. A navigation nobody loaded reads as EF Core
leaves it — null, or an empty collection — and runs no query. Lazy loading hides its cost: a navigation read in a loop
is a query per row (N+1), and a component's render, a form's validation or a JSON serializer reads navigations without
anybody meaning to load them. Load what a read needs instead: `Include`, a projection, a read face.

The way back is explicit, and discouraged: `.AllowLazyLoading()` on that context's options.

```csharp
services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskSqlite(sp)
    .UseLazyLoadingProxies()
    .AllowLazyLoading()                                  // without it: RASK126, and nothing loads
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

`UseLazyLoadingProxies` without it is [RASK126](diagnostics.md#rask126). A context that does allow it still has a second
line of defence in forms: validation never reads a navigation EF Core has not loaded, so a validation pass cannot set
lazy loads off either ([forms-validation.md](forms-validation.md)).

## How to organise it

Use **vertical slices**: code is grouped by use case (`ProductsPage`, `NewProductPage`,
`EditProductPage`), each slice owning its page and the forms over the operations it calls — no
shared repository and no service layer. The only shared things are the app shell, a form-error template,
and the domain: the aggregates, their value objects and their typed ids ([domain modelling](domain.md)).
A mapping rule the conventions do not cover — an index, a relationship — is the aggregate's own static
`Configure` ([mapping rules](data.md#mapping-rules-the-conventions-dont-cover)).

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
Measured by [`SqliteDecimalOrderingBenchmarks`](../tests/Rask.Benchmarks/SqliteDecimalOrderingBenchmarks.cs)
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

It is also the conventional way to represent money. Reach for it when the shape of
the data calls for it — not, any longer, to dodge a correctness problem.

## Schema & seeding

The schema is migrations: `rask db add`, applied as the app starts
([migrations apply themselves on start](data.md#migrations-apply-themselves-on-start)). Seed rows through the
aggregate's own factory, so even seed data honours its rules.

## Testing

Tests run on SQLite in memory, a database per test, through the app's own context —
`TestDatabase.Start<CatalogDbContext>()` — with nothing mocked ([testing the domain](testing.md#testing-the-domain)).

## Run it

Scaffold an app with `rask new Shop`, follow [chapter 2](tutorial/02-first-feature.md), and
`dotnet run` it.
