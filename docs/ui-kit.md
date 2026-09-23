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

## Principles

The kit's behaviour follows the practices [Flux UI](https://fluxui.dev) set out for Livewire, adapted to a
C# component framework that ships no script of its own:

- **Use the browser.** A dialog is a modal `<dialog>` opened by an invoker command; a menu, a listbox and a
  megamenu are `[popover]`s; a sidebar is a checkbox drawer. The top layer, Escape, light-dismiss and focus
  return are the platform's, and they work before any runtime has booted.
- **Use CSS.** The submenu's safe triangle is a clipped wedge, the scroll lock under a dialog is a `:has()`
  rule, a button's spinner is a `[data-loading]` rule. Where something truly needs script — pressing a menu row,
  marking a button that is waiting on its handler — the framework runtime does it, generically, for every
  control, not the kit.
- **Accessible by default.** Every field describes itself (`aria-describedby`, `aria-invalid`, `aria-required`),
  menus carry a keyboard cursor, the current navigation item says `aria-current="page"`, a waiting button says
  `aria-busy` — none of it opt-in.
- **One vocabulary.** `Position` + `Align` place everything that floats, events are `On…`, `Kbd` shows a shortcut
  wherever one is shown, `Tone`/`Variant`/`Size` style everything.
- **We style, you space.** Components bring padding, borders and colour — never an outer margin.
- **Simple first, composable after.** `UiInput.Label("Email").Hint(…)` is one line; `UiNavList` with
  `UiNavGroup`s and `UiNavItem`s, or `UiDropdown` with `UiMenuSub`s, is there when one line is not enough.

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
`Rask.Dashboard` does — and it is the only stylesheet the console carries, reset included.

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
its content light on the same screen. `Rask.Dashboard` pins `Light` on both its `<html>` and its shell for
exactly that reason; see [the dashboard](dashboard.md).

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
UiButton.Error.Outline.Lg["Delete"]
```

**Every member is a step of its own**, so a value reads as a word rather than as an argument. The
setter is still there for a value the source does not know:

```csharp
UiButton.Tone(order.IsUrgent ? UiTone.Error : UiTone.Neutral)["Ship"]
```

| Enum | Members, each a step |
| --- | --- |
| `UiTone` | `Neutral` `Primary` `Secondary` `Accent` `Info` `Success` `Warning` `Error` |
| `UiVariant` | `Solid` `Outline` `Soft` `Dash` `Ghost` `Link` |
| `UiSize` | `Default` `Xs` `Sm` `Md` `Lg` `Xl` |

The steps are generated, not written, and four kinds of enum deliberately get none — in each case
because the step would read as a claim about the component rather than about one of its properties:

| No steps when | Because |
| --- | --- |
| the enum has more than 8 members | `UiIconName` has 78; `UiButton.ChevronRight` says the button **is** a chevron |
| one component has two properties of it | an `Icon` and a `TrailingIcon` have no answer to which `.Search` would set |
| it is a `[Flags]` enum | `.Top.Bottom` reads as two steps that each *replace* the other, since a step assigns |
| it is one of the BCL's | `UiDatePicker.Sunday` says the picker is Sunday, not that its week starts there |

A member whose name the component already uses is skipped too — `UiCard` has a `Default` property, so
`UiSize.Default` stays an argument there and the rest of the size axis is unaffected.

These are daisyUI's own words, deliberately. Translating them into a private vocabulary was the first
thing this kit did and the first thing it stopped doing: daisyUI's documentation is the documentation
for everything the components render, and a second set of words made every example a translation.

Not every component honours every member — daisyUI defines no `input-outline`, and no `tooltip-neutral`
— and **a member a component has no class for writes nothing**, rather than a class that would sit in
the markup looking as though it styled something.

Other axes follow the same rule: `UiPosition`, `UiAlign`, `UiModalPosition`, `UiMaskShape`,
`UiLoadingShape`, `UiSwapAnimation`, `UiAuraStyle`, `UiTabStyle`, `UiMarker`, `UiOpenOn`.

### One vocabulary for placing things

Everything that floats against something else is placed with the same two words, the ones Flux UI uses:
**`Position`** picks the side (`UiPosition` — Top, Right, Bottom, Left) and **`Align`** slides it along that
side (`UiAlign` — Start, Center, End, following the reading direction). They are two properties because
daisyUI composes them — a menu above its trigger, flush with the trigger's end edge, is both.

```csharp
UiDropdown.Trigger("Actions").Position(UiPosition.Top).Align(UiAlign.End)[ … ]
UiTooltip.Tip("Copy").Position(UiPosition.Right)[ … ]
UiTabs.Position(UiPosition.Bottom)[ … ]
UiDrawer.Id("nav").Panel(menu).Position(UiPosition.Right)[ … ]
UiModal.Title("Details").Position(UiModalPosition.End)[ … ]   // placed against the viewport, not a trigger
```

Events are always `On…` — `UiModal.OnClose`, `UiModal.OnCancel`, `UiToast.OnDismiss` — the same prefix every
element event carries. A `<dialog>`'s own endings are element events too: `Dialog.OnCancel` for a dismissal and
`Dialog.OnClose` for any close.

### We style, you space

A kit component brings its padding, its border and its colours, and **never an outer margin**. Where it
sits — the gap above a row of tabs, the bleed of a scrolling strip to the screen edge — belongs to the
page that places it, because the same component sits in a card, a toolbar and a page gutter, and a margin
right for one is wrong for the other two. Two exceptions are part of a component's shape rather than its
placement: `UiNavTab`'s `-mb-px`, which joins the active tab's border to its nav's hairline, and
`UiToast`'s `mx-auto`, which centres a fixed overlay in the viewport.

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

## Buttons and links that go somewhere

Every kit component that goes somewhere takes a `RouteUrl`: `UiButton.Href`, `UiLink.Href`,
`UiCard.Href`, `UiStat.Href`, `UiNavTab.Href` and `UiBrand.Href`. All of them follow one rule. Hand one a
**generated route** and it navigates inside the app, the way `NavLink` does. The anchor carries
`data-rask-nav`, which the runtime intercepts and routes without reloading the page. It also carries the
deploy's path base, so a new tab or a copied link reaches the same page. Hand one a **string** and it
is an ordinary link the browser follows itself, written exactly as given. That is what a URL that
leaves the app wants.

```csharp
UiButton.Tone(UiTone.Primary).Href(Routes.CreateProduct())["New product"]    // stays in the app
UiLink.Href(Routes.ProductsPage()).Text("Back to the list")                   // stays in the app
UiButton.Href("https://github.com/pal-tamas/rask").NewTab(true)["GitHub"]     // leaves it
```

A string that happens to name one of your own pages is still a string: it reloads the whole app to get
there. Use the route. `NewTab(true)` is never intercepted, because the reader asked for another tab.

## Application layout

Flux UI's layout pieces, drawn with daisyUI. The sidebar beside the docs on this site is exactly this.

```csharp
UiSidebar.Id("app-nav").Collapsible(UiBreakpoint.Lg).Page(Main[Outlet])[
    UiBrand.Label("Shop").Href(Routes.HomePage()),
    UiNavList.AccessibleLabel("Main")[
        UiNavItem.Label("Orders").Href(Routes.OrdersPage()).Icon(UiIconName.Book).Badge("12"),
        UiNavGroup.Heading("Catalogue").Expandable(true)[
            UiNavItem.Label("Products").Href(Routes.ProductsPage()),
            UiNavItem.Label("Categories").Href(Routes.CategoriesPage())
        ]
    ],
    UiSpacer.Key("spacer"),
    UiNavList.AccessibleLabel("Account")[UiNavItem.Label("Settings").Href(Routes.SettingsPage())]
]

