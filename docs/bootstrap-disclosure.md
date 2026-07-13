# Bootstrap — tabs, accordion & collapse

The show/hide disclosure components from [`Rask.Bootstrap`](bootstrap.md) — `BsTabs`(+`BsTabItem`),
`BsAccordion`(+item), and `BsCollapse`. Each is **controlled** (you own the active/expanded state and
flip it through the live runtime) and runs with **zero `bootstrap.js`**.

```csharp
BsTabs(Active: _tab, OnSelect: t => _tab = t)[
    BsTabItem(Key: "one")["First"],
    BsTabItem(Key: "two")["Second"]
]
BsCollapse(Open: _open)[ /* revealed content */ ]
```

## Live example

Tabs & accordion with controlled active/expanded state — driven entirely by Rask's live runtime,
**no `bootstrap.js`**:

<!-- demo:bootstrap-tabs -->
