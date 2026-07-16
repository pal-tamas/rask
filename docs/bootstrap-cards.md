# Bootstrap — cards, lists & tables

The content containers from [`Rask.Bootstrap`](bootstrap.md) — `BsCard` (with
`BsCardHeader`/`BsCardBody`/`BsCardFooter`/`BsCardTitle`/`BsCardSubtitle`/`BsCardText`/`BsCardImage`),
`BsListGroup`(+item), `BsPlaceholder` (loading skeletons), `BsTable`, `BsPagination`(+`BsPageItem`),
and `BsBreadcrumb`(+item).

```csharp
BsCard(Class: Bs.Join(Shadow.Sm))[
    BsCardHeader()["Order #1024"],
    BsCardBody()[ BsCardTitle()["Summary"], BsCardText()["3 items · shipped"] ]
]
BsListGroup()[ BsListGroupItem()["One"], BsListGroupItem(Active: true)["Two"] ]
```

Outgrown `BsTable`? [`BsDataGrid<T>`](data-grid.md) adds typed columns, click-to-sort headers, paging,
footer totals and master-detail rows.

## Scrolling tables & sticky headers

A long table can scroll inside its own box instead of running down the page. `MaxHeight` takes any CSS
length and bounds the scroll container; `StickyHeader` then freezes the header row while the body scrolls
under it:

```csharp
BsTable(Striped: true, MaxHeight: "400px", StickyHeader: true)[
    Thead()[Tr()[Th(Scope: "col")["Product"], Th(Scope: "col")["Price"]]],
    Tbody()[rows.Select(r => Tr()[Td()[r.Name], Td()[r.Price.ToString("C")]])]
]
```

**`StickyHeader` needs `MaxHeight`.** A sticky header sticks to its nearest scroll container, so with
nothing bounding the table's height there is nothing for it to stick to and the header simply scrolls away
with the page. `MaxHeight` is what creates that container — set both, or neither.

`MaxHeight` implies the scroll wrapper even when `Responsive` is off, because a height with no scroll
container would just clip the rows.

`Aria` passes ARIA attributes through to the `<table>` itself (each entry emits `aria-{key}`), which is
how you mark a table `aria-busy` while it refetches.

Both are available on [`BsDataGrid<T>`](data-grid.md) too, which forwards them.

## Live example

Cards, driven entirely by Rask's live runtime — **no `bootstrap.js`**:

<!-- demo:bootstrap-cards -->

## Breadcrumbs

`BsBreadcrumb` renders `<nav aria-label="breadcrumb"><ol class="breadcrumb">…`. Give each
`BsBreadcrumbItem` an `Href` to make it a link, and mark the current page `Active` — it renders as plain
text with `aria-current="page"`. `Label` renames the wrapping `<nav>` for assistive tech.

```csharp
BsBreadcrumb()[
    BsBreadcrumbItem(Href: "/")["Home"], BsBreadcrumbItem(Active: true)["Data"]
]
```

<!-- demo:bootstrap-breadcrumb -->

## List groups

`BsListGroup` holds `BsListGroupItem` children. Items take `Active`, `Disabled`, and a `Color` tint;
pass `Href` for a clickable item (`.list-group-item-action`). Set `Numbered` for an auto-numbered `<ol>`
or `Flush` to drop the outer borders.

```csharp
BsListGroup()[ BsListGroupItem(Active: true)["One"], BsListGroupItem(Href: "/two")["Two"] ]
```

<!-- demo:bootstrap-listgroup -->

## Placeholders

`BsPlaceholder` is a loading skeleton — a `<span class="placeholder">` sized by `Col` (the `col-{n}`
grid width), optionally tinted with `Color` and scaled with `Size`. `Animation` (`Glow` or `Wave`) wraps
it so it shimmers while content loads.

```csharp
BsPlaceholder(Col: 7, Animation: BsPlaceholderAnimation.Glow)
```

<!-- demo:bootstrap-placeholder -->

## Tables

`BsTable` wraps a core `<table>` with the typed style toggles — `Striped`, `Hover`, `Bordered`,
`Small`, `Responsive`, and more — over the usual `Thead`/`Tbody`/`Tr`/`Th`/`Td` markup. For typed
columns, click-to-sort and paging, reach for [`BsDataGrid<T>`](data-grid.md) instead.

```csharp
BsTable(Striped: true, Hover: true)[ Thead()[Tr()[Th()["Name"]]], Tbody()[Tr()[Td()["Ada"]]] ]
```

<!-- demo:bootstrap-table -->

## Pagination

`BsPagination`(+`BsPageItem`) renders `<nav><ul class="pagination">…`. Each item is a real `<button>`
(or an `<a>` with `Href`); wire `OnClick` to drive paging from C#, mark the current page `Active`, and
`Disabled` greys the ends — all zero-JS through the live runtime.

```csharp
BsPagination()[
    BsPageItem(Disabled: true)["Previous"], BsPageItem(Active: true)["1"], BsPageItem()["2"]
]
```

<!-- demo:bootstrap-pagination -->
