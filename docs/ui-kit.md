# The UI kit (`Rask.Ui`)

**Every daisyUI component, as a typed Rask component.** The kit wraps
[daisyUI](https://daisyui.com) 5 — vendored, compiled at the kit's own build, and shipped inside the
package — so a Rask app gets the whole component vocabulary without a single utility string, an npm
install, or a Tailwind configuration of its own.

It is **markup and nothing else**: no data access, no host dependency, and no JavaScript. It runs on
the ASP.NET host and in browser-WebAssembly, which is the one place it differs from `Rask.Dashboard` —
the console is deliberately server-only because its panels read a `DbContext`.

```bash
dotnet add package Rask.Ui
```

Live, on this site: [Actions](/docs/ui/actions) · [Data display](/docs/ui/data-display) ·
[Navigation](/docs/ui/navigation) · [Feedback](/docs/ui/feedback) ·
[Data input](/docs/ui/data-input) · [Layout & mockups](/docs/ui/layout).

## Wiring it up

> **Every project `rask new` creates arrives wired this way already** — the two properties, the two
> links in the right order, and the theme scope. This section is for an app that predates it, or one
> that was not scaffolded.

Two things, and forgetting either produces a page that renders structurally correct components with
**no colour at all** — so both are worth doing before anything else.

**1. Link the stylesheet.** Opt into the build writing it, then link it:

```xml
<PropertyGroup>
  <RaskUiWriteStylesheet>true</RaskUiWriteStylesheet>
</PropertyGroup>
```

```csharp
protected override Component? HeadAssets =>
[
    Link.Rel("stylesheet").Href(UiStylesheet.Href(LiveOptions.PathBase)),   // the kit's, FIRST
    Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/css/app.css"),
];
```

`UiStylesheet.Href()` carries a content hash, so the file can be cached hard and still change when the
kit does. A library that renders kit components into somebody else's host wants no file in a `wwwroot`
it does not own; that case keeps `UiStylesheet.Css` and inlines it in a `<style>`, which is what
`Rask.Dashboard` does.

**2. Turn the theme on.** Nothing in the kit has a colour until an ancestor carries the theme scope:

```csharp
protected override Component Shell(Component head, Component body) =>
    Html.Lang("en").Attributes((UiStylesheet.ThemeScopeAttribute, ""))[head, body];
```

The scope exists so that *referencing* this package cannot repaint an application that only wanted a
button. daisyUI paints `:root` by default; the kit confines it to `[data-rask-ui]` instead, and the
same reasoning is why it ships no preflight.

**Order is the contract.** The kit's sheet declares the palette, so redefining a token in your own
`@theme` re-skins every component without overriding a single rule — which only works while your copy
is what the cascade reads last.

**The kit's sheet is linked first because it declares the layer order for the whole document.** A
browser orders `@layer` names by *first appearance*, across every sheet on the page, and nothing later
can reorder a name that has already been placed — so whichever sheet loads first decides the ranking
every other sheet is judged by. The kit's opens with

```css
@layer properties, theme, base, components, daisyui, rask, utilities;
```

which puts your utilities above your own Tailwind preflight, above daisyUI, and above the kit's own
corrections. Link it second and that statement arrives too late: the order falls out of whatever the
sheets happen to mention first, which is how `base` once ended up outranking `utilities` for a whole
site — every `text-4xl` and `px-*` in the markup, present and correct, and silently beaten by
preflight's `h1 { font-size: inherit }` and `* { padding: 0 }`. `UiLayerOrderTests` holds the order in
the compiled sheet.

> **CSS layers do not merge across `<link>` elements.** If you find a kit rule beating one of your
> utilities, or the reverse, that is why — and it is not something either sheet's source order can
> settle.

## Writing daisyUI class names yourself

The sheet above carries the classes **the kit's own components** write, because Tailwind emits a class
only where it can see the name — and these names live in a compiled assembly your Tailwind cannot scan.
So `UiCard` is styled by it and a `card-body` you write in your own markup is not: a correct-looking
class naming a rule that exists nowhere.

To write daisyUI directly, compile it yourself. The kit ships the plugin bundle for exactly this, and a
third opt-in copies it beside your stylesheet:

```xml
<RaskUiWriteDaisyUiPlugin>true</RaskUiWriteDaisyUiPlugin>
```

```css
@layer properties, theme, base, components, daisyui, utilities;

@import "tailwindcss";

@source not "./vendor";
@plugin "./vendor/daisyui.mjs";
```

By relative path because Tailwind resolves a plugin the way Node does, and the standalone engine a C#
host compiles with carries no package tree — so there is still no npm and no `node_modules`.
`@source not` matters as much: the bundle names every class daisyUI defines, and scanned it is a
safelist for the whole library.

**An app that does both carries two copies of daisyUI** — yours at `:root`, the kit's confined to
`[data-rask-ui]`. They do not conflict, because the kit's is scoped and layered, but the page carries
both. Reference the kit for its components, take the plugin for your own markup, and take both when you
want both.

## Themes

daisyUI's 35 themes all ship, as the `UiThemeName` enum. Light is the default and dark follows the
operating system — a scope with **no** `data-theme` matches `[data-rask-ui]:not([data-theme])`, which
daisyUI compiles under `prefers-color-scheme: dark`. To pin one, put `data-theme` on the element
carrying the theme scope — or on any container, to re-theme just that subtree.

`UiShell` carries the scope itself, so it names its own theme:

```csharp
UiShell.Theme(UiThemeName.Light)[ /* … */ ]
```

Leave it off and that subtree follows the OS. Writing `data-theme` on an ancestor does **not** settle
it, because the rule that follows the OS is `[data-rask-ui]:not([data-theme])` and it matches the
shell's own element — which is how a surface with its own fixed palette can render its chrome dark and
its content light on the same screen. `Rask.Dashboard` pins `Light` for exactly that reason; see
[the dashboard](dashboard.md).

```csharp
UiThemeController.Label("Dark").Theme(UiThemeName.Dark).Active(_theme is UiThemeName.Dark)
                 .OnChange(theme => _theme = theme)
```

The control **reports** a choice and cannot apply it: the palette is set by an ancestor, and no
component can write an attribute onto something above it. The page holds the value and writes
`UiTheme.Value(theme)` there — which is also what lets it be persisted, something daisyUI's CSS-only
`theme-controller` could not offer, since nothing in C# knew which theme was showing.

`UiThemePicker` and `UiThemeDropdown` are ready-made pickers over the whole set, and both offer
**System** as their first entry — `UiThemeName.System`, whose value is `UiTheme.SystemValue`. It is not
a palette: it means the absence of a choice, so selecting it **removes** `data-theme` and lets
`prefers-color-scheme` decide again. Turn it off with `.ShowSystem(false)`, rename it with
`.SystemLabel("Automatic")`.

Never stamp `data-theme="system"`. daisyUI compiles no block for it, so it matches nothing and leaves
every `--color-base-*` undefined on the element your document inherits from — a fully laid-out page
with no colour in it, and nothing reports why.

### Remembering the choice

`UiThemeScript` is the other half of the picker. Put it in your root component's head assets, before
the stylesheets:

```csharp
protected override Component? HeadAssets => [Title["…"], UiThemeScript, /* stylesheets */];
```

It applies the stored palette **before the first paint** (so a saved dark theme never flashes light),
re-applies it after every morph (a full-document morph strips attributes off `<html>`), ticks the
reader's radio back on, and exposes `window.raskSetTheme(value)` / `window.raskTheme()`.

With nothing stored it writes **no `data-theme` at all**, which is what makes the page follow the
operating system — in CSS, with nothing running, and repainting if the reader flips their OS while the
page is open. A stored value is checked against the themes that exist (built from `UiTheme.All`), so a
hand-edited `localStorage` entry means "no choice" rather than an uncoloured page.

It carries **no C# event handlers**, deliberately. Handler ids are positional, so one handler in the
chrome of every page shifts every id after it — and an island captures its callback id from the
prerendered markup, so moving the ids breaks its clicks silently on a page that still looks alive.

### Reading the palette

Every token is readable in every theme, and that is measured rather than intended — see
`ThemeContrastTests`, which resolves every pair out of the shipped stylesheets across all 36 palettes
and holds them to WCAG AA (4.5:1).

Three tiers, and picking the wrong one is the classic silent defect:

| Tier | Example | Use it for |
|---|---|---|
| surface | `bg-ui-brand`, `bg-ui-warn` | a fill that carries no text of its own |
| `-surface` | `bg-ui-ok-surface` | the quiet wash behind a badge or alert |
| `-ink` | `text-ui-danger-ink` | **any text**, and any fill that carries text |

daisyUI's `primary`/`success`/`warning`/`error`/`neutral` are **surfaces**. Read as text they fail AA
badly — `--color-neutral` is 1.26:1 on daisyUI's own `dark`, `--color-error` 2.87:1 on its `light` —
so the `-ink` tiers exist, each mixed toward `--color-base-content` (the ground's opposite in every
palette, so one declaration darkens on a light theme and lightens on a dark one; mixing toward `black`
is the trap, because it is only the right direction on half the palettes).

A **filled control is `bg-ui-*-ink text-ui-bg`**, never a saturated fill with a white label:
`bg-ui-brand text-white` measures 1.00:1 on `luxury`. Contrast is symmetric, so the ground read on an
`-ink` fill is the same proven measurement as `-ink` read on the ground. daisyUI's own `-content`
colours are generated for 3:1, not 4.5.

### The kit's own components are corrected the same way

`UiButton`, `UiBadge`, `UiAlert`, `UiTooltip` and the `link-*` tones render daisyUI classes, and
daisyUI labels each tone with its own `-content` colour — generated for 3:1, so small text on them fails
AA on between two and ten palettes per tone (`secondary` is 3.05:1 on daisyUI's own `dark`, `error`
under AA on ten). The kit corrects them to the `-ink` fill with the ground as the label, in
`@layer rask-ui-corrections`, pointing at the tokens above so the per-theme corrections apply for free.

Two consequences worth knowing:

- **It is all custom properties** (`--btn-color`, `--badge-fg`, `--tt-bg`) except where daisyUI declares
  `color` outright — alert, link, tooltip content. Those three therefore also outrank your own `text-*`
  utility on those elements, because the corrections layer is appended after `utilities`.
- **`checkbox-*`, `radio-*`, `toggle-*`, `range-*` and `progress-*` are deliberately untouched** — they
  carry no text, so WCAG asks 3:1 of them as non-text UI, and recolouring them would be a redesign.
  `step-*` carries a label and is *not* yet corrected: daisyUI sets its variables on compound
  pseudo-element selectors an inherited custom property cannot reach.

If you write your own daisyUI classes (see above), you are on daisyUI's `-content` pairs, not these —
the corrections name the classes the kit writes.

## The three axes

Colour, fill and size are independent and compose, so an outlined error button needs no member of its
own:

```csharp
UiButton.Tone(UiTone.Error).Variant(UiVariant.Outline).Size(UiSize.Lg)["Delete"]
```

| Enum | Members |
| --- | --- |
| `UiTone` | `Neutral` `Primary` `Secondary` `Accent` `Info` `Success` `Warning` `Error` |
| `UiVariant` | `Solid` `Outline` `Soft` `Dash` `Ghost` `Link` |
| `UiSize` | `Default` `Xs` `Sm` `Md` `Lg` `Xl` |

These are daisyUI's own words, deliberately. Translating them into a private vocabulary was the first
thing this kit did and the first thing it stopped doing: daisyUI's documentation is the documentation
for everything the components render, and a second set of words made every example a translation.

Not every component honours every member — daisyUI defines no `input-outline`, and no `tooltip-neutral`
— and **a member a component has no class for writes nothing**, rather than a class that would sit in
the markup looking as though it styled something.

Other axes follow the same rule: `UiPlacement`, `UiModalPlacement`, `UiMaskShape`, `UiLoadingShape`,
`UiSwapAnimation`, `UiAuraStyle`, `UiTabStyle`, `UiMarker`, `UiOpenOn`.

## Components that are one element

A button is a `<button>`, and a table is a `<table>`. `UiButton`, `UiBadge`, `UiAlert`, `UiTable` and
`UiList` do not wrap a raw element; they are the element. They derive from **`UiElement`**, which derives
from `Element`, so every step an element takes works on them unchanged, the events included. What they
show is their **children**, the same as a raw element's:

```csharp
UiButton.Id("save").Tone(UiTone.Primary).OnClick(SaveAsync)[UiIcon.Name(UiIconName.Check), "Save"]

UiBadge.Tone(UiTone.Success)["Live"]

UiAlert.Tone(UiTone.Error)[UiIcon.Name(UiIconName.Warning), Span["Payment failed: "], Code[error]]

UiTable.Id("orders").Data("testid", "orders").Aria(("label", "Orders"))[
    Thead[Tr[Th["Order"], Th["Total"]]],
    Tbody[rows]
]
```

A bare `UiIcon.Name(…)` is the right size in all of these. The kit's stylesheet sizes an icon nobody sized
from the button or badge it sits in, and leaves alone an icon that has a size class of its own.

A square or circle button holds one glyph, so it names itself with **`AccessibleLabel`**:
`UiButton.AccessibleLabel("Close").Square(true)[UiIcon.Name(UiIconName.Close)]`.

The kit's classes and ARIA compose with yours instead of replacing them. `.Class("mb-0")` is added to the
kit's classes through `ResolveClass()`. A label you set with `.Aria(…)` wins over one the kit would derive
through `ResolveAria()`, which writes into Core's `aria-*` slot so the attribute order stays the one
`Element` documents.

**One tag, and that has a consequence.** An element's children are written straight from the indexer, so
an element-derived component cannot draw anything around them. The two places this shows:

- **`UiTable.Scroll(true)`** puts the table in a bordered box that scrolls sideways. While it does,
  `UiTable` renders as the box with the `<table>` inside. The id, classes, data, ARIA and handlers stay on
  the `<table>`, so `#orders tbody tr` finds the same rows either way.
- **`UiList.Ordered(true)`** is an `<ol>`, numbered. Use it when the order means something, such as a log
  or a set of steps. A row is a plain `Li`.

The kit pads cells and rows with a stylesheet rule on its `ui-table` / `ui-list` marker, in the layer
below your utilities. A `px-0` on a cell therefore gets flush content. A `[&_td]:px-3` variant would have
out-specified it.

## What is in it

Grouped as daisyUI groups them, so its documentation reads straight across.

| | |
| --- | --- |
| **Actions** | `UiButton` `UiDropdown` `UiModal` `UiSwap` `UiThemeController` `UiFab` |
| **Data display** | `UiAccordion` `UiAccordionSection` `UiCollapse` `UiAvatar` `UiAura` `UiBadge` `UiCard` `UiCarousel` `UiChatBubble` `UiCountdown` `UiDiff` `UiHover3d` `UiHoverGallery` `UiKbd` `UiList` `UiListRow` `UiStat` `UiStatusDot` `UiTable` `UiDataGrid` `UiColumn` `UiTextRotate` `UiTimeline` |
| **Navigation** | `UiBreadcrumbs` `UiDock` `UiLink` `UiMegamenu` `UiMegamenuPanel` `UiMenu` `UiMenuItem` `UiNavbar` `UiPagination` `UiSteps` `UiStep` `UiTabs` `UiTab` |
| **Feedback** | `UiAlert` `UiLoading` `UiProgress` `UiRadialProgress` `UiSkeleton` `UiToast` `UiTooltip` |
| **Data input** | `UiInput` `UiTextarea` `UiSelect` `UiMultiSelect` `UiFileInput` `UiCheckbox` `UiToggle` `UiRadio` `UiRange` `UiRating` `UiFieldset` `UiValidator` `UiLabel` `UiOtp` `UiFilter` `UiCalendar` |
| **Layout** | `UiDivider` `UiDrawer` `UiFooter` `UiHero` `UiIndicator` `UiJoin` `UiStack` `UiMask` |
| **Mockup** | `UiMockupBrowser` `UiMockupCode` `UiMockupPhone` `UiMockupWindow` |
| **Chrome** | `UiShell` `UiTopBar` `UiBrand` `UiNav` `UiNavTab` `UiCrumbSwitcher` `UiCrumbSeparator` `UiTopLink` `UiMain` `UiHeader` `UiGrid` `UiNotice` `UiMetricRow` `UiMetric` `UiDetailList` `UiDetailRow` `UiCode` `UiSearch` |
| **Support** | `UiIcon` / `UiIconName`, `UiTheme` / `UiThemeName`, `UiStyles`, `UiStylesheet` |

## Who owns the state

The kit ships no JavaScript, and that constraint decides the shape of every interactive component. It
resolves three ways, and which one a component takes is a property of what the platform can do rather
than of anyone's preference.

**The browser owns it, declaratively.** `UiModal` with an `Id` and a `Trigger` is a real
`<dialog popover>`: the browser supplies the top layer, Escape, light-dismiss and a native
`::backdrop`. `UiMegamenu` is built the same way. `UiFab` opens on `:focus-within` because daisyUI
defines no class to force it. All of these work on a prerendered page with no runtime booted, and with
scripting off entirely.

```csharp
UiModal.Title("Shortcuts").Id("shortcuts").Trigger("Show shortcuts")[ … ]
```

**The page owns it, in C#.** `UiDropdown`, `UiCollapse`, `UiAccordion`, `UiSwap`, `UiTabs` and
`UiModal`'s `Open` path hold their state in a field and redraw through the live diff — which is what
lets a dropdown close itself when the action inside it completes.

```csharp
UiDropdown.Trigger("Actions").Open(_open).OnToggle(open => _open = open)[ … ]
```

`Open` is nullable and the three settings mean three things: unset is uncontrolled and the browser
decides; `true` and `false` hand it to the page. Closed writes `dropdown-close` rather than merely
omitting `dropdown-open`, because daisyUI also opens on `:focus-within` — without it, tabbing into
the panel would re-open a dropdown the page had just closed.

**The markup owns it.** `UiTab` is a real link with a real URL, so a tab is bookmarkable, survives a
refresh and answers the back button. `UiDrawer` keeps its checkbox because daisyUI's rules are written
against `.drawer-toggle:checked`; C# sets it and hears it change, but the input is the component.

**And one that lets you choose.** `UiSelect` is the platform's `<select>` by default and draws its own
list when `Native` is `false` — a `[popover]` `role="listbox"` under a `role="combobox"` box, with the
arrow keys, Home/End, Enter, and a roving `aria-activedescendant` cursor that skips unavailable
options. Reach for it when the list must carry more than the platform will show, or must escape an
`overflow: hidden` ancestor. Both modes take the same properties and mean the same thing by them; what
differs is that the drawn list **needs the runtime**, where the native control works on a prerendered
page and with scripting off. That is why the default is native.

**And one that lets you choose several.** `UiMultiSelect` is the same control for a field that holds a
collection. Native is a real `<select multiple>`; `Native: false` draws the list, shows the chosen
answers as removable chips in the box, and — unlike the single-select — leaves the list OPEN as you
pick, because choosing three answers should not mean opening it three times. `SelectAll` adds a bulk
row, `Filter` adds a search box (you supply the predicate, so it works for any `T`), and `Chips` caps
how many chips the box shows before the rest collapse into "+N more". It binds the `List<T>`, `T[]` or
`HashSet<T>` your model already declares, refilling a get-only collection in place; the write-back
builds whatever the property declares. A field typed `IReadOnlyList<T>` is the one shape that cannot
bind — it is not an `ICollection<T>`, so the chain has nothing to infer from.

Both controls take an `OptionTemplate` for rows that need more than words. Setting one implies the
drawn list, because an `<option>` holds text and nothing else — writing `Native(true)` beside a
template is [RASK075](diagnostics.md#rask075).

The browser still owns dismissal there — Escape and click-outside — and C# hears it through
`OnToggle`, which is what keeps `aria-expanded` truthful rather than drifting the moment the list is
dismissed.

## Form controls

**All twelve** of the kit's data-input controls implement `IFormControl<T>`, so each works in the two
shapes every Rask input does:

```csharp
Form.Model(_order)[
    UiSelect.Bind(() => _order.Country).Options(countries).Label("Country"),
    UiSelect.Value(_country).Options(countries).Label("Country").OnChange(v => _country = v),
    UiMultiSelect.Bind(() => _order.Tags).Options(tags).Label("Tags")
]
```

**A labelled text field floats its label.** `UiInput`, `UiTextarea` and a native `UiSelect` draw `Label`
as daisyUI's `floating-label`: the caption sits in the field until there is content, then rises out of the
way. It is still the field's real `<label>`, linked to the control. `Floating(false)` puts it back above
the field as a legend. Controls with no text to float over keep the legend: checkboxes, ranges, ratings,
and a `UiSelect` that draws its own list.

```csharp
UiInput.Bind(() => _account.Email).Label("Email")                   // floats
UiInput.Bind(() => _account.Seats).Label("Seats").Floating(false)   // legend above the field
```

A floating caption rises from a *shown* placeholder, so the placeholder falls back to the label text when
you give none.

**The opening step fixes the type argument and the mode together.** `Bind` opens a bound control and
`Value` a controlled one; they are mutually exclusive because a control with both would have two
sources of truth for one field, and the compiler enforces it — both live on the control's entry, so
taking one leaves the other unreachable. `Label`, `Options` and the rest follow in any order, since none
of them says anything about `T`. Bound mode drives the surrounding `Form`'s validation — per-field
`Validate`, `AfterBind`, and the `aria-invalid`/`aria-describedby` display — and controlled mode leaves
the value with the parent. See [building form controls](building-form-controls.md).

**Generic where the value type varies, concrete where it does not.** `UiInput<T>`, `UiTextarea<T>`,
`UiSelect<T>` and `UiFilter<T>` are generic — the model decides what they hold, and `UiInput` even
takes its `type` attribute from `T`, so a bound `int` is a number field with nothing said at the call
site. The rest are closed over the one type they can have: `UiCheckbox`, `UiToggle` and `UiRadio` over
`bool`, `UiRange` over `double`, `UiRating` over `int`, `UiOtp` and `UiFileInput` over `string`,
`UiCalendar` over `DateOnly`. A checkbox's value is a `bool` and nothing else; a type parameter there
would have exactly one legal argument.

| | Binds |
|---|---|
| `UiInput<T>` `UiTextarea<T>` `UiSelect<T>` | what the field holds |
| `UiMultiSelect<T>` | the ELEMENT type — it binds an `ICollection<T>` |
| `UiFilter<T>` | the chosen option of a whole radio group |
| `UiRadio` | whether **this** option is the chosen one — the group's value belongs to `UiFilter<T>` |
| `UiCheckbox` `UiToggle` | on or off |
| `UiRange` `UiRating` `UiCalendar` | the position, the star count, the day |
| `UiOtp` | the code — `OnComplete` fires on the transition into a full one, in both modes |
| `UiFileInput` | the chosen file's name, **write-only** — a browser refuses to have a file input's value set, so binding fills the model and never the box. The bytes come through `OnFiles`. |

**A field with no value yet opens on its type alone**: `UiInput.Of<string>().Label("Search")`. A form
control's openings are its mode pins, so a required step like `Label` never gets to pin `T` — without
`Of` a controlled field with nothing in it would have to invent a value to compile. `Of` is the
controlled mode: the parent still owns whatever the field ends up with.

`UiCalendar` is the one to read twice. `Month` and `OnMonth` are the **view**, not the value — paging
through months changes nothing a form would submit, which is why they sit outside the binding.

## The rule the whole kit rests on

daisyUI emits a component's CSS only where Tailwind can **see** its class name in the scanned source.
A name built by concatenation — `"btn-" + tone` — is a name no scanner ever reads, so the class is
absent from the compiled sheet and the component renders **with no styling whatsoever**. Not
misaligned, not the wrong colour: unstyled. The build stays green and the markup carries exactly the
class the call site asked for.

That is why every class the kit can write is spelled out as a complete literal in `UiClassNames.cs`,
why the axes above are closed enums rather than strings, and why `UiTextRotate` has no `Interval`
property — daisyUI reads its speed from a `duration-*` utility, and turning a `TimeSpan` into a class
name at run time is exactly the failure this rule prevents.

If you write your own `ui-*` classes, the same applies to you: copy the kit's `@theme` block into your
own stylesheet, because Tailwind emits a utility only where it can see the token.

## Two rules it holds itself to

**Mobile-first, which is a different claim from responsive.** Every control takes a 44px touch target
below `sm`. A dialog is a bottom sheet on a phone and a centred card above it, because a centred
dialog at 360px either overflows or shrinks its content past reading. The tab bar scrolls sideways
rather than wrapping, so the header is exactly one row tall however many tabs there are.

**Every control has a name.** A label is required, not optional, and it becomes the accessible name
rather than a placeholder — a placeholder disappears the moment typing starts, so the one thing saying
what a field is for vanishes exactly when a reader might check it. An icon-only button names itself with
`AccessibleLabel`, written as `aria-label`; a spinner is `aria-hidden` with its words beside it; a failed
toast changes its **icon** and not only its colour.

## Names

Every component carries the `Ui` prefix, and that is load-bearing rather than decorative. Inside a
markup host a bare `X` is the chain's `Build<X>` entry, so a component called `Shell` would collide
with `Component.Shell`, and `Nav`/`Main`/`Button`/`Select`/`Search` with the HTML tags. On a
collision [RASK040](diagnostics.md#rask040) gives **neither** type an entry, across the whole
compilation.

The same rule applies to the namespace. `Rask.Ui` is an enclosing namespace of every `Rask.*`
compilation, so a type of your own named `Ui` inside a `Rask.`-rooted namespace will be shadowed by it
— C# resolves a simple name against enclosing namespaces before it looks at imports.

Each component lives in a file named after it, so the file list in `src/Rask.Ui` is the component list.

## See also

- [Dashboard](dashboard.md) — the operator console this kit was extracted from
- [Tailwind](tailwind.md) — how the compiler is wired into a Rask build
- [Data grid](data-grid.md) — `UiDataGrid`, whose columns arrive through a factory and whose row key is required
- [Building components](building-components.md) — the chain the kit is composed with