// in the top bar, shown only while the sidebar is collapsed:
UiSidebarToggle.For("app-nav").Collapsible(UiBreakpoint.Lg)
```

- **`UiSidebar`** is an `<aside>` beside `Page`: docked — sticky, full height — from `Collapsible` up, and a
  drawer below it that `UiSidebarToggle` slides in and a click beside it slides out. The open state is daisyUI's
  checkbox, so it opens on a prerendered page with no runtime; `Open`/`OnToggle` mirror it into C#, which is how a
  navigation closes it. `UiSidebarToggle` is a `<label>` for that checkbox with `role="button"` and a tab stop, and
  the runtime presses it on Enter and Space.
- **`UiNavList`** is a named `<nav>` around daisyUI's `menu`. **`UiNavItem`** is a `NavLink` underneath, so
  **`Current` is worked out from the route** — `menu-active` and `aria-current="page"` — unless you state it;
  `Match` + `MatchPrefix` keep an item current across a section. **`UiNavGroup`** is a heading over its items, or a
  `<details>` disclosure with `Expandable`, controlled with `Expanded`/`OnToggle`.
- **`UiSidebarHeader`** and **`UiSidebarFooter`** hold their place while the navigation between them scrolls —
  Flux's `sidebar.header` and `sidebar.footer`. The footer needs no `UiSpacer` in front of it: it pins itself, so
  a nav list long enough to scroll scrolls *between* the two rather than pushing the account row off the bottom.
- **`UiProfile`** is that account row: an avatar, a name, an optional caption, and — given children — the button
  that opens the account menu, with the same keyboard contract `UiDropdown` has, because both are
  **`UiMenuButton`** underneath. Without an `Avatar` it draws the **initials** of `Name`, since most accounts have
  no picture and a broken image is worse than a monogram. Its menu opens upward by default, because the row sits
  at the bottom of the sidebar.
- **A docked sidebar can narrow to a rail of icons**, which is a different question from `Collapsible`:
  `Collapsible` says at what width the sidebar stops being beside the page at all, `Collapsable(true)` keeps it
  beside the page and takes the words away. **`UiSidebarCollapse`** is the control, a `<label>` for a second
  checkbox — so it needs no runtime either — and it appears exactly where `UiSidebarToggle` disappears.
  The words that go are marked `ui-rail-hide` by the components that own them, so a CSS rule never has to
  guess which text is a label and which is content, and each link, the brand and the profile row keep their
  name as a `title` — the rail's tooltip, and the accessible name of a link that is only an icon now. (A drawn
  tooltip would be cut off: the panel clips its overflow.)

  `Collapsed`/`OnCollapse` hand the choice to C#, and remembering it is the app's: the kit stores nothing on
  your behalf. Read it once from `IBrowserStorage` after the first render and write it back as it changes:

  ```csharp
  public sealed partial class AppShell(IBrowserStorage storage) : Component
  {
      private bool _rail;

      // After the first render, because storage lives in the browser. The hook repaints when it completes.
      protected override async Task OnFirstRendered() =>
          _rail = await storage.Local.GetAsync("sidebar-rail") == "1";

      protected override Component? Render() =>
          UiSidebar.Id("nav").Page(UiMain[Children ?? []]).Collapsible(UiBreakpoint.Lg).Collapsable(true)
              .Collapsed(_rail)
              .OnCollapse(async rail =>
              {
                  _rail = rail;
                  await storage.Local.SetAsync("sidebar-rail", rail ? "1" : "0");
              })[ … ];
  }
  ```

  The first paint is the open sidebar and a remembered rail follows a frame later; a page that must not flicker
  keeps the choice in a cookie instead and reads it on the server.
- **`UiSpacer`** is `flex: 1`: it pushes what follows it to the far end of a row or a column.
- **`UiDivider`** is Flux's separator: `Vertical`, `Subtle`, and `Align(UiAlign.Start|End)` for its words, a
  `separator` to assistive tech when it has none, and **no outer margin** — daisyUI's 1rem is zeroed, so the page
  spaces it.
- **`UiHeading`** separates how big a heading looks (`Size`) from where it sits in the outline (`Level` 1–6, a
  `<div>` without one); **`UiSubheading`** and **`UiText`** (`Strong`, `Subtle`, `Tone`, `Inline`) are the rest of the
  type scale. `UiHeader` and `UiCard` take a `HeadingLevel` instead of a fixed `<h1>`/`<h2>`.

## Buttons that wait

A button whose handler is still running shows it — with nothing to set. Press "Save" on a slow link and,
once the handler has gone 200 ms without finishing, the button swaps its label for a spinner at the same
width, carries `aria-busy="true"`, and drops a second press until the first one's render has landed. This
is Flux UI's answer to the double submit, and it holds on both hosts: the Server runtime ends the wait on
the handler's ack, the WebAssembly runtime when its dispatch returns.

```csharp
UiButton.Tone(UiTone.Primary).OnClick(SaveAsync)["Save"]          // waits automatically
UiButton.Loading(false).OnClick(StepAsync)[UiIcon.Name(UiIconName.Plus)]  // a stepper: presses queue
UiButton.Loading(_exporting)["Export"]                            // work that outlives the handler
```

It is the **runtime** that marks the button, not script in the kit, because only the runtime knows when a
dispatch starts and ends. So every `<button>` with a handler gets the same `data-loading` + `aria-busy`
attributes — the kit's stylesheet is what turns them into a spinner, and your own CSS can style
`[data-loading]` on any control. `data-rask-loading="off"` on an element, or on a toolbar around several,
opts them out; `data-rask-loading` on a non-button element opts it in. It is never `disabled`, which would
throw keyboard focus off the control mid-press. A Blazor island's buttons get it too — their handlers
dispatch over the same channel.

## What is in it

Grouped as daisyUI groups them, so its documentation reads straight across.

| | |
| --- | --- |
| **Actions** | `UiButton` `UiDropdown` `UiContextMenu` `UiCommand` `UiPopover` `UiModal` `UiSwap` `UiThemeController` `UiFab` |
| **Data display** | `UiAccordion` `UiAccordionSection` `UiCollapse` `UiAvatar` `UiAura` `UiBadge` `UiCard` `UiCarousel` `UiChatBubble` `UiCountdown` `UiDiff` `UiEmpty` `UiHover3d` `UiHoverGallery` `UiKbd` `UiHighlight` `UiList` `UiListRow` `UiStat` `UiStatusDot` `UiTable` `UiDataGrid` `UiColumn` `UiTree` `UiTextRotate` `UiTimeline` `UiChart` |
| **Navigation** | `UiBreadcrumbs` `UiDock` `UiLink` `UiMegamenu` `UiMegamenuPanel` `UiMenu` `UiMenuItem` `UiNavbar` `UiPagination` `UiSteps` `UiStep` `UiTabs` `UiTab` |
| **Feedback** | `UiAlert` `UiLoading` `UiProgress` `UiRadialProgress` `UiSkeleton` `UiToast` `UiTooltip` |
| **Data input** | `UiInput` `UiTextarea` `UiSelect` `UiFileInput` `UiCheckbox` `UiToggle` `UiRadio` `UiRange` `UiRating` `UiFieldset` `UiValidator` `UiLabel` `UiOtp` `UiFilter` `UiCalendar` `UiDatePicker` |
| **Layout** | `UiDivider` `UiDrawer` `UiFooter` `UiHero` `UiIndicator` `UiJoin` `UiStack` `UiMask` |
| **Mockup** | `UiMockupBrowser` `UiMockupCode` `UiMockupPhone` `UiMockupWindow` |
| **Chrome** | `UiShell` `UiTopBar` `UiBrand` `UiNav` `UiNavTab` `UiCrumbSwitcher` `UiCrumbSeparator` `UiTopLink` `UiMain` `UiHeader` `UiGrid` `UiMetricRow` `UiMetric` `UiDetailList` `UiDetailRow` `UiCode` `UiSearch` |
| **Support** | `UiIcon` / `UiIconName`, `UiTheme` / `UiThemeName`, `UiBreakpoint`, `UiStyles`, `UiStylesheet` |

## Who owns the state

The kit ships no JavaScript, and that constraint decides the shape of every interactive component. It
resolves three ways, and which one a component takes is a property of what the platform can do rather
than of anyone's preference.

**The browser owns it, declaratively.** `UiModal` with an `Id` and a `Trigger` is a real **modal**
`<dialog>`, opened by an HTML invoker command (`command="show-modal" commandfor`): the browser supplies
the top layer, an inert page behind it so Tab cannot wander out, Escape, and focus handed back to the
trigger on close. Every open and close control also names the dialog as a `popover`, so a browser
without invoker commands (before Chrome 135, Firefox 144, Safari 26.2) opens it as a popover instead —
top layer and Escape, without the inert page. `UiMegamenu` is built on the popover the same way. `UiFab`
opens on `:focus-within` because daisyUI defines no class to force it. All of these work on a prerendered
page with no runtime booted, and with scripting off entirely.

```csharp
UiModal.Title("Shortcuts").Id("shortcuts").Trigger("Show shortcuts")[ … ]
UiModal.Title("Filters").Id("filters").Trigger("Filters").Position(UiModalPosition.End)[ … ]  // a flyout
UiModal.Title("Unsaved work").Id("edit").Dismissible(false).Escapable(false)[ … ]
```

Flux UI's switches are all here: `Dismissible(false)` ignores a click outside, `Escapable(false)` ignores
Escape (`closedby="none"`; Safari has not shipped it), `Closable(false)` drops the header's close button, and
`OnClose` hears every way it closed. `OnCancel` hears only a DISMISSAL — Escape or a click outside — and runs
before `OnClose`, so a dialog holding a draft can throw it away when the user backs out and keep it when they
press a button that closes it; the header's close button is not a dismissal. `Position(UiModalPosition.Start|End)` makes it a full-height flyout. While
any kit dialog is open the page behind it does not scroll. The state-driven `Open` path below cannot reach the
top layer, but it is not left without containment: it carries the runtime's `data-rask-focus-trap`, so focus
moves in, Tab cycles inside, Escape runs `OnCancel` then `OnClose`, and focus returns when it closes.

`UiTooltip` takes `Kbd("⌘S")` to teach a shortcut where the reader is already looking, and `Toggleable(true)`
to show on a tap — a touch screen has no hover, so an ordinary tooltip is never seen there.

**The browser owns the open state, C# owns the cursor.** `UiDropdown` is a menu button over a `[popover]`
menu: the browser opens and closes it — top layer, Escape, a click outside, focus back on the trigger — and
the menu takes focus as it opens. What C# owns is the keyboard cursor Flux UI's menus have: the arrows move an
`aria-activedescendant` cursor that skips disabled rows and wraps, Home/End jump, a letter jumps to the next
row starting with it, ArrowRight opens a submenu and ArrowLeft closes it, Enter or Space press the row, Tab
leaves. The runtime supplies the few things C# cannot: pressing the row, closing the popover after a pick, and
closing it when Tab leaves.

```csharp
UiDropdown.Trigger("View").Align(UiAlign.End)[
    UiMenuGroup.Heading("Arrange")[
        UiMenuSub.Heading("Sort by")[
            UiMenuRadioGroup.Value(_sort).Options([("name", "Name"), ("date", "Date")]).OnChange(s => _sort = s)
        ],
        UiMenuItem.Text("Refresh").Kbd("⌘R").OnClick(Refresh)
    ],
    UiMenuSeparator.Key("sep"),
    UiMenuCheckbox.Key("archived").Value(_archived).Text("Show archived").OnChange(on => _archived = on),
    UiMenuItem.Text("Delete").Tone(UiTone.Error).OnClick(Delete)
]
```

A submenu flies out beside its row, and a pointer moving diagonally toward it — across the row below — does
not close it: the flyout carries a CSS wedge back to its row and waits 300 ms before closing, Flux's **safe
triangle** with no script. A tap opens it on a touch screen. `UiMenuCheckbox` keeps the menu open, since
flipping three switches should not mean opening it three times; any row can ask for the same with `KeepOpen`.
Style the rows from `data-highlighted` (the cursor), `data-checked` and the dropdown's `data-open`.

A controlled item written as `.Value(x).Key("k")` keeps the value it had when the key first claimed it — put
`Key` first on a non-generic item (`UiMenuCheckbox.Key("k").Value(x)`), and leave a generic one such as
`UiMenuRadioGroup` unkeyed.

`Open` is nullable and the three settings mean three things: unset leaves it to the reader; `true` and `false`
hand it to the page, and the runtime shows or hides the popover to match whenever the page changes its mind,
which is what lets a dropdown close itself when the action inside it completes. `OpenOn(UiOpenOn.Hover)` keeps
daisyUI's CSS dropdown, which a pointer can open and a popover cannot — without the keyboard cursor.

```csharp
UiDropdown.Trigger("Actions").Open(_open).OnToggle(open => _open = open)[ … ]
```

**The page owns it, in C#.** `UiCollapse`, `UiAccordion`, `UiSwap`, `UiTabs` and `UiModal`'s `Open` path hold
their state in a field and redraw through the live diff.

**The markup owns it.** `UiTab` with an `Href` is a real link with a real URL, so a tab is bookmarkable,
survives a refresh and answers the back button. `UiDrawer` keeps its checkbox because daisyUI's rules are
written against `.drawer-toggle:checked`; C# sets it and hears it change, but the input is the component.

**And for a view with no URL, the same tab takes a `Name` instead.** Wrap the row in a `UiTabGroup` and give
each tab a `UiTabPanel`:

```csharp
UiTabGroup.Selected(_pane).OnSelect(p => _pane = p)[
    UiTabs[
        UiTab.Label("Details").Name("details"),
        UiTab.Label("History").Name("history")
    ],
    UiTabPanel.Name("details")[ /* … */ ],
    UiTabPanel.Name("history")[ /* … */ ]
]
```

One component for both, because a reader sees one thing — what it is comes from what it is given. The
`UiTabs` inside the group is not ceremony: a `tablist` may contain only tabs, so the panels cannot be its
siblings, and it is the structure Flux uses for the same reason. Leave `Selected` off and the group shows the
first tab and keeps track itself.

Inside a group the tab is a real `<button>`, not a link — there is nowhere for it to go, and an `href="#"` is
one the browser follows, putting a stray fragment in the address bar and breaking the back button it was meant
to protect. The keyboard is the tabs pattern: **ArrowLeft/ArrowRight move and show as they go**, Home and End
jump to the ends, and they wrap. Only the selected tab is a tab stop, so Tab out of the row lands *in* the
panel rather than walking every remaining tab. Every panel is rendered, with the ones not shown carrying
`hidden`, so their content is still findable by the browser's own in-page search.

**And one that lets you choose.** `UiSelect` is the platform's `<select>` by default and draws its own
list when `Native` is `false` — a `[popover]` `role="listbox"` under a `role="combobox"` box, with the
arrow keys, Home/End, Enter, and a roving `aria-activedescendant` cursor that skips unavailable
options. Reach for it when the list must carry more than the platform will show, or must escape an
`overflow: hidden` ancestor. Both modes take the same properties and mean the same thing by them; what
differs is that the drawn list **needs the runtime**, where the native control works on a prerendered
page and with scripting off. That is why the default is native.

**And one that lets you choose several — under the same name.** Bind a collection and `UiSelect` IS the
multi-select. There is no second component to remember and no `Multiple` flag to set: the field's own
type is the answer, so a model that holds many answers cannot accidentally get the control that holds
one.

```csharp
UiSelect.Bind(() => _order.Country)   // string        → one answer
UiSelect.Bind(() => _order.Tags)      // List<string>  → several
UiSelect.Values(_picked)              // controlled, several
UiSelect.Value(_country)              // controlled, one
```

`List<T>`, `IList<T>`, `HashSet<T>`, `Collection<T>`, `ObservableCollection<T>`, `T[]` and
`ICollection<T>` all open the multi-value control; the write-back refills a get-only collection in
place and otherwise builds whatever the property declares. A field typed `IReadOnlyList<T>` is the one
shape that cannot bind — it is not an `ICollection<T>`, so there is nothing to write back through.
The controlled opening is spelled `Values` rather than `Value` because `["a", "b"]` and `null` are
target-typed: they fit every collection shape equally, so one name could not tell the two controls
apart without guessing.

Native is a real `<select multiple>`; `Native: false` draws the list, shows the chosen answers as
removable chips in the box, and — unlike the single-select — leaves the list OPEN as you pick, because
choosing three answers should not mean opening it three times. `SelectAll` adds a bulk row and `Chips`
caps how many chips the box shows before the rest collapse into "+N more".

**And one you type into.** There is no `UiCombobox`, because a box you type into to narrow a fixed set
of answers is the same question a select asks. `Searchable` puts a search box at the top of the drawn
list, matching the option's words case- and accent-insensitively **in the visitor's own culture** —
somebody typing `oster` means to find `Österreich`. `Filter` says what a match is when the words shown
are not the whole answer (a country's code as well as its name); `OnSearch` hands the typing to the
page instead, for a list that comes from a server, and filters nothing locally — what the page handed
back IS the answer. `Loading` shows "Searching…" while it waits, `EmptyText` and `LoadingText` say it
in your own words, and `Clearable` adds a button that puts the field back to nothing chosen. Each of
these implies the drawn list, because a `<select>` has nowhere to put them.

A short list needs none of it: the drawn list already has **type-ahead**, where a letter jumps to the
next option starting with it, which is what a native select does.

**A whole set of choices is one field too.** `UiRadioGroup<T>` binds the group's value and
`UiCheckboxGroup<T>` binds the collection your model declares — one field, not one per option, which is what
a bare `UiRadio` (bound to its own `bool`) could never give a form.

```csharp
UiRadioGroup.Bind(() => _account.Plan).Options(plans).Label("Plan")
    .Layout(UiChoiceLayout.Cards)
    .OptionDescription(v => v == "pro" ? "Everything, billed monthly" : null)

