# Data grid

`BsDataGrid<T>` from [`Rask.Bootstrap`](bootstrap.md) is the list-screen upgrade from [`BsTable`](bootstrap-cards.md):
typed columns bound straight to your row type, click-to-sort headers, client-side paging, per-column footer
totals, custom cells, an empty state, and expandable master-detail rows. It is **controlled by component state**,
so all of it works with **zero JavaScript** — no `bootstrap.js`, no grid plugin.

```csharp
BsDataGrid(
    Data: products,
    PageSize: 10,
    RowKey: p => p.Id,
    Columns:
    [
        new BsColumn<Product> { Title = "Product", Value = p => p.Name, Sortable = true },
        new BsColumn<Product> { Title = "Price", Value = p => p.Price.ToString("C"), Class = Txt.End() },
    ])
```

- [Columns](#columns) · [Sorting](#sorting) · [Paging](#paging) · [Footer totals](#footer-totals)
- [Empty state](#empty-state) · [Master-detail](#master-detail) · [Density & styling](#density--styling)
- [Accessibility](#accessibility) · [Server-side paging & sorting](#server-side-paging--sorting)
- [Putting the state in the URL](#putting-the-state-in-the-url)
- [When not to use it](#when-not-to-use-it) · [Reference](#reference)

## Live example

Sortable headers, a pager, a `Template` cell and a footer total — click a header, then page through:

<!-- demo:data-grid -->

## Columns

A `BsColumn<T>` binds to `T` directly; there is no view-model in between.

| Property | Purpose |
|---|---|
| `Title` | The header text. |
| `Value` | `Func<T, object?>` — the cell text, `ToString()`'d and HTML-encoded. |
| `Template` | `Func<T, Component>` — a custom cell. Overrides `Value`. |
| `Sortable` | Makes the header a sort toggle. |
| `SortKey` | `Func<T, IComparable?>` — what to order by. Falls back to `Value` when it is `IComparable`. |
| `Class` | Applied to the header, the cells **and** the footer cell (e.g. `Txt.End()` for numbers). |
| `Footer` | `Func<IReadOnlyList<T>, object?>` — a summary cell, computed over every row. |
| `FooterTemplate` | Same, but renders a component. Overrides `Footer`. |

`Value` renders text; `Template` renders anything:

```csharp
new BsColumn<Product>
{
    Title = "Stock",
    Class = Txt.End(),
    Sortable = true,
    SortKey = p => p.Stock,                       // sort by the number...
    Template = p => p.Stock == 0                  // ...but render a badge
        ? BsBadge(Color: BsColor.Danger)["Out of stock"]
        : BsBadge(Color: BsColor.Success)[p.Stock.ToString()],
}
```

Cell values always go through Rask's encoding text path, so a value containing `<script>` renders as text, not
markup. Use `Template` when you genuinely want components.

## Sorting

Set `Sortable = true` and the header becomes a button: click to sort ascending, click again for descending.
Only one column sorts at a time, and sorting always returns to the first page — staying on page 7 of a
re-ordered list would show rows you never asked for.

Ordering uses `SortKey` when set, otherwise `Value` if it happens to be `IComparable`. Reach for `SortKey`
whenever the displayed text would sort wrongly — formatted dates and numbers are the usual traps:

```csharp
// "2026.01.09" vs "2026.1.9": sort the DateTime, display the string.
new BsColumn<Tx> { Title = "When", Sortable = true, SortKey = t => t.When,
                   Value = t => t.When.ToString("yyyy.MM.dd HH:mm") }
```

## Paging

`PageSize: 0` (the default) shows every row and renders no pager. Any positive value adds a compact pager with
a `from-to / total` summary. The page window slides to stay small even for hundreds of pages.

Paging is **client-side**: the grid receives the whole list and slices it. See
[when not to use it](#when-not-to-use-it) for large result sets.

## Footer totals

`Footer` (text) or `FooterTemplate` (a component) adds a `<tfoot>` cell. Totals are computed over the **whole
result set, not the visible page**, so they do not change as you page:

```csharp
new BsColumn<Product>
{
    Title = "Price", Class = Txt.End(), Value = p => p.Price.ToString("C"),
    Footer = rows => rows.Sum(p => p.Price).ToString("C"),
}
```

The `<tfoot>` appears only if at least one column defines a footer.

## Empty state

`Empty` replaces the entire grid — headers, pager and all — when there are no rows, so a filtered-to-nothing
list reads as a message rather than an empty table. The grid keeps its sort and page across the round-trip.

<!-- demo:data-grid-empty -->

## Master-detail

`ExpandedContent` gives every row an expander toggle and, when open, a full-width detail row built by your
callback. Return `null` to give a particular row no detail.

**Set `RowKey`.** It ties expansion to the row's identity rather than its position, so an open row stays open
across a sort or a page change. Without it, rows are keyed by index and expansion follows the wrong row.

<!-- demo:data-grid-detail -->

## Row styling & row clicks

`RowClass` computes extra classes for a row from the row itself — the hook for the overdue invoice, the
cancelled order:

```csharp
BsDataGrid(Data: invoices, RowKey: i => i.Number,
    RowClass: i => i.DaysOverdue switch { 0 => null, < 30 => "table-warning", _ => "table-danger" },
    Columns: [...])
```

`OnRowClick` (and its awaited twin `OnRowClickAsync`) raises the row the user clicked:

```csharp
BsDataGrid(Data: invoices, RowKey: i => i.Number, OnRowClick: i => Open(i.Number), Columns: [...])
```

<!-- demo:data-grid-row -->

### Which cells the click reaches — and why it matters

The handler goes on the **cells** of the columns whose `RowClickable` resolves true, never on the `<tr>`. By
default that means **`Value` columns are clickable and `Template` columns are not**, and that asymmetry is a
safety rule rather than a style choice.

Rask's client cancels the default action of every click it dispatches. So under a click handler, a checkbox
never fires `change`, an `<a href>` never navigates, and a bare `<button>` (which defaults to `type=submit`)
swallows the click instead. **Every one of those failures is silent.** A `Value` cell is plain encoded text and
can never contain any of them, so it is always safe; a `Template` cell is exactly where you put a link or a
button, so it opts out by default.

Override it per column when you know better:

```csharp
// A non-interactive template (a badge, an icon) — safe to make clickable.
new BsColumn<Invoice> { Title = "Status", Template = StatusBadge, RowClickable = true }

// A Value column carved out of the row click.
new BsColumn<Invoice> { Title = "Ref", Value = i => i.Ref, RowClickable = false }
```

The leading expander and selection cells never get the handler, so their controls always work.

### A clickable row is not an accessible control

**Never make the row click the only way to reach an action.** A `<tr>` cannot be made keyboard-operable
without either a fake `role="button"` — which destroys the row semantics a screen reader depends on — or a
tabindex on every row, which buries the real controls. So the grid deliberately does neither.

Put a real link or button in a column (a `Template` column, so it keeps its own click) and let the row click
duplicate it, exactly as the demo above does. Pointer users get the shortcut; everyone else gets the button.

### What it costs

`OnRowClick` registers a handler per *clickable cell*, so it scales with rows × columns rather than rows. On a
100-row × 5-column **unpaged** grid that is 500 handlers — measured at **+45% allocation and roughly double the
render time** versus the same grid without it (`BsDataGridBenchmarks`).

That is the price of the per-cell design, and it is why the row's callback closure is built once per row and
shared across its cells rather than minted per cell. Two ways to keep it small, both worth taking:

- **Page the grid.** `PageSize: 20` renders a fifth of the rows and a fifth of the handlers — and is ~0.28× the
  allocation of the unpaged grid before you even add the row click.
- **Set `RowClickable = false`** on columns that don't need to respond.

`RowClass` is free — it costs one delegate call per row and allocates nothing measurable.

## Density & styling

`Striped`, `Hover` (both on by default), `Small` and `Responsive` (on by default, wrapping the table in a
`.table-responsive` scroll container) map to Bootstrap's table classes. `Id` and `Class` land on the `<table>`,
and `Class` is joined last so it wins the cascade.

### Scrolling & sticky headers

`MaxHeight` (any CSS length) makes the table scroll in its own box instead of running down the page, and
`StickyHeader` freezes the header row while the body scrolls under it. The pager stays outside the scroll box.

```csharp
BsDataGrid(Data: readings, MaxHeight: "280px", StickyHeader: true, Columns: [...])
```

**`StickyHeader` needs `MaxHeight`.** A sticky header sticks to its nearest scroll container, so with nothing
bounding the height there is nothing to stick to and the header scrolls away with the page. Set both, or
neither.

<!-- demo:data-grid-sticky -->

## Accessibility

Handled for you, and worth knowing about:

- Every header is `<th scope="col">`.
- A sortable header carries `aria-sort` (`ascending`/`descending`/`none`) and its control is a real `<button>`
  — so sorting is keyboard-operable (Tab, then Enter or Space) with no extra work.
- The expander toggle has an accessible name and `aria-expanded`, plus `aria-controls` pointing at its detail
  row while open.
- `OnRowClick` adds **no** `role`/`tabindex` to the row, because faking a button there would cost the row its
  semantics. It is a pointer shortcut for an action that must also have a real control — see
  [row clicks](#a-clickable-row-is-not-an-accessible-control).

## Server-side paging & sorting

`Data` alone hands the grid the whole list and it sorts and pages in memory — fine for hundreds of rows, wrong
for hundreds of thousands. There are two ways to let the database do the work.

### The short way: hand it the query

Pass an `IQueryable<T>` as `Data` — an EF `DbSet` or any query built from one. The grid orders it by the sorted
column's `SortBy` expression, counts it, and materialises only the current page, so `ORDER BY`/`COUNT`/`LIMIT`
all happen in the database and the whole table is never loaded.

```csharp
BsDataGrid(
    // Order it: Skip/Take over an unordered SQL query is undefined (rows can repeat or vanish between
    // pages, and EF warns). Sorting a column replaces this ordering; it is the fallback.
    Data: db.Products.AsNoTracking().Where(p => p.Active).OrderBy(p => p.Id),
    PageSize: 20,
    Columns:
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
[`/table`](/table); its source is on the page, verbatim. See also [routing.md](routing.md#query-parameters).

## When not to use it

With a list as `Data`, the grid sorts and pages **in memory**. For large sets pass an `IQueryable`, or a
fetched page + `TotalCount` (above), so the database does the work.

For very long *unpaged* lists, `VirtualizeModel<T>` ([composition](composition.md#virtualize--windowed-lists))
renders only the visible window instead.

## Reference

`BsDataGrid<T>` parameters:

| Parameter | Default | Purpose |
|---|---|---|
| `Data` | `null` | The rows. An `IQueryable` → sorted/paged in the store (server only, blocks); a list → in memory; a lazy sequence is materialised once. |
| `Columns` | `null` | The `BsColumn<T>` list. |
| `PageSize` | `0` | Rows per page; `0` shows everything and renders no pager. |
| `RowKey` | row index | Stable row identity. Required for `ExpandedContent`. |
| `Empty` | `null` | Rendered instead of the grid when there are no rows. |
| `ExpandedContent` | `null` | Builds a row's detail row (master-detail). |
| `Striped` / `Hover` | `true` | Bootstrap table styling. |
| `Small` | `false` | `.table-sm`. |
| `Responsive` | `true` | Wraps the table in `.table-responsive`. |
| `RowClass` | `null` | Extra classes for a row, computed from it. Data rows only. |
| `OnRowClick` / `OnRowClickAsync` | `null` | The row the user clicked; the async form is awaited. Fires from `RowClickable` cells only. |
| `StickyHeader` | `null` | Freezes the header row. Needs `MaxHeight`. |
| `MaxHeight` | `null` | Bounds the table's scroll container (any CSS length). Pager stays outside. |
| `TotalCount` | `null` | Rows behind `Data`. Set = `Data` is one already-sorted, already-paged slice. |
| `Page` | `null` | Controlled 0-based page. Null = the grid owns it. |
| `OnPageChange` / `OnPageChangeAsync` | `null` | The page the user asked for; the async form is awaited. |
| `Sort` | `null` | Controlled sort: the `SortField` of the sorted column. |
| `SortDescending` | `false` | Direction of the controlled sort. |
| `OnSortChange` / `OnSortChangeAsync` | `null` | The sort the user asked for (`DataGridSort`); the async form is awaited. |
| `Id` / `Class` | `null` | Applied to the `<table>`. |

`BsColumn<T>`: `Title`, `Value`, `Template`, `Sortable`, `SortKey` (in-memory ordering), `SortField` (names the
column in `OnSortChange`), `SortBy` (the `ORDER BY` expression when `Data` is an `IQueryable`), `Class`,
`Footer`, `FooterTemplate`, `RowClickable` (null = auto: `Value` columns yes, `Template` columns no).
