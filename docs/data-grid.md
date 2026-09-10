# The data grid (`UiDataGrid`)

A table over a typed row sequence — sortable headers, paging, selection, expandable detail rows,
grouping, a column chooser, and a card layout on a phone. It lives in [the UI kit](ui-kit.md), so
`dotnet add package Rask.Ui` is the whole installation.

```csharp
UiDataGrid.Data(_products)[c => [
    c.Field(p => p.Name).Title("Product").Sortable(true),
    c.Field(p => p.Price).Title("Price").Class("text-right"),
]]
```

## Columns arrive through a factory, and that is not a style choice

The children indexer takes a **lambda**, and its parameter is the grid itself. Everything else about
the grid follows from that one fact, so it is worth being precise about why.

A column names a member of the row type — `c.Field(p => p.Name)`. C# infers a method's type arguments
from **its own arguments**, never from the target type of the indexer the call sits in. Written as a
flat child, `UiColumn.Field(p => p.Name)` has nothing at all to tell it what `p` is, and the call fails
with CS0411. Handing the grid to a lambda fixes the row type before a single column is written:

```csharp
UiDataGrid.Data(_products)[c => [        // c is UiDataGrid<Product>
    c.Field(p => p.Name),                // so p is a Product
]]
```

This is why the grid's chain is `GridBuild<T, TKey>` rather than the usual `Build<T>` — an indexer
cannot be constrained, so the only way to offer a column-factory indexer on a grid and nowhere else is
for the grid's chain to be a different type. It is the same reasoning that gave `Form` its own
`FormBuild<T>`.

Two openings:

| | |
| --- | --- |
| `c.Field(p => p.X)` | A column bound to a member. Its **field token** (`"x"`) is its identity — what sorting, grouping, hiding and reordering all speak in — and it supplies the cell value with nothing further said. |
| `c.Column()` | A column bound to nothing: an actions column, or one computed from the whole row. It has no token, so it can be shown but never sorted, grouped, hidden or reordered by name. |

A column may be `null` — `admin ? c.Field(p => p.Cost) : null` — and is dropped rather than rendered.
That is why a column is a component at all.

## Where the sorting and paging happen

Decided by what the grid is given, and the three modes are reached by three different steps.

**In memory.** A list. Right for a set small enough to hold, and it asks nothing of the caller.

```csharp
UiDataGrid.Data(_products)[c => [ … ]]
```

**In the store.** An `IQueryable<T>`, handed to the *same* `Data` step. The grid translates the sort
into `ORDER BY` and the page into `Skip`/`Take` and counts with `Count()`, so the set can be
arbitrarily large. One step rather than two, because an `IQueryable<T>` **is** an `IEnumerable<T>` and a
second name for the same slot is a second thing to get wrong.

```csharp
UiDataGrid.Data(db.Products).PageSize(25)[c => [
    c.Field(p => p.Name).Title("Product").Sortable(true),
]]
```

A column sorted this way needs a `Field` or a `SortBy` its provider can translate — both are
`Expression`s for exactly that reason, where `SortKey` is the in-memory equivalent and is ignored here.
This mode enumerates **synchronously**, which is why the third exists.

**Awaited.** A `Source`: the grid hands you the sort and page it wants and takes back the rows plus a
total.

```csharp
UiDataGrid.Of<Product>().PageSize(25).Source(async request =>
{
    var page = await _api.GetProductsAsync(request.Sort, request.Descending, request.Page, request.PageSize);
    return new UiGridPage<Product>(page.Rows, page.Total);
})[c => [ … ]]
```

`Of<Product>()` opens the chain here because a lambda cannot pin a type argument — there is nothing in
`request => …` that says what a row is.

Rows it returns are rendered as they arrive, already ordered and already sliced; the grid sorts and
pages nothing of its own, and `UiGridPage.Total` is what the pager counts in. The source is asked again
only when the request actually changes.

**A page you fetched yourself.** `Data` plus `TotalCount`: the rows are rendered as given and the pager
counts in what it was told. A grid whose parent pages for it but leaves `TotalCount` unset renders a
pager that always claims to be one page long.

## Controlled and uncontrolled, one axis at a time

Say nothing and the grid holds its own sort, page, selection, grouping and column layout in fields and
redraws through the live diff. Name the state **and** its change callback and that axis belongs to the
page instead — while every other axis carries on holding its own.

