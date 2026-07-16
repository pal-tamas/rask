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
