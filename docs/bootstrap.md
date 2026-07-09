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

## Components

| Area | Components |
|---|---|
| Content | `BsButton` `BsButtonGroup` `BsBadge` `BsAlert` `BsCard` (+`BsCardHeader/Body/Footer/Title/Subtitle/Text/Image`) `BsSpinner` `BsProgress` `BsListGroup`(+item) `BsPagination`(+`BsPageItem`) `BsBreadcrumb`(+item) `BsPlaceholder` `BsTable` `BsCloseButton` `BsIcon` |
| Navigation | `BsNavbar` `BsNav` `BsNavItem` (each `BsNavItem` with `Href` renders a SPA-routed `NavLink` that auto-highlights the active route) |
| Interactive (zero-JS, controlled) | `BsModal` `BsOffcanvas` (set `Responsive: Bp.Md` for a drawer-below / static-above sidebar) `BsCollapse` `BsAccordion`(+item) `BsTabs`(+`BsTabItem`) `BsDropdown`(+item) `BsToast` |
| Forms (`IFormControl<T>`) | `BsInput<T>` `BsTextarea<T>` `BsSelect<T>` `BsCheck` `BsRadioGroup<T>` `BsCheckboxGroup<T>` `BsMultiSelect<T>` `BsDatePicker<T>` `BsTimePicker<T>` `BsDateTimePicker<T>` `BsFormGroup` `BsFormLabel` `BsInputGroup`(+`BsInputGroupText`) |

Typed enums replace stringly-typed variants everywhere: `BsColor` (Primary…Dark), `BsSize` (Sm/Md/Lg),
`BsTheme` (Light/Dark, via `data-bs-theme`), `BsPlacement`, `BsSpinnerKind`, `BsPlaceholderAnimation`,
and `BsIconName` (every Bootstrap Icons glyph). Interactive components are **controlled** — you own the
open/active state and wire `Open:`/`OnClose:` (etc.); flipping the field re-renders through the live
runtime, no `bootstrap.js`.

```csharp
BsButton(Color: BsColor.Primary, Size: BsSize.Lg)["Save"]
BsModal(Open: _open, Title: "Hi", OnClose: () => _open = false)[ /* body */ ]  // traps focus, Escape-closes, labelled — see accessibility.md
BsModal(Open: _open, FullscreenBelow: Bp.Sm)[ /* edge-to-edge on phones, sized dialog at sm+ */ ]
BsInput(() => model.Email, Label: "Email", Type: InputType.Email)   // .is-invalid + .invalid-feedback built in
BsIcon(Name: BsIconName.HeartFill, Color: BsColor.Danger)

// Navigation: a navbar shell + a vertical nav whose items SPA-route and self-highlight.
BsNavbar(Color: BsColor.Dark, Theme: BsTheme.Dark, Sticky: true)[ /* brand, actions */ ]
BsNav(Vertical: true, Pills: true)[
    BsNavItem(Href: Routes.HomePage())["Home"],
    BsNavItem(Href: Routes.OrdersPage(), Match: "/orders", ActiveMatch: NavLinkMatch.Prefix)["Orders"]
]
// A sidebar that is a drawer on mobile and a static column on desktop:
BsOffcanvas(Responsive: Bp.Md, Open: _open, OnClose: () => _open = false)[ /* nav */ ]
```

### Live examples

Every component below is driven entirely by Rask's live runtime — **no `bootstrap.js`** is loaded.

**Navigation** — a navbar shell + a vertical nav whose items SPA-route and self-highlight:

<!-- demo:bootstrap-nav -->

**Buttons & badges:**

<!-- demo:bootstrap-buttons -->

**Cards:**

<!-- demo:bootstrap-cards -->

**Alerts** — dismissible, the close is controlled state:

<!-- demo:bootstrap-alerts -->

**Icons** — the typed `BsIcon` over every Bootstrap Icons glyph:

<!-- demo:bootstrap-icons -->

**Modal** — open and close driven by Rask state:

<!-- demo:bootstrap-modal -->

**Tabs & accordion** — controlled active/expanded state:

<!-- demo:bootstrap-tabs -->

**Toasts** — shown, stacked, dismissed and auto-hidden entirely from Rask state (no `bootstrap.js`, no
`data-bs-dismiss`, no `setTimeout`):

<!-- demo:bootstrap-toast -->

