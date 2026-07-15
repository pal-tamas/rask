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

## Live example

Cards, driven entirely by Rask's live runtime — **no `bootstrap.js`**:

<!-- demo:bootstrap-cards -->