```csharp
UiDataGrid.Data(_page)
    .Sort(_sort).SortDescending(_desc).OnSortChange(s => { _sort = s.Field; _desc = s.Descending; })
    .TotalCount(_total).Page(_page).OnPageChange(async p => await LoadAsync(p))
    [c => [ … ]]
```

| Axis | State | Callback |
| --- | --- | --- |
| Sort | `Sort`, `SortDescending` | `OnSortChange` / `OnSortChangeAsync` |
| Page | `Page` | `OnPageChange` / `OnPageChangeAsync` |
| Selection | `Selected` | `OnSelectionChange` / `OnSelectionChangeAsync` |
| Grouping | `Grouped` | `OnGroupedChange` / `OnGroupedChangeAsync` |
| Hidden columns | `HiddenColumns` | `OnHiddenColumnsChange` / `…Async` |
| Column order | `ColumnOrder` | `OnColumnOrderChange` / `…Async` |

A sortable header cycles **ascending → descending → off**. The third state is not decoration: it is the
only way back to the order the source itself chose.

## Selection is typed, and only offered once a row can be named

```csharp
UiDataGrid.Data(_products)
    .RowKey(p => p.Id)                       // the chain now carries int
    .Selected(_selected)                     // IReadOnlyList<int>
    .OnSelectionChange(keys => _selected = keys)
    [c => [ … ]]
```

`RowKey` pins the chain's key type, and the selection steps are declared only over a pinned one — so a
grid that has not said what identifies a row is not one whose selection is *rejected*: it is one where
selection is not offered, in completion or at compile time. The keys reaching your callback are
`IReadOnlyList<int>`, not boxed objects, and a membership test per row per render boxes nothing.

It is **not** `Key`: that is already the chain's step for reconciliation identity — which instance of
the *grid* is being built — and has to be able to come first ([RASK046](diagnostics.md#rask046)).

Naming a row key alone is an identity, not an invitation to select. The checkboxes appear with the
selection itself. "Select all" ticks **this page** and says so, because a grid holding one page can only
name the keys it has.

## The phone

Below `sm` the table restyles itself into stacked, labelled lines — each cell keeps its column's title
in front of it, through `data-label` and a `before:content-[attr(data-label)]` utility. It is the
**same markup** under different utilities, so it costs nothing but the classes.

`Card(p => …)` replaces those lines with markup of your own. That version *does* cost: the authored
cards and the table both render, the table hidden below `sm` and the cards above it. Reach for it when a
phone wants genuinely different content, not merely the same cells stacked.

## Grouping, and the chrome above the table

`Groupable(true)` on a column puts a group button in its header. `GroupPanel(true)` shows the panel that
reorders and removes the grouping; `ColumnChooser(true)` shows the menu that hides, shows and moves
columns.

**Both are driven by buttons, and drag is added on top.** That is a decision about who can use them
rather than about taste: HTML5 drag events do not fire on touch at all and cannot be driven from a
keyboard, so a panel whose only gesture was dragging would be unreachable on the phone this kit designs
for first. Both paths call the same handler.

A grouped column leaves the table by default — it holds the same value for every row of its band, and
that value is already the band's heading. `ShowGroupedColumns(true)` keeps it. `GroupSubtotals(true)`
repeats the column footers per band; `GroupCollapsible(false)` stops a band folding shut.

## Cells, clicks, and the rule behind them

`Cell(p => …)` gives a column custom markup; `Footer` and `FooterCell` give it a summary computed over
every row, not merely the page.

`OnRowClick` fires from a column's cells rather than from the row, so a column can carve itself out —
and by default a **custom cell does not fire it**. That asymmetry is a safety rule. The client cancels
the default action of any click it dispatches, so under a row handler a checkbox never fires `change`,
an `<a href>` never navigates, and a bare `<button>` (which defaults to `type=submit`) swallows the
click instead. Every one of those failures is silent. A text cell can contain none of them and is always
safe; a custom cell is exactly where an author puts a link, so it opts out. `RowClickable(true)` opts a
non-interactive template back in.

## It needs the runtime

Sorting, paging and every other interaction here are C# handlers, so a grid on a page that has not
booted renders its first page and stays there. That is the deliberate trade — see
[who owns the state](ui-kit.md#who-owns-the-state) for the kit's three answers and why this component
takes the one it does.

## See also

- [The UI kit](ui-kit.md) — everything else in the package, and the rules it holds itself to
- [Building components](building-components.md) — the chain, and the shapes it comes in
- [Testing](testing.md) — `RaskTest` drives the grid's handlers in-process, with no browser
