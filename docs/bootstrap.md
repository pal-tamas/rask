# Rask.Bootstrap

Typed [Bootstrap 5.3](https://getbootstrap.com/docs/5.3/) components for Rask. Discoverable C#
factories (`BsButton`, `BsCard`, `BsModal`, …) that emit correct Bootstrap markup, interactive
components driven by Rask's live runtime with **zero JavaScript**, `IFormControl<T>`-bound form
controls, color modes, a typed `BsIcon`, and **typed utility classes**.

The package bundles Bootstrap 5.3.8 + Bootstrap Icons 1.13.1 as static web assets under
`_content/Rask.Bootstrap` and is a self-contained, optional package (like the validation libraries) —
no changes to your host beyond linking the CSS.

## Setup

Reference the package, then link the CSS in your `App`'s `Head` with `BootstrapStyles()`:

```csharp
protected override Component? Head =>
[
    Title()["My App"],
    BootstrapStyles()           // links _content/Rask.Bootstrap/css + icons (PathBase-aware)
];
```

`BootstrapStyles(Icons: false)` skips the Bootstrap Icons stylesheet. It also always links a tiny
`rask-bootstrap.css` (after Bootstrap, so it wins the cascade) with fixes for the zero-JS components:
`BsDropdown(AlignEnd: true)` would otherwise be a no-op because Bootstrap gates its alignment classes
on a Popper-only attribute. The assets are served by the host's static-file pipeline (`app.UseStaticFiles()` /
`app.MapStaticAssets()` on Server; the WASM static-web-asset pipeline bakes them into the published
`wwwroot` automatically).

The `Bs*` factories are available unqualified — the package ships a `build/*.props` that adds the
global `using static Rask.Bootstrap.Generated`, exactly like the validation packages.

Typed enums replace stringly-typed variants everywhere: `BsColor` (Primary…Dark), `BsSize` (Sm/Md/Lg),
`BsTheme` (Light/Dark, via `data-bs-theme`), `BsPlacement`, `BsSpinnerKind`, `BsPlaceholderAnimation`,
and `BsIconName` (every Bootstrap Icons glyph). Interactive components are **controlled** — you own the
open/active state and wire `Open:`/`OnClose:` (etc.); flipping the field re-renders through the live
runtime, no `bootstrap.js`.

```csharp
BsButton(Color: BsColor.Primary, Size: BsSize.Lg)["Save"]
BsModal(Open: _open, Title: "Hi", OnClose: () => _open = false)[ /* body */ ]  // traps focus, Escape-closes, labelled — see accessibility.md
BsInput(() => model.Email, Label: "Email", Type: InputType.Email)   // .is-invalid + .invalid-feedback built in
BsIcon(Name: BsIconName.HeartFill, Color: BsColor.Danger)
```

## Components

Each component group has its own page. Every interactive component is **controlled** and runs with
**zero `bootstrap.js`**.

| Guide | Components |
|---|---|
| **[Buttons & badges](bootstrap-buttons.md)** | `BsButton` `BsButtonGroup` `BsBadge` `BsCloseButton` |
| **[Cards, lists & tables](bootstrap-cards.md)** | `BsCard` (+`BsCardHeader/Body/Footer/Title/Subtitle/Text/Image`) `BsListGroup`(+item) `BsPlaceholder` `BsTable` `BsPagination`(+`BsPageItem`) `BsBreadcrumb`(+item) |
| **[Alerts, spinners & progress](bootstrap-feedback.md)** | `BsAlert` `BsSpinner` `BsProgress` |
| **[Icons](bootstrap-icons.md)** | `BsIcon` — typed over every Bootstrap Icons glyph via `BsIconName` |
| **[Navbar & nav](bootstrap-navigation.md)** | `BsNavbar` `BsNavbarBrand` `BsNav` `BsNavItem` (SPA-routed, auto-active) |
| **[Modals, offcanvas & dropdowns](bootstrap-overlays.md)** | `BsModal` `BsOffcanvas` `BsDropdown`(+item) + the fixed-position popover helper |
| **[Tabs, accordion & collapse](bootstrap-disclosure.md)** | `BsTabs`(+`BsTabItem`) `BsAccordion`(+item) `BsCollapse` |
| **[Toasts](bootstrap-toasts.md)** | `BsToast` `BsToaster` |
| **[Form controls](bootstrap-forms.md)** | `BsInput<T>` `BsTextarea<T>` `BsCheck` `BsRadioGroup<T>` `BsCheckboxGroup<T>` `BsFormGroup` `BsFormLabel` `BsInputGroup`(+`BsInputGroupText`) |
| **[Selects & multiselect](bootstrap-select.md)** | `BsSelect<T>` `BsMultiSelect<T>` — searchable, keyboard-contained comboboxes |
| **[Date & time pickers](bootstrap-pickers.md)** | `BsDatePicker<T>` `BsTimePicker<T>` `BsDateTimePicker<T>` |
| **[Utility classes](bootstrap-utilities.md)** | `Bs.Join(...)` + the typed utility-class groups |

## Versioning

The bundled Bootstrap (5.3.8) and Bootstrap Icons (1.13.1) are vendored under
`src/Rask.Bootstrap/wwwroot`. To bump them, replace those files and regenerate `BsIconName.g.cs` from
the icon font CSS (see the comment at the top of that file).
