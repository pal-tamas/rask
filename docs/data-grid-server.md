# Data grid — server-side data & URL state

Push paging and sorting into the database, show a loading state, and drive the grid's state from the URL.

‹ Back to [Data grid](data-grid.md)

## Server-side paging & sorting

`Data` alone hands the grid the whole list and it sorts and pages in memory — fine for hundreds of rows, wrong
for hundreds of thousands. There are two ways to let the database do the work.

### The short way: hand it the query

Pass an `IQueryable<T>` as `Data` — an EF `DbSet` or any query built from one. The grid orders it by the sorted
column's `SortBy` expression, counts it, and materialises only the current page, so `ORDER BY`/`COUNT`/`LIMIT`
all happen in the database and the whole table is never loaded.

```csharp
BsDataGrid
    // Order it: Skip/Take over an unordered SQL query is undefined (rows can repeat or vanish between
    // pages, and EF warns). Sorting a column replaces this ordering; it is the fallback.
    .Data(db.Products.AsNoTracking().Where(p => p.Active).OrderBy(p => p.Id))
    .PageSize(20)
    .Columns(
    [
        // SortBy has to be an Expression: only an expression tree becomes ORDER BY. A Func would pull the
        // whole table into memory to sort it there — exactly what Query exists to avoid.
        new BsColumn<Product> { Title = "Product", Value = p => p.Name, Sortable = true, SortBy = p => p.Name },
        new BsColumn<Product> { Title = "Price", Value = p => p.Price, Sortable = true, SortBy = p => p.Price },
    ])
```

Know what you're buying:

- **Give it a stable order.** Paging a SQL query with no `ORDER BY` is undefined. The grid replaces your
  ordering when the user sorts, so yours is the default it falls back to.
- **The `DbContext` must outlive the render.** The query re-runs during render on every sort/page click, so a
  context from `await using var db = ...` would be disposed by then. It needs one that lives as long as the
  component — a session-pinned context, the thing short-lived-`DbContext` guidance warns about. The async way
  below has no such constraint.
- **Server hosts only.** The query runs in-process, so it needs the database in-process. A WASM app has no
  database and throws on first render, with the fix in the message. (A native host with a local SQLite
  `DbContext` is fine.)
- **It blocks.** `Count()` and `ToList()` run inside the synchronous render, so a sort or page click holds a
  request thread for two round-trips. That's fine for an admin list screen. If it isn't fine for you, use the
  async way below — it's the same feature with the `await` in your hands.
- Results are cached per (query, sort, page), so an unrelated re-render — expanding a detail row, a parent
  updating — doesn't re-run the query.
- A `Sortable` column with no `SortBy` can't be ordered in the store, so its header stays plain rather than
  offering a control that would do nothing.

### The async way: a fetched page + `TotalCount`

Pass **one already-fetched page** as `Data` and tell the grid how many rows are really behind it with
`TotalCount`. The grid renders
that slice verbatim — no re-sorting, no re-slicing — and sizes the pager from the total. Nothing blocks,
because you own the `await`:

```csharp
private IReadOnlyList<Product> _rows = [];
private int _total;
private string? _sort;
private bool _desc;

// The grid awaits this handler, so the query runs before the re-render paints.
private async Task LoadAsync(DataGridSort sort)
{
    (_sort, _desc) = (sort.Field, sort.Descending);

    var q = db.Products.AsNoTracking();
    q = _sort switch
    {
        "name"  => _desc ? q.OrderByDescending(p => p.Name)  : q.OrderBy(p => p.Name),
        "price" => _desc ? q.OrderByDescending(p => p.Price) : q.OrderBy(p => p.Price),
        _       => q.OrderBy(p => p.Id),   // a stable order — paging without one is undefined
    };

    _total = await q.CountAsync(ct);
    _rows  = await q.Skip(_page * 20).Take(20).ToListAsync(ct);
}

BsDataGrid(
    Data: _rows,
    TotalCount: _total,          // the whole set, not the page — this is what makes the pager right
    PageSize: 20,
    Sort: _sort, SortDescending: _desc, OnSortChangeAsync: LoadAsync,
    Page: _page, OnPageChangeAsync: async p => { _page = p; await LoadAsync(new(_sort, _desc)); },
    Columns: [ /* each Sortable column needs a SortField */ ])
```

