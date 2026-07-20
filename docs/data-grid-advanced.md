# Data grid — advanced features

Advanced `BsDataGrid<T>` features that build on the [data grid basics](data-grid.md): grouping (bands and
row grouping), the columns chooser, selection, and row styling / row clicks. Start with the
[main data grid guide](data-grid.md) for columns, sorting, paging, and server-side data.


## Grouping

Band rows by a column's value. Name the column with `Field`, mark it `Groupable`, and list the fields to group
by — outermost first:

```csharp
new BsColumn<Deal> { Title = "Region", Value = d => d.Region, Field = d => d.Region, Groupable = true }
...
BsDataGrid(Data: deals, Grouped: ["region", "rep"], OnGroupedChange: g => _grouped = [.. g],
    GroupCollapsible: true, GroupSubtotals: true, Columns: [...])
```

<!-- demo:data-grid-group -->

### `Field` names the column

`Field = d => d.Region` calls the column `"region"` — the token `Grouped` carries, `OnSortChange` reports, and
a URL serialises (`?group=region,rep`). It is an **expression**, so the name is read off the member and cannot
drift from the property it describes.

`Value` could never supply it: it is a `Func<T, object?>`, and a compiled delegate carries no member name.

One `Field` also feeds the rest — it names a controlled sort (`SortField` still wins where set) and doubles as
the `ORDER BY` for an `IQueryable` (`SortBy` still wins). Name a column once.

### Bands are runs, and the grid keeps them whole

A band is a run of **consecutive** rows sharing the key. That only works if the rows arrive ordered by the
group keys — so the grid orders them:

| Mode | Who orders | Result |
|---|---|---|
| list (in memory) | **the grid** | Bands are always whole. |
| `IQueryable` | **the grid** | The group columns lead the `ORDER BY`, so bands are whole within the page. |
| `TotalCount` slice | **you** | The grid never holds the set. Order by these fields in your query. |

Your sort then applies **within** each band — "group by region, sort by amount" means what it looks like. A
groupable column needs an expression to order by under `IQueryable`; without one it is skipped, the same rule
`Sortable` already follows there.

Under the two server-side modes a band can still **split across a page boundary**: the grid only holds one
page, and paging cuts wherever it cuts.

`Grouped` is URL input, so unknown or non-`Groupable` field names are ignored rather than thrown — a stale
`?group=deleteMe` renders an ungrouped grid.

### Grouped columns fold away

A grouped column holds the **same value for every row in its band**, and the band header already names it
(`Region: EMEA (4)`). Rendering the column too would be a run of duplicates under a header that says nothing new —
so by default a grouped column is **dropped from the table** while it is grouped: its header, cells, subtotal
and footer go, and the band-header and detail-row colspans shrink to match. Its group control moves to the panel
chip, which is where you ungroup it.

Set `ShowGroupedColumns: true` to keep it — the value then shows in the band header **and** repeated down every
row (the behaviour before this default). Nothing else changes; grouping still orders, bands and subtotals the
same way.

```csharp
BsDataGrid(Data: deals, Grouped: ["region"], OnGroupedChange: g => _grouped = [.. g],
    ShowGroupedColumns: true, Columns: [...]) // keep the Region column visible while grouped
```

### Collapsing and subtotals

`GroupCollapsible` makes each band header a toggle. Collapse state is keyed by the band's **value path**, so it
follows the band through a sort or a page change rather than staying at a position.

`GroupSubtotals` adds a subtotal row per innermost band, reusing each column's `Footer`/`FooterTemplate` over
that band's rows — one hook, not two. Like the grand footer under `TotalCount`/`IQueryable`, a subtotal only
sees **the rows on this page**.

### The group panel

`GroupPanel: true` renders a chip per group level above the grid and a group control on every `Groupable`
header, so the user can group, renest and ungroup:

```csharp
BsDataGrid(Data: deals, GroupPanel: true, Grouped: _grouped,
    OnGroupedChange: g => _grouped = [.. g], Columns: [...])
```

Drag a header into the panel to group by it, drag the chips to renest, drag one out to ungroup.

**Every one of those is also a real `<button>`** — the chips carry ungroup and move in/out, and each groupable
header carries group-by. So the whole feature works from the keyboard alone, and drag is only an accelerator.
That ordering is deliberate: a feature whose primary action is drag-only cannot be reached by keyboard at all,
which would fail WCAG 2.1.1 for the thing the panel exists to do.

