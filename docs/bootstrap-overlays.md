# Bootstrap — modals, offcanvas & dropdowns

The overlay components from [`Rask.Bootstrap`](bootstrap.md) — `BsModal`, `BsOffcanvas`, and
`BsDropdown`. Every one is **controlled** (you own the open state and flip it through the live
runtime) and runs with **zero `bootstrap.js`**.

```csharp
BsModal(Open: _open, Title: "Hi", OnClose: () => _open = false)[ /* body */ ]  // traps focus, Escape-closes, labelled — see accessibility.md
BsModal(Open: _open, FullscreenBelow: Bp.Sm)[ /* edge-to-edge on phones, sized dialog at sm+ */ ]

// A sidebar that is a drawer on mobile and a static column on desktop:
BsOffcanvas(Responsive: Bp.Md, Open: _open, OnClose: () => _open = false)[ /* nav */ ]
```

## Modal & offcanvas

`BsModal` traps focus, closes on Escape, and is labelled for assistive tech (see
[accessibility.md](accessibility.md)). `FullscreenBelow: Bp.Sm` makes it edge-to-edge on phones and a
sized dialog at `sm+`. `BsOffcanvas` with `Responsive: Bp.Md` is a drawer below the breakpoint and a
static column above it.

<!-- demo:bootstrap-modal -->

## Dropdowns

`BsDropdown`(+`BsDropdownItem`) is a controlled, Popper-less menu: you own the `Open` state and wire
`OnToggle`, and each item's handler closes it on selection. `AlignEnd` right-aligns the menu:

<!-- demo:bootstrap-dropdown -->

## The fixed-position popover helper

Every Bs `.dropdown-menu` popover — this dropdown, plus the `BsSelect`/`BsMultiSelect` comboboxes and
the date/time pickers on the [selects](bootstrap-select.md) and [pickers](bootstrap-pickers.md) pages —
is re-anchored with `position: fixed` while open by a tiny runtime helper (declarative, opt-in via
`data-rask-popover`), so it escapes any `overflow: hidden/auto` ancestor (a card, a scroll region)
instead of being clipped, and tracks the trigger on scroll/resize. The one exception is a browser
rule, not a Rask bug: an ancestor with a CSS
`transform`/`filter`/`perspective`/`will-change`/`contain` becomes the containing block for
`position: fixed`, so a popover inside it is clamped to that box rather than the viewport.

The package also ships `BsPopover` (a hover/click text popover) and `BsConfirmDialog` (a modal-backed
confirm prompt) for the common one-off cases.