UiCheckboxGroup.Bind(() => _account.Topics).Options(topics).Label("Email me about").CheckAll(true)
```

`Layout` is Flux's set of looks — `List`, `Cards`, `Pills`, `Buttons`, `Segmented`. It is not called
`Variant` because every field already has one (`UiVariant`: Solid, Outline, Ghost…) and two properties of
that name meaning different things on one control is worse than one with a plainer name.

**Every layout keeps a real `<input>` inside its label.** A card, a pill and a segment look like buttons, and
a button is the one thing a choice must not be: the browser's own grouping, the arrow keys inside a radio
group, the space bar, the form post and every assistive technology all come from the input being there. The
look is `has-[:checked]:` rules on the label around it — CSS reading the input's own state, with nothing to
keep in sync. Where the whole label is the affordance the box is `sr-only`, never `hidden`, which would take
it out of the tab order too. `CheckAll` reports `aria-checked="mixed"` while only some of the list is in,
rather than claiming "all" over a half-filled one.

Both selects take an `OptionTemplate` for rows that need more than words. Setting one implies the
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
    UiSelect.Bind(() => _order.Tags).Options(tags).Label("Tags")
]
```

**Toasts: the page owns the list.** One `UiToast` is one notice; `UiToaster` stacks them in a corner
(`Position` + `Align`, newest last so an arriving toast never pushes the one being read out from under the
eye). A toast takes `Heading`, an `Action` (an Undo, a link to what was made) and `Duration`.

