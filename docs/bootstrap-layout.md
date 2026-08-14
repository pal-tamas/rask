# Bootstrap — layout

The layout primitives from [`Rask.Bootstrap`](bootstrap.md) — `BsContainer` (the page shell), `BsRow` +
`BsCol` (the 12-unit responsive grid), and `BsStack` (a flex row or column with a gap).

```csharp
BsContainer[
    BsRow.Gutter(4)[
        BsCol.Md(6)[BsCard[BsCardBody["Left"]]],
        BsCol.Md(6)[BsCard[BsCardBody["Right"]]]
    ],
    BsStack.Gap(2)[BsButton.Color(BsColor.Primary)["Save"], BsButton["Cancel"]]
]
```

Reach for these instead of hand-writing `Div(Class: "row g-4")` or `Div(Class: "d-flex gap-2")`. The
underlying tokens stay available as typed strings in the [`Grid`](bootstrap-utilities.md) and
[`Flex`](bootstrap-utilities.md) utility groups for the cases a component doesn't cover — pass them
through `Class`.

## BsContainer — the page shell

A container centres the page content and caps its width per breakpoint. It also pads its own sides by half
a gutter — and that padding is what a nested `BsRow`'s negative side margins cancel against, which is why a
bare `.row` outside a container overhangs the viewport.

| Call | Class | Behaviour |
|---|---|---|
| `BsContainer()` | `.container` | width-capped at every breakpoint |
| `BsContainer(Fluid: true)` | `.container-fluid` | full width at every breakpoint |
| `BsContainer(FluidBelow: Bp.Md)` | `.container-md` | full width **below** md, capped from md up |

`FluidBelow` is named for what it does, not for the class it emits: Bootstrap's `.container-md` reads as
"a container at md" but is really the fluid one below md — it picks up a `max-width` only from md up. It
supersedes `Fluid` when both are set.

## BsRow + BsCol — the grid

`BsRow` holds `BsCol` children and supplies their gutters. `Gutter` (`.g-0` … `.g-5`) is the space between
columns on both axes — note it is *not* flex gap: Bootstrap implements it as column padding plus row
margin. The row declares `--bs-gutter-x` on itself, so `Gutter` is the knob; setting that variable on a
surrounding container has no effect on the columns.

Each `BsCol` span prop is a width in the 12-unit grid at **one** breakpoint, and they stack exactly as the
class names do. `Span` is the unprefixed base that applies from the narrowest width up; `Sm`…`Xxl` take
over from their own breakpoint up:

```csharp
BsRow.Gutter(3)[
    BsCol[…],                  // .col           equal width, shares the row with its siblings
    BsCol.Auto(true)[…],        // .col-auto      just wide enough for its content
    BsCol.Md(6)[…],             // .col-md-6      full width below md, half from md up
    BsCol.Span(7).Sm(8)[…],    // .col-7 .col-sm-8
    BsCol.Md(6).Lg(4)[…]       // .col-md-6 .col-lg-4
]
```

A column with no span anywhere falls back to the equal-width `.col`. A column *with* one deliberately does
not also get `.col`: `col-md-6` alone is already full width below md (Bootstrap gives `.row > *`
`width:100%`), whereas `col col-md-6` would be equal-width there instead — a different layout, still
reachable with `Class: Grid.Col`.

`Auto` and `Span` are two ways to fill the **same** unprefixed slot, so they're alternatives rather than
additions — `Auto` wins if you set both. Pairing `Auto` with a *breakpoint* span is a different thing and
fully supported: `BsCol(Auto: true, Md: 6)` → `.col-auto .col-md-6`, content-width below md and half from
md up.

To centre columns of unequal height, pass the typed token through `Class`:
`BsRow(Class: Flex.Align(BsAlign.Center))`.

> `BsCol` is a grid column. Don't confuse it with [`BsColumn<T>`](data-grid.md), which is a *data-grid*
> column definition — a config object passed to `BsDataGrid`'s `Columns`, not a component.

## BsStack — flex rows and columns

`BsStack` is the one-line answer to "lay these out in a line with a gap". It's horizontal by default; set
`Vertical` for a column.

```csharp
BsStack.Gap(2)[BsButton["Save"], BsButton["Cancel"]]   // <div class="d-flex gap-2">
BsStack.Vertical(true).Gap(3)[…]                          // <div class="d-flex flex-column gap-3">
BsStack.Gap(2).Align(BsAlign.Center)[…]                   // <div class="d-flex gap-2 align-items-center">
BsStack.Justify(BsJustify.Between).Align(BsAlign.Center)[…]
BsStack.Gap(2).WrapItems(true)[…]                         // items flow onto more lines
```

`Justify` is the main axis, `Align` the cross axis. `WrapItems` says whose wrapping it controls — the
stack's items flow onto more lines; the stack itself is unaffected.

### Why not .vstack / .hstack?

`BsStack` builds on `d-flex` rather than Bootstrap's `.vstack`/`.hstack` shorthands, deliberately. Neither
shorthand is a superset of `d-flex`:

```css
.vstack { display:flex; flex:1 1 auto; flex-direction:column; align-self:stretch }
.hstack { display:flex; flex-direction:row; align-items:center; align-self:stretch }
```

`.hstack` silently adds `align-items:center`, `.vstack` adds `flex:1 1 auto`, and both add
`align-self:stretch` — so a stack built on them would restyle any plain `d-flex` it replaced.
`BsStack(Align: BsAlign.Center)` says that alignment out loud and otherwise leaves the CSS default alone.

Migrating an existing `.hstack`/`.vstack` is therefore *not* a pure rename — `align-self:stretch` is the
one part `BsStack` never emits. It only matters when the stack is itself a flex item (inside a card body
or a plain block it's inert); if a flex parent was stretching it, add `Class: "align-self-stretch"`, and
`Class: Flex.Fill` covers a `.vstack`'s `flex:1 1 auto`.

It also means responsive direction composes, which the shorthands can't do at all — Bootstrap ships no
breakpoint variant of `.vstack`/`.hstack`, while `.flex-md-row` exists:

```csharp
BsStack.Vertical(true).Gap(3).Class(Flex.Row(Bp.Md))   // column on a phone, row from md up
```

Horizontal emits no `flex-row` token, because row is already the flex default.

<!-- demo:bootstrap-layout -->
