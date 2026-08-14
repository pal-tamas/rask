# Bootstrap — navbar & nav

The navigation components from [`Rask.Bootstrap`](bootstrap.md) — `BsNavbar`, `BsNavbarBrand`,
`BsNav`, and `BsNavItem`. Each `BsNavItem` with an `Href` renders a SPA-routed `NavLink` that
auto-highlights the active route, and the whole shell runs with **zero `bootstrap.js`**.

For the overlay navigation (modal, offcanvas, dropdown) see
[modals, offcanvas & dropdowns](bootstrap-overlays.md).

```csharp
// A navbar shell + a vertical nav whose items SPA-route and self-highlight.
BsNavbar.Color(BsColor.Dark).Theme(BsTheme.Dark).Sticky(true)[ /* brand, actions */ ]
BsNav.Vertical(true).Pills(true)[
    BsNavItem.Href(Routes.HomePage())["Home"],
    BsNavItem.Href(Routes.OrdersPage()).Match("/orders").ActiveMatch(NavLinkMatch.Prefix)["Orders"]
]
```

## Live example

A navbar shell + a vertical nav whose items SPA-route and self-highlight — driven entirely by Rask's
live runtime, **no `bootstrap.js`**:

<!-- demo:bootstrap-nav -->