`Duration` is the interesting one. It does **not** hide the element — it asks the runtime to *click the
toast's own dismiss control*, which runs your `OnDismiss`, which takes the toast off your list. Hiding it
instead would leave the page believing a toast is up that nobody can see, and the next render would put it
back. The countdown **pauses** while the pointer is over the toast or focus is inside it, so reaching for the
action does not lose it, and a toast with no `OnDismiss` writes no timer at all, because there would be
nothing to press.

```csharp
UiToaster.Position(UiPosition.Bottom).Align(UiAlign.End)[
    _notices.Select(n => UiToast.Key(n.Id).Message(n.Text)
        .Duration(TimeSpan.FromSeconds(6))
        .OnDismiss(() => _notices.Remove(n)))
]
```

The hook is the **runtime's**, not the kit's, and it is generic: any element with
`data-rask-dismiss-after="<ms>"` is dismissed by clicking its own `[data-rask-dismiss]` — the same convention
the focus trap presses on Escape. An `UiTone.Error` toast says `role="alert"`; every other outcome is
announced politely as `status`.

**`UiContextMenu` is the same menu, opened by a right-click.** Its children are the rows a `UiDropdown` takes,
and it is the same control underneath (`UiMenuSurface`), so the keyboard is identical; only the opening differs:

```csharp
UiContextMenu.Target(Div.TabIndex(0).Class("card")["Invoice 42"])[
    UiMenuItem.Text("Open").OnClick(Open),
    UiMenuSeparator,
    UiMenuItem.Text("Delete").Tone(UiTone.Error).OnClick(Delete)
]
```

