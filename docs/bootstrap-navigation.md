# Bootstrap — navigation & overlays

The interactive navigation and overlay components from [`Rask.Bootstrap`](bootstrap.md) — the
navbar/nav, tabs & accordion, modal, toast and dropdown. Every one is **controlled** (you own the
open/active state and flip it through the live runtime) and runs with **zero `bootstrap.js`**.

See also: [Bootstrap components](bootstrap.md) (setup, layout, utilities) ·
[Forms & inputs](bootstrap-forms.md) (the `IFormControl<T>` controls).

Interactive components are controlled — you own the state and wire `Open:`/`OnClose:` (etc.); flipping
the field re-renders through the live runtime, no `bootstrap.js`:

```csharp
BsModal(Open: _open, Title: "Hi", OnClose: () => _open = false)[ /* body */ ]  // traps focus, Escape-closes, labelled — see accessibility.md
BsModal(Open: _open, FullscreenBelow: Bp.Sm)[ /* edge-to-edge on phones, sized dialog at sm+ */ ]

// Navigation: a navbar shell + a vertical nav whose items SPA-route and self-highlight.
BsNavbar(Color: BsColor.Dark, Theme: BsTheme.Dark, Sticky: true)[ /* brand, actions */ ]
BsNav(Vertical: true, Pills: true)[
    BsNavItem(Href: Routes.HomePage())["Home"],
    BsNavItem(Href: Routes.OrdersPage(), Match: "/orders", ActiveMatch: NavLinkMatch.Prefix)["Orders"]
]
// A sidebar that is a drawer on mobile and a static column on desktop:
BsOffcanvas(Responsive: Bp.Md, Open: _open, OnClose: () => _open = false)[ /* nav */ ]
```

## Live examples

Every component below is driven entirely by Rask's live runtime — **no `bootstrap.js`** is loaded.

**Navigation** — a navbar shell + a vertical nav whose items SPA-route and self-highlight:

<!-- demo:bootstrap-nav -->

**Modal** — open and close driven by Rask state:

<!-- demo:bootstrap-modal -->

**Tabs & accordion** — controlled active/expanded state:

<!-- demo:bootstrap-tabs -->

**Toasts** — shown, stacked, dismissed and auto-hidden entirely from Rask state (no `bootstrap.js`, no
`data-bs-dismiss`, no `setTimeout`):

<!-- demo:bootstrap-toast -->

**Dropdowns** — `BsDropdown`(+`BsDropdownItem`) is a controlled, Popper-less menu: you own the `Open`
state and wire `OnToggle`, and each item's handler closes it on selection. `AlignEnd` right-aligns the
menu:

<!-- demo:bootstrap-dropdown -->

Every Bs `.dropdown-menu` popover — this dropdown, plus the `BsSelect`/`BsMultiSelect` comboboxes and
the date/time pickers on the [forms page](bootstrap-forms.md) — is re-anchored with `position: fixed`
while open by a tiny runtime helper (declarative, opt-in via `data-rask-popover`), so it escapes any
`overflow: hidden/auto` ancestor (a card, a scroll region) instead of being clipped, and tracks the
trigger on scroll/resize. The one exception is a browser rule, not a Rask bug: an ancestor with a CSS
`transform`/`filter`/`perspective`/`will-change`/`contain` becomes the containing block for
`position: fixed`, so a popover inside it is clamped to that box rather than the viewport.