The edge buttons are `disabled` at the ends rather than being no-ops that look live, and a groupable header's
group control carries `aria-pressed` so its state is announced. Once a column is grouped its header folds away
(see [Grouped columns fold away](#grouped-columns-fold-away)), so the panel chip is what then carries its
ungroup control — unless `ShowGroupedColumns: true` keeps the header, and its pressed control, in place.

### What it costs

Grouping re-orders the set and boxes one key per row per level to find the runs — but by default it also
**removes the grouped column's cells**, and a folded-away column of 100 cells outweighs the ordering. Net, a
grouped grid allocates **less** than a plain one over 100 rows: about **−8%** for one level, **−7%** for two
(`BsDataGridBenchmarks`). Keep the column with `ShowGroupedColumns: true` and the ordering shows as a cost
instead — **+5%** for one level, **+19%** for two.

**Don't group by a near-unique column.** Grouping 100 rows by a unique SKU is 100 bands — one header per row —
and a band header spans the whole table whether or not the column folds away, so it stays expensive. Group by
the low-cardinality things (region, status, month); that is what a band is for.

## Columns chooser & reordering

`ColumnChooser: true` renders a **"Columns" menu** above the grid: a checkbox to show or hide each column, and
move earlier/later buttons to reorder it.

```csharp
BsDataGrid(Data: deals, ColumnChooser: true,
    HiddenColumns: _hidden, OnHiddenColumnsChange: h => _hidden = [.. h],
    ColumnOrder: _order,   OnColumnOrderChange: o => _order = [.. o],
    Columns: [...])
```

<!-- demo:data-grid-columns -->

Both axes are **token lists of `Field` names**, exactly like `Grouped` — `HiddenColumns: ["region"]` and
`ColumnOrder: ["amount", "name", "region"]`. Set them (with the change callbacks) to control the layout the way
`Grouped` controls grouping, or leave them null and the grid owns its own. Because they are just tokens they are
URL-serialisable, which is the whole point (see [Putting the state in the URL](data-grid-server.md#putting-the-state-in-the-url)):
`?hide=region&cols=amount,name,region` restores a laid-out grid on reload or share.

**Every action is a real `<button>` or checkbox**, so the menu works from the keyboard alone. Dragging a header
onto another header reorders it too — a mouse accelerator over the same handlers — while dragging a header onto
the [group panel](#the-group-panel) still groups by it. Which one a drop means is decided by where it lands, so
the two gestures share one drag without fighting.

### It composes with the rest

Hiding, reordering and grouped-column folding funnel through **one** visible-column list, so everything
downstream follows for free:

- **Sort** tracks a column's identity, not its slot: reorder the columns and clicking a header still sorts the
  right one; the caret rides along.
- A **hidden column keeps its sort applied** to the data — only its header disappears until you show it again,
  just as a grouped-away column's grouping persists though its header is gone.
- **Footers and subtotals** drop with their column and the `<tfoot>` and band colspans shrink to match.
- An explicit `HiddenColumns` entry **overrides `ShowGroupedColumns`** — asking to keep a grouped column, then
  hiding it, removes it.

### The rules at the edges

- **Name your columns.** A column addressed by the chooser needs a `Field` (its token); one without is a
  fixture — always visible, held at its declared slot. [RASK034](diagnostics.md#rask034) warns at the call site
  when a chooser column has no `Field`.
- **Opt a column out** with `Hideable = false` (pins it visible) or `Reorderable = false` (anchors its
  position). A column that sets both is a pure fixture and RASK034 leaves it alone.
- **At least one column always shows.** The last visible column's checkbox is disabled, and a stale
  `HiddenColumns` that resolves to hiding everything is ignored wholesale — the grid never renders a bodyless
  table. A stale or unknown token (`?hide=deleteMe`) is dropped, the same tolerance `Grouped` gives.
- **A new column absent from a persisted `ColumnOrder`** is appended at its declared position, never dropped —
  so adding a column without touching a saved layout lands it predictably at the end.

A grid that uses none of this renders byte-identical markup and allocates exactly as before. With the chooser
on but idle the cost is making each header a drag source — about **+2%** over a plain grid of 100 rows
(`BsDataGridBenchmarks`); hiding a column removes its cells, so a hidden-and-reordered grid actually allocates
**less** than the plain one.

## Selection

`Selectable` adds a leading checkbox column so rows can be picked for a bulk action. The grid tracks the
selection and reports it through `OnSelectionChange`:

```csharp
BsDataGrid(Data: tasks, Selectable: true, RowKey: t => t.Id,
    OnSelectionChange: keys => _selected = keys,
    Columns: [...])
```

<!-- demo:data-grid-selection -->

**Set `RowKey`.** Selection is tracked by key, so it follows a row through a sort and accumulates across pages
— pick three on page 1 and two on page 2 and you have five. Without a `RowKey` the grid falls back to the row
index, and the selection would follow the third *position* rather than the row you picked.

### It reports keys, not rows

`OnSelectionChange` hands you the **full set of selected keys** after every click — not a delta, and not rows.
Under `TotalCount` or an `IQueryable` the grid only ever holds the current page, so it cannot turn a key from a
page you have left back into a row. Map them yourself:

```csharp
var ids = _selected.Cast<int>().ToHashSet();
await db.Tasks.Where(t => ids.Contains(t.Id)).ExecuteDeleteAsync();
```

**Re-check them server-side.** A key can name a row that has since been deleted, or one this user may not
touch. The grid is reporting what was clicked, not granting permission.

### Select-all covers the page

The header checkbox reads *"select all rows on this page"*, because the page is all the grid holds — next to a
pager, "select all" would be a lie. Rows picked on other pages are left alone.

It has **no indeterminate state**: `indeterminate` is a JavaScript-only DOM property, and this grid renders
without any. The box is checked when every row on the page is selected, and unchecked otherwise.

### Controlled selection

Pass `SelectedKeys` to own it yourself — the same shape `Page` and `Sort` use. The grid then renders the
selection you give it and only reports clicks:

```csharp
BsDataGrid(Data: page, SelectedKeys: _selected, OnSelectionChange: k => _selected = k, ...)
```

Unlike `Sort`, "is it set?" is a sound signal here: an empty list is a perfectly good controlled selection
meaning *nothing picked*, so `null` unambiguously means the grid owns it.

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