The runtime opens it: an element carrying `data-rask-contextmenu="<popover id>"` shows that popover at the pointer
in place of the browser's menu, straight away and on either host — a round trip first would be a lag felt on every
right-click — and pulls it back inside the viewport near an edge. The ContextMenu key and Shift+F10 open it at the
focused element, which is why the target above is focusable. Nothing in a context menu should be the ONLY way to
do something: iOS Safari never fires the event, so put the same actions somewhere visible too.

**`UiCommand` is a command palette.** A search field that opens a dialog of commands — from a click, or from
anywhere on the page with its `Shortcut`:

```csharp
UiCommand.Label("Search commands").Shortcut("mod+k")[
    UiMenuGroup.Heading("Invoices")[
        UiMenuItem.Text("New invoice").Icon(UiIconName.Plus).OnClick(NewInvoice)
    ],
    UiMenuItem.Text("Settings").Href(Routes.Settings())
]
```

The commands are the same `UiMenuItem`s a dropdown takes. The dialog is the platform's modal `<dialog>`, opened by
the invoker command as `UiModal` is. Inside, the search box is a `combobox` over a `listbox` whose options are the
commands: typing narrows them in C# (case- and accent-insensitive, and a command that does not match is not
rendered, so the keyboard cannot land on it), ArrowUp and ArrowDown move the highlight while focus stays in the box,
and Enter presses the highlighted command — its handler runs or its link is followed — and closes the palette.

