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

## Advanced features

Grouping, the columns chooser, selection, and row styling / row clicks live in a companion page so this one
stays focused on the essentials:

- **[Data grid — advanced features](data-grid-advanced.md)** — [grouping](data-grid-advanced.md#grouping)
  (bands + row grouping), the [columns chooser & reordering](data-grid-advanced.md#columns-chooser--reordering),
  [selection](data-grid-advanced.md#selection), and [row styling & row clicks](data-grid-advanced.md#row-styling--row-clicks).


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
- The pager's prev/next arrows are icon-only, so they carry an explicit `aria-label` (*"Previous page"* /
  *"Next page"*); the numbered items are named by their own text. (`BsPageItem` takes an `Aria` bag if you
  build your own pager and need to name an icon-only control.)
- A selection checkbox has no visible label, so it is named from its row's first `Value` column ("Select
  Espresso Machine") rather than twenty identical "Select row"s, which read as one control repeated. The
  header's says *"select all rows on this page"* — see [selection](data-grid-advanced.md#select-all-covers-the-page).
- `OnRowClick` adds **no** `role`/`tabindex` to the row, because faking a button there would cost the row its
  semantics. It is a pointer shortcut for an action that must also have a real control — see
  [row clicks](data-grid-advanced.md#a-clickable-row-is-not-an-accessible-control).
- While `Loading`, the table is `aria-busy` and the sort/pager controls are `aria-disabled` (not `disabled`,
  which would drop focus to `<body>`). The spinner's `role="status"` live region sits *outside* the
  `aria-busy` table — inside it, the announcement would be deferred until busy cleared, i.e. never.
- The [column chooser](data-grid-advanced.md#columns-chooser--reordering) is a real `<button>` disclosure (`aria-expanded`); each
  menu row is a labelled checkbox (*"Show Region"*) and labelled move buttons (*"Move Region earlier"*),
  disabled at the ends rather than live-looking no-ops. Header drag-to-reorder is a mouse accelerator, so every
  hide and every move is reachable from the keyboard alone.

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
| `Grouped` | `null` | The `Field` names to band by, outermost first. URL-serialisable. |
| `OnGroupedChange` / `OnGroupedChangeAsync` | `null` | The grouping the user asked for; the async form is awaited. |
| `GroupCollapsible` | `null` | Band headers become toggles. |
| `GroupSubtotals` | `null` | A subtotal row per innermost band, reusing each column's `Footer`. Page-scoped. |
| `GroupPanel` | `null` | Chips + per-header group controls. Drag or keyboard — every gesture has a button. |
| `ShowGroupedColumns` | `null` | `true` keeps a grouped column in the table. Default folds it away — its value already names the band header. |
| `ColumnChooser` | `null` | Renders the "Columns" menu (show/hide checkbox + reorder buttons) and enables header-drag reorder. Implied by `HiddenColumns`/`ColumnOrder`. |
| `HiddenColumns` | `null` | Controlled visibility: the `Field` names to hide. URL-serialisable. Null = the grid owns it. |
| `OnHiddenColumnsChange` / `OnHiddenColumnsChangeAsync` | `null` | The full hidden set after a toggle; the async form is awaited. |
| `ColumnOrder` | `null` | Controlled order: the `Field` names in display order. Partial + stale-tolerant. URL-serialisable. |
| `OnColumnOrderChange` / `OnColumnOrderChangeAsync` | `null` | The full column order after a move; the async form is awaited. |
| `Selectable` | `null` | Adds the leading checkbox column. Implied by `SelectedKeys`/`OnSelectionChange`. Set `RowKey` with it. |
| `SelectedKeys` | `null` | Controlled selection (`RowKey` values). Null = the grid owns it. |
| `OnSelectionChange` / `OnSelectionChangeAsync` | `null` | The full set of selected keys after a click; the async form is awaited. |
| `RowClass` | `null` | Extra classes for a row, computed from it. Data rows only. |
| `OnRowClick` / `OnRowClickAsync` | `null` | The row the user clicked; the async form is awaited. Fires from `RowClickable` cells only. |
| `StickyHeader` | `null` | Freezes the header row. Needs `MaxHeight`. |
| `MaxHeight` | `null` | Bounds the table's scroll container (any CSS length). Pager stays outside. |
| `Loading` | `null` | `null` = feature unused; `false` = idle; `true` = fetching (spinner, `aria-busy`, clicks ignored). |
| `TotalCount` | `null` | Rows behind `Data`. Set = `Data` is one already-sorted, already-paged slice. |
| `Page` | `null` | Controlled 0-based page. Null = the grid owns it. |
| `OnPageChange` / `OnPageChangeAsync` | `null` | The page the user asked for; the async form is awaited. |
| `Sort` | `null` | Controlled sort: the `SortField` of the sorted column. |
| `SortDescending` | `false` | Direction of the controlled sort. |
| `OnSortChange` / `OnSortChangeAsync` | `null` | The sort the user asked for (`DataGridSort`); the async form is awaited. |
| `Id` / `Class` | `null` | Applied to the `<table>`. |

`BsColumn<T>`: `Title`, `Value`, `Template`, `Class`, `Footer`, `FooterTemplate`, `RowClickable` (null = auto:
`Value` columns yes, `Template` columns no).

**Column chooser:** `Hideable` (default `true`; `false` pins the column visible), `Reorderable` (default
`true`; `false` anchors it at its declared slot). A column with both `false` is a fixture the chooser ignores.

**Naming a column:** `Field` (an expression — names the column from the member, and doubles as the `ORDER BY`),
`Groupable`, `GroupKey` (band coarser than the cell), `GroupHeader` (custom band header).

**Sorting:** `Sortable`, `SortKey` (in-memory ordering), `SortField` (overrides the name in `OnSortChange`),
`SortBy` (overrides the `ORDER BY` expression under `IQueryable`).
