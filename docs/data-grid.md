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
- [Accessibility](#accessibility) · [Reference](#reference)

## On this page

- [Server-side data & URL state](data-grid-server.md) — server paging/sorting, loading state, URL-driven state, when not to use it.

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
[when not to use it](data-grid-server.md#when-not-to-use-it) for large result sets.

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