Three generic runtime hooks do what C# cannot. `data-rask-shortcut="mod+k"` CLICKS its element when the combination
is pressed (`mod` is ⌘ on a Mac and Ctrl elsewhere; `ctrl`, `alt`, `shift`, `meta`; a shortcut with no modifier
does not fire while the reader is typing), so a shortcut does exactly what a click on its element does.
`data-rask-press-active` makes Enter click the element a combobox's `aria-activedescendant` names, because following
a link is only reachable by clicking it. `data-rask-close-on-pick` closes a dialog after a click on an option in it
has reached its handler. The field shows the shortcut in both platforms' words and the runtime marks a Mac
(`data-rask-mac` on `<html>`), so the stylesheet shows ⌘K there and Ctrl K everywhere else.

**`UiPopover` is a panel, not a menu.** `UiDropdown` IS a menu — its children are rows you pick from, it says
`role="menu"` and it walks a keyboard cursor over them. A filter panel, a colour picker or a bubble of help is
none of those, and putting one in a menu tells a screen reader it is a list of commands and traps the arrow
keys inside it. `UiPopover` is the same machinery — a `[popover]` the browser lifts into the top layer and
dismisses on Escape and on a click outside, placed with the same `Position`/`Align` — with `role="dialog"` and
ordinary Tab movement inside.