**Forms** — `IFormControl<T>`-bound controls with built-in validation. `BsSelect<T>` is a custom combobox —
a `.form-select` display box (showing the option's rich `OptionLabel`) that opens a `.dropdown-menu` listbox
(data-driven `Options` + `OptionLabel`). Pass a **`Filter` predicate** (`(item, text) => bool`) to add a
**search field in the dropdown** that narrows the options as you type; a nullable binding gets an `×` clear.
`BsMultiSelect<T>` is the same but multi-value, with the chosen items shown as chips (and the same opt-in
`Filter`). Both are zero-JS live-diff, keyboard-navigable, ARIA `combobox`/`listbox`; `Native: true` drops
`BsSelect` back to the plain OS `<select>` (handy on mobile). To bind a **projected field** while the options
are objects, add an `OptionValue` selector — `BsSelect(() => model.PersonId, people, OptionValue: p => p.Id,
OptionLabel: p => Text(p.Name))` binds the id but renders/searches the whole `Person`:

<!-- demo:bootstrap-forms -->

**Date & time pickers** — `BsDatePicker<T>`/`BsTimePicker<T>`/`BsDateTimePicker<T>` are **hand-editable**:
the box is a text `<input>` you can type into (parsed live per keystroke in `CultureInfo.CurrentCulture`;
a partial/invalid entry is kept, not reverted, and blur normalises it), and focusing it opens a custom
calendar/clock **popover** (a month grid + hour/minute lists) driven entirely by Rask live-diff state — no
`bootstrap.js`. They bind `DateOnly`/`TimeOnly`/`DateTime` (and their nullable + `DateTimeOffset` forms),
localize the weekday order/names and month label from `CultureInfo.CurrentCulture`, and constrain selection
with `Min`/`Max`/`Disable`. A nullable value gets a clear (×) button; `Native: true` degrades to the native
`<input type=date|time|datetime-local>`:

<!-- demo:bootstrap-pickers -->

**Dropdowns** — `BsDropdown`(+`BsDropdownItem`) is a controlled, Popper-less menu: you own the `Open`
state and wire `OnToggle`, and each item's handler closes it on selection. `AlignEnd` right-aligns the
menu:

<!-- demo:bootstrap-dropdown -->

Every Bs `.dropdown-menu` popover — the pickers, `BsDropdown`, `BsMultiSelect`, and `BsSelect` — is re-anchored with
`position: fixed` while open by a tiny runtime helper (declarative, opt-in via `data-rask-popover`), so
it escapes any `overflow: hidden/auto` ancestor (a card, a scroll region) instead of being clipped, and
tracks the trigger on scroll/resize. The one exception is a browser rule, not a Rask bug: an ancestor
with a CSS `transform`/`filter`/`perspective`/`will-change`/`contain` becomes the containing block for
`position: fixed`, so a popover inside it is clamped to that box rather than the viewport.

## Utility classes

Bootstrap's utility classes are exposed as **typed string tokens**, grouped by family, composed into a
`Class` with `Bs.Join(...)` (it skips null/empty and returns `null` when nothing is present, so it
leaves `Class` unset rather than emitting `class=""`):

```csharp
BsCard(Class: Bs.Join(Shadow.Sm, Border.None, Margin.Bottom(4)))
Div(Class: Bs.Join(Display.Flex(), Flex.Gap(2), Flex.Justify(BsJustify.Between)))
```

Spacing, display, flex and text-alignment helpers take an optional **responsive breakpoint** `Bp`
(`Bp.Sm/Md/Lg/Xl/Xxl`), which inserts the Bootstrap infix:

```csharp
Bs.Join(Display.Flex(Bp.Lg), Margin.Bottom(4, Bp.Md))   // → "d-lg-flex mb-md-4"
```

### Groups

| Group | Members → emitted class |
|---|---|
| `Shadow` | `None` `Sm` `Default` `Lg` → `shadow-none/-sm/shadow/shadow-lg` |
| `Border` | `All` `None` `Top/End/Bottom/Start` (+`*None`) → `border` `border-0` `border-top` …; `Color(BsColor)` → `border-{color}` |
| `Margin` | `All/Top/Bottom/Start/End/X/Y(int, Bp?)` → `m{side}-{bp?}-{n}`; `XAuto` `StartAuto` `EndAuto` |
| `Padding` | `All/Top/Bottom/Start/End/X/Y(int, Bp?)` → `p{side}-{bp?}-{n}` |
| `Display` | `None/Inline/InlineBlock/Block/Flex/InlineFlex/Grid(Bp?)` → `d-{bp?}-{value}` |
| `Flex` | `Row/Column(+Reverse)/Wrap/Nowrap(Bp?)` `Fill` `Grow(int)` `Shrink(int)` `Gap(int, Bp?)` `Justify(BsJustify, Bp?)` `Align(BsAlign, Bp?)` |
| `Rounded` | `Default` `None` `Pill` `Circle` `Top/End/Bottom/Start` `Size(int)` |
| `Txt` | `Start/Center/End(Bp?)` `Color(BsColor)` `Muted` `Truncate/Wrap/Nowrap/Break` `Uppercase/Lowercase/Capitalize` `DecorationNone/Underline` |
| `Font` | `Bold/Bolder/Semibold/Medium/Normal/Light/Lighter` `Italic/NotItalic` `Size(int)` (→ `fw-*`, `fst-*`, `fs-{n}`) |
| `Sizing` | `W(int)` `H(int)` `WAuto` `HAuto` `MaxW100` `MaxH100` `VW100` `VH100` `MinVW100` `MinVH100` |
| `Position` | `Static/Relative/Absolute/Fixed/Sticky` `Top0/Top50/Bottom0/Start0/…` `TranslateMiddle(+X/Y)` |
| `Bg` | `Color(BsColor)` `Body` `BodyTertiary` `White` `Transparent` |

> The text group is named `Txt` (not `Text`) to avoid clashing with the core `Text` node component.

<!-- demo:bootstrap-utilities -->

## Versioning

The bundled Bootstrap (5.3.8) and Bootstrap Icons (1.13.1) are vendored under
`src/Rask.Bootstrap/wwwroot`. To bump them, replace those files and regenerate `BsIconName.g.cs` from
the icon font CSS (see the comment at the top of that file).