`OnSortChangeAsync`/`OnPageChangeAsync` are how the grid supports async data **without any async machinery of
its own**: the click awaits your handler, you run the query in it, and the re-render shows the result. Footers
see only the slice you passed, so compute whole-set totals in your query.

Combine it with a controlled `Page`/`Sort` (next section) and the state can live in the URL too.

## Loading state

Server-side paging means a click awaits a round-trip. `Loading` is how the grid says so: set it around your
fetch and it dims the table behind a spinner, marks it `aria-busy`, and ignores further sort/page clicks until
it clears.

```csharp
private async Task ReloadAsync()
{
    _loading = true;
    (_rows, _total) = await FetchAsync(_page, _sort);
    _loading = false;
}
```

**No `StateHasChanged` anywhere** — that is the point. Rask renders your component twice for free here: once
when the `await` actually yields, which is what paints the spinner, and once when the handler returns, which
clears it.

<!-- demo:data-grid-loading -->

`Loading` is deliberately **`bool?`**, and the three states differ:

| Value | Meaning |
|---|---|
| `null` (default) | The grid isn't using the feature. Markup is exactly as it always was. |
| `false` | In use, idle. |
| `true` | Fetching. |

That distinction is not fussiness. Once the feature is in use the grid renders a `position-relative` wrapper,
and it renders it in **both** states so it never appears or disappears under the table — which keeps the
table's DOM identity, and with it **focus and scroll position**, across a refetch. A grid that never sets
`Loading` gets no wrapper at all.

Two details worth knowing:

- **The empty state is suppressed while loading.** A fetch in flight is not "no results", so the first load
  doesn't flash the placeholder before the rows land.
- **Controls get `aria-disabled`, not `disabled`.** A real `disabled` attribute drops focus to `<body>` — it
  would throw away the user's keyboard position on every page click. They stay focusable and announce as
  disabled, and the handlers guard for real.

`Loading` does nothing for an `IQueryable` `Data`: that query runs synchronously inside the render, so there
is no moment at which the grid is both mounted and waiting.

## Putting the state in the URL

By default the grid owns its page and sort. Set `Page` (with `OnPageChange`) and/or `Sort`+`SortDescending`
(with `OnSortChange`) and it stops owning them: it renders what you pass and **reports what the user clicked**
instead of moving itself. Put those values in `[QueryParam]` properties and the grid's state becomes shareable,
bookmarkable, and replayed by browser back/forward for free.

```csharp
[QueryParam("sort")] public string? SortKey { get; set; }
[QueryParam("dir")]  public string? Dir { get; set; }
[QueryParam]         public int? Page { get; set; }

BsDataGrid(
    Data: filtered,
    PageSize: 10,
    Page: Math.Max(0, (Page ?? 1) - 1),                     // 0-based here, 1-based in the URL
    OnPageChange: p => nav.SetQuery("page", (p + 1).ToString()),
    Sort: SortKey,
    SortDescending: Dir == "desc",
    OnSortChange: s => nav.SetQuery(("sort", s.Field), ("dir", s.Descending ? "desc" : "asc"), ("page", "1")),
    Columns: [ /* each Sortable column needs a SortField — that's what OnSortChange reports */ ])
```

The two are independent: control the sort and let the grid own the page, or the reverse. And because
`OnSortChange` only tells you *which column was clicked*, the policy stays yours — the worked example cycles
asc → desc → unsorted, which the grid's own two-state toggle doesn't do.

**Filtering** is deliberately not a grid feature: it's a query concern. Filter the list you pass as `Data` (or
in the query behind it), and reset to page 1 when the filter changes.

The complete worked example — filter, sort, page and page size all in the URL — is the data table at
`/table`; its source is on the page, verbatim. See also [routing.md](routing.md#route-and-query-parameters).

## When not to use it

With a list as `Data`, the grid sorts and pages **in memory**. For large sets pass an `IQueryable`, or a
fetched page + `TotalCount` (above), so the database does the work.

For very long *unpaged* lists, `VirtualizeModel<T>` ([composition](composition-lists.md#virtualize--windowed-lists))
renders only the visible window instead.