**`UiChart` draws lines, areas and bars as SVG on the server.** No script and no chart library. The series arrive
through a factory whose parameter is the chart, as a data grid's columns do, which is what gives each lambda its
row type:

```csharp
UiChart.Data(months).Label("Revenue and costs").Format("C0").Class("h-64")[c => [
    c.X(m => m.Name),
    c.Area(m => m.Revenue).Label("Revenue"),
    c.Line(m => m.Costs).Label("Costs").Tone(UiTone.Warning),
    showOrders ? c.Bar(m => m.Orders).Label("Orders") : null
]]
```

`Line`, `Area` and `Bar` read a `double`, `decimal`, `int` or `long` without a cast, and share one value axis whose
ends are round numbers with zero on it. A series with no `Tone` takes the next colour in turn, and a legend appears
once there is more than one. Size it with a height class: the plot stretches to the box while its strokes keep
their width, and the axis labels are HTML beside it so they never stretch. Hovering a column shows that row's
values in CSS. The figure is named by `Label`, the drawing is hidden from assistive technology, and a visually
hidden table carries every value it draws — series as columns, rows as rows.

**`UiTextarea` grows, or does not.** `Resize` says which way the handle drags (`None` for a box in a layout the
extra height would break), and `AutoSize` grows the box to fit what is typed. That one is CSS —
`field-sizing: content` — so it needs no runtime and works on a prerendered page; where an engine has not
shipped it the box keeps its `Rows` and scrolls, which is what it does today, so the feature degrades to the
current behaviour rather than to a broken one.

**`UiLink.External` opens away and says so.** `target="_blank"`, `rel="noopener noreferrer"` (a new tab opened
without it can reach back through `window.opener`), a small mark and a screen-reader-only "opens in a new
tab" — all three, because any one alone is worse than none. A generated route is one of your own pages and is
never external, so it is ignored there.

**`UiSkeleton` has shapes.** `Lines(3)` draws a paragraph with the last line short, because a stack of equal
bars reads as a table; `Circle` is what an avatar leaves behind. It stays `aria-hidden` throughout.

**A text field's box can hold more than what is typed.** `UiInput` takes `Icon` and `IconTrailing`, a `Kbd`
for the shortcut that focuses it, and `Clearable` for a button that empties it — Flux's input affordances.
Any of them turns the box into a container around a bare `<input>`, which is daisyUI's own icon-input shape,
and the label then stays **above** the field: a floating caption rises through exactly the room the icon now
occupies. The container is a `<div>`, not a `<label>`, because a wrapping label implicitly names the input it
holds and the field already has a label — two names on one control is the "Email Email" problem.

**`UiAvatar` draws initials when there is no picture.** `Src` is optional; give it a `Name` and it renders the
monogram — the first letter of each of the first two words, deliberately not first-and-last, since a name is
not reliably two words in that order. The letters are `aria-hidden` and the frame carries the name, because
"AL" read letter by letter tells a reader nothing. Same frame, same rounding either way, so a list does not
change shape when somebody removes their photo.

**A labelled text field floats its label.** `UiInput`, `UiTextarea` and a native `UiSelect` draw `Label`
as daisyUI's `floating-label`: the caption sits in the field until there is content, then rises out of the
way. It is still the field's real `<label>`, linked to the control. `Floating(false)` puts it back above
the field as a legend. Controls with no text to float over keep the legend: checkboxes, ranges, ratings,
and a `UiSelect` that draws its own list.

```csharp
UiInput.Bind(() => _account.Email).Label("Email")                   // floats
UiInput.Bind(() => _account.Seats).Label("Seats").Floating(false)   // legend above the field
```

While the label floats it is also the placeholder, and a `Placeholder` you set is ignored. A different
placeholder would sit in the box in the label's place until someone focused the field. Put guidance about
the value in `Hint`, under the field, where it stays visible while typing. `Placeholder` still applies to
a field with no visible label and to one with `Floating(false)`.

**A bound field says what it knows.** Under the control it shows its validation message and, while an async
validator is still out, a small spinner with "Checking…". The words are announced; the spinner is
decoration. Opt out of either with `ShowValidation(false)` or `ShowValidating(false)`, where the page shows
those states some other way, such as a summary at the top of the form.

**The opening step fixes the type argument and the mode together.** `Bind` opens a bound control and
`Value` a controlled one; they are mutually exclusive because a control with both would have two
sources of truth for one field, and the compiler enforces it — both live on the control's entry, so
taking one leaves the other unreachable. `Label`, `Options` and the rest follow in any order, since none
of them says anything about `T`. Bound mode drives the surrounding `Form`'s validation — per-field
`Validate`, `AfterBind`, and the `aria-invalid`/`aria-describedby` display — and controlled mode leaves
the value with the parent. See [building form controls](building-form-controls.md).

**Every value control is a field, and a field has one shape.** `UiInput`, `UiTextarea`, `UiSelect` (single or
multiple), `UiOtp`, `UiFileInput`, the radio and checkbox groups and the date pickers all take the same members
from `UiFormField<T>`: a visible `Label` (a `<label for>` over the control, with an optional `Badge` beside it) or,
without one, an invisible `AccessibleLabel`; a `Hint` and a controlled `Error` under it; an `Id`, derived from the
bound member or the label when you give none; and `aria-describedby`, `aria-invalid` and `aria-required` worked out
from those and from the bound member's `[Required]` and messages. `Label` is never a required step, so write it
anywhere after the opening — `UiOtp.Value(code).Length(6).Label("Verification code").Hint("Sent to your phone")`.

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
| `UiSelect<T>` over a collection | the ELEMENT type — it binds an `ICollection<T>` |
| `UiFilter<T>` | the chosen option of a whole radio group |
| `UiRadio` | whether **this** option is the chosen one — the group's value belongs to `UiFilter<T>` |
| `UiCheckbox` `UiToggle` | on or off |
| `UiRange` `UiRating` `UiCalendar` | the position, the star count, the day |
| `UiOtp` | the code — `OnComplete` fires on the transition into a full one, in both modes |
| `UiFileInput` | the chosen file's name, **write-only** — a browser refuses to have a file input's value set, so binding fills the model and never the box. The bytes come through `OnFiles`. |


**A file drop area is the same file input.** `UiFileInput.Dropzone(true)` draws Flux UI's large area in place
of the compact box, with `Heading` (the `Label` by default) and `Text` for what is accepted:

```csharp
UiFileInput.Value("").Label("Receipts")
    .Dropzone(true)
    .Heading("Drop receipts here, or click to choose")
    .Text("PDF or JPG, several at once")
    .Accept(".pdf,.jpg")
    .Multiple(true)
    .OnFiles(files => _receipts.AddRange(files.Select(f => f.Name)))
```

The native input is stretched invisibly over the whole area, so a click anywhere opens the picker and a file
dropped anywhere lands in the input — the browser already turns a drop on a file input into a chosen file, so no
script decides where a drop goes and it works before the runtime boots. The one thing CSS cannot say is "a file is
being dragged over this", so the runtime sets `data-dragging` on the nearest `[data-rask-dropzone]` while a drag
carrying files is over it, and the area styles itself from that. `Text` is the input's `aria-describedby`, ahead of
any `Hint` or validation message. The heading is the area's caption, so a dropzone draws no legend over it.

**A field with no value yet opens on its type alone**: `UiInput.Of<string>().Label("Search")`. A form
control's openings are its mode pins, so a required step like `Label` never gets to pin `T` — without
`Of` a controlled field with nothing in it would have to invent a value to compile. `Of` is the
controlled mode: the parent still owns whatever the field ends up with.

`UiCalendar` is the one to read twice. `Month` and `OnMonth` are the **view**, not the value — paging
through months changes nothing a form would submit, which is why they sit outside the binding. Leave `Month`
unset and the calendar pages by itself.

**Several days and a range are the same entry, told apart by the model** — the way `UiSelect` becomes the
multiple select when it binds a collection:

```csharp
UiCalendar.Bind(() => model.Delivery).Label("Delivery")   // DateOnly: one day
UiCalendar.Bind(() => model.DaysOff).Label("Days off")    // List<DateOnly>, HashSet<DateOnly>, …: several
UiCalendar.Bind(() => model.Stay).Label("Stay")           // UiDateRange: a range
UiCalendar.Values([monday, friday]).Label("Days off")     // several, controlled
UiCalendar.Value(new UiDateRange(from, to)).Label("Stay") // a range, controlled
```

`UiDateRange(Start, End)` is always whole: the reader's first click is held by the control and drawn as the
start, and the model changes only when the second click gives the range an end — in date order, whichever end
was clicked first. So a bound model never holds half a range. `default(UiDateRange)` is nothing chosen, as
`default(DateOnly)` is for one day; bind the nullable where the two must differ.

**`UiDatePicker` is the field.** A field-shaped button showing the choice in the reader's short date format, with
the grid in a popover — the browser's, so the top layer, Escape, a click outside and focus back on the button
come with it. It is a form field like `UiInput` (`Label`, `Hint`, `Error`, `Badge`, validation), and it takes the
same three openings: one day closes the popover on the pick, several days keep it open while they are added, and
a range closes on the click that gives it its end.

```csharp
UiDatePicker.Bind(() => booking.Stay).Label("Stay").Min(DateOnly.FromDateTime(DateTime.Today))
```

Nothing in either is typed. Where a date may be months away, a `UiInput` of type date is faster than paging, and
it is the only route for somebody who cannot use a pointer comfortably. There is no drawn time picker:
`UiInput.Type(InputType.Time)` is the platform's own.

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
- [Tree](tree.md) — `UiTree`, whose children arrive through the indexer and whose cursor is one focusable element
- [Building components](building-components.md) — the chain the kit is composed with
