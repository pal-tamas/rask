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

```csharp
using Rask;

Ui.Button.Primary["Save"]
```

Live, on rask.sh: [Actions](https://rask.sh/docs/ui/actions) · [Data display](https://rask.sh/docs/ui/data-display) ·
[Navigation](https://rask.sh/docs/ui/navigation) · [Feedback](https://rask.sh/docs/ui/feedback) ·
[Data input](https://rask.sh/docs/ui/data-input) · [Layout & mockups](https://rask.sh/docs/ui/layout).

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
- **Simple first, composable after.** `Ui.Input.Label("Email").Description(…)` is one line; `Ui.NavList` with
  `Ui.NavGroup`s and `Ui.NavItem`s, or `Ui.Dropdown` with `Ui.MenuSub`s, is there when one line is not enough.

## Wiring it up

> **An app on `RaskApp` or the WASM host needs none of this in its code.** The host links the kit's sheet
> first and your `css/app.css` after it, and puts the theme scope on `<html>`, so `App.cs` is a title and
> a router; `app.Configure(c => c.Ui.Off())` leaves the kit out. `rask new` also sets the two properties.
> This section is for a hand-wired `AddRask()`/`MapRask<App>()` host, or an App that overrides `Shell`.

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
So `Ui.Card` is styled by it and a `card-body` you write in your own markup is not: a correct-looking
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

## Dark mode

Dark mode is [Flux UI's](https://fluxui.dev/docs/dark-mode): a `dark` class on `<html>`, and `dark:`
utilities that follow it. There is no theme list and no theme picker.

Put `Ui.AppearanceScript` in your root component's head assets, **before the stylesheets**:

```csharp
protected override Component? HeadAssets => [Title["…"], Ui.AppearanceScript, /* stylesheets */];
```

It runs before the first paint, so a dark page starts dark. The reader's **appearance** is `light`,
`dark` or `system` — `system` is the default, follows `prefers-color-scheme` while the page is open, and
is stored as no key at all; the other two are kept in `localStorage` under `rask.appearance`
(`.StorageKey("…")` to change it). Only those exact words are a choice: anything else in storage means
system. The class is put back after every morph, and another tab's change is followed.

A control needs two properties, the ones Flux documents as `Flux.appearance` and `Flux.dark`:

```js
Rask.appearance = 'light' | 'dark' | 'system'   // get or set the reader's preference
Rask.dark = true | false                        // get or set whether the page is dark right now
```

So a toggle is one line and **no C# handler** — it works before the app has booted, and costs no
handler id (ids are positional, and one handler in every page's chrome moves every id after it):

```csharp
Button.Type(ButtonType.Button).AriaLabel("Toggle dark mode")
    .Attributes(("onclick", "Rask.dark = !Rask.dark"))[Ui.Icon.Name(Ui.IconName.Moon).Mini]
```

That is the moon in rask.sh's own top bar. A Light / Dark / System menu sets `Rask.appearance` the same
way.

In your own Tailwind sheet, point `dark:` at the class, as Flux does:

```css
@custom-variant dark (&:where(.dark, .dark *));
```

### Re-skinning

Two colours, per [Flux's theming](https://fluxui.dev/docs/theming). The **base** is Tailwind's `zinc`
scale, written directly in the components, so an app changes every gray by re-pointing it in its own
`@theme`:

```css
@theme {
  --color-zinc-50: var(--color-slate-50);
  /* … 100 through 900 … */
  --color-zinc-950: var(--color-slate-950);
}
```

The **accent** is three variables — the fill of a primary action, the same hue as readable text, and
the text on the fill — with a second set under `.dark`:

```css
@theme {
  --color-fx-accent: var(--color-red-500);
  --color-fx-accent-content: var(--color-red-600);
  --color-fx-accent-foreground: var(--color-white);
}

@layer theme {
  .dark {
    --color-fx-accent: var(--color-red-500);
    --color-fx-accent-content: var(--color-red-400);
    --color-fx-accent-foreground: var(--color-white);
  }
}
```

(Flux's names are `--color-accent*`; the kit's carry `fx-` until daisyUI, which defines `--color-accent`
with another meaning, is gone.)

### While daisyUI still draws part of the kit

The components not yet rebuilt on Flux's model are daisyUI's, and daisyUI reads `data-theme`. Until the
last of them is replaced, `Ui.AppearanceScript` also writes `data-theme="dark"` or `"light"` on
`<html>` to match the class — daisyUI's other 33 themes are no longer reachable from it. `Ui.Shell`
still takes `.Theme(Ui.ThemeName.Light)` to pin a subtree (`Rask.Dashboard` pins its console light; see
[the dashboard](dashboard.md)); `Ui.ThemeName` and `UiTheme` leave with daisyUI.

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

`Ui.Badge`, `Ui.Tooltip` and the `link-*` tones render daisyUI classes, and
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

## Buttons

`Ui.Button` is [Flux's button](https://fluxui.dev/components/button) and `Ui.ButtonGroup` its `button.group`,
measured against Flux's own page in light and in dark. A variant, a size and a colour are steps:

```csharp
Ui.Button["Cancel"]                         // the outline — Flux's default
Ui.Button.Primary["Save"]                   // filled with the accent; one per view
Ui.Button.Filled["Edit"]                    // a tinted fill, no border
Ui.Button.Danger["Delete"]                  // red
Ui.Button.Ghost["More"]                     // no surface until hovered
Ui.Button.Subtle["Skip"]                    // a ghost with a muted label

Ui.Button.Sm["Small"]                       // 32px tall; 40px without a size
Ui.Button.Xs["Extra small"]                 // 24px

Ui.Button.Primary.Blue["Deploy"]            // any Tailwind hue: Ui.Color
Ui.Button.Red["Remove"]                     // on the outline it tints the label, the border and the hover
```

An icon is a prop, which is what lets the button pad itself around it — 12px on the icon's side, 16px on
the other — and size the drawing: 16px beside a label, 20px alone.

```csharp
Ui.Button.Icon(Ui.IconName.ArrowDownTray)["Export"]
Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Open"]
Ui.Button.Icon(Ui.IconName.XMark).AriaLabel("Close")          // no children: a square. Name it.
Ui.Button.Icon(Ui.IconName.Cog6Tooth).Tooltip("Settings")     // a tooltip names it too
```

```csharp
Ui.Button.Primary.Class("w-full")["Send invite"]                    // full width is a class, as in Flux
Ui.Button.Ghost.Sm.Icon(Ui.IconName.XMark).Inset(Ui.Inset.All)      // pulled out by its invisible padding
Ui.Button.Type(Ui.ButtonType.Submit)["Sign in"]                     // `type="button"` unless you say so

Ui.ButtonGroup[Ui.Button["Oldest"], Ui.Button["Newest"], Ui.Button["Top"]]   // fused: one border between each pair
```

| Prop | What it takes |
| --- | --- |
| `Variant` | `Ui.ButtonVariant`: `Outline` (default) `Primary` `Filled` `Danger` `Ghost` `Subtle` |
| `Size` | `Ui.ButtonSize`: `Base` (default) `Sm` `Xs` |
| `Color` | `Ui.Color`: Tailwind's seventeen hues, `Red` … `Rose`, and its five grays. A hue recolours every variant but `Danger`; a gray changes only `Primary` |
| `Icon`, `IconTrailing` | a `Ui.IconName`; `IconVariant` picks another drawing of it |
| `Square` | as wide as tall. Automatic with an icon and no children; `Square(false)` turns that off |
| `Align` | `Ui.Align.Start` / `Center` / `End`, for a button wider than its content |
| `Inset` | `Ui.Inset` flags — `Top`, `Bottom`, `Left`, `Right`, `All` — for a ghost or subtle button |
| `Loading` | see [Buttons that wait](#buttons-that-wait) |
| `Tooltip`, `TooltipPosition`, `TooltipKbd`, `Kbd` | a hint on hover and keyboard focus, and the shortcut shown in it |
| `Href`, `NewTab` | see [Buttons and links that go somewhere](#buttons-and-links-that-go-somewhere) |
| `As` | `Ui.ButtonAs.Div` for the look of a button on something that is not one |
| `Type`, `Disabled`, `Command`, `CommandFor` | the `<button>`'s own attributes |

`Ui.Tone`, `Ui.Variant` and `Ui.Size` do not apply to it: a button has Flux's variants, not daisyUI's tones.
A selected toggle is `.AriaPressed(AriaPressed.True)` on the variant that reads as selected — `Filled`.

## The three axes

On the components daisyUI still draws, colour, fill and size are independent and compose, so an outlined
error badge needs no member of its own:

```csharp
Ui.Badge.Error.Outline["Failed"]
```

| Enum | Members |
| --- | --- |
| `Ui.Tone` | `Neutral` `Primary` `Secondary` `Accent` `Info` `Success` `Warning` `Error` |
| `Ui.Variant` | `Solid` `Outline` `Soft` `Dash` `Ghost` `Link` |
| `Ui.Size` | `Default` `Xs` `Sm` `Md` `Lg` `Xl` |

These are daisyUI's own words, deliberately. Translating them into a private vocabulary was the first
thing this kit did and the first thing it stopped doing: daisyUI's documentation is the documentation
for everything the components render, and a second set of words made every example a translation.

Not every component honours every member — daisyUI defines no `input-outline`, and no `tooltip-neutral`
— and **a member a component has no class for writes nothing**, rather than a class that would sit in
the markup looking as though it styled something.

Other axes follow the same rule: `Ui.Position`, `Ui.Align`, `Ui.ModalPosition`, `Ui.MaskShape`,
`Ui.LoadingShape`, `Ui.SwapAnimation`, `Ui.AuraStyle`, `Ui.TabStyle`, `Ui.OpenOn`.

### One vocabulary for placing things

Everything that floats against something else is placed with the same two words, the ones Flux UI uses:
**`Position`** picks the side (`Ui.Position` — Top, Right, Bottom, Left) and **`Align`** slides it along that
side (`Ui.Align` — Start, Center, End, following the reading direction). They are two properties because
daisyUI composes them — a menu above its trigger, flush with the trigger's end edge, is both.

```csharp
Ui.Dropdown.Trigger("Actions").Position(Ui.Position.Top).Align(Ui.Align.End)[ … ]
Ui.Tooltip.Tip("Copy").Position(Ui.Position.Right)[ … ]
Ui.Tabs.Position(Ui.Position.Bottom)[ … ]
Ui.Drawer.Id("nav").Panel(menu).Position(Ui.Position.Right)[ … ]
Ui.Modal.Title("Details").Position(Ui.ModalPosition.End)[ … ]   // placed against the viewport, not a trigger
```

Events are always `On…` — `Ui.Modal.OnClose`, `Ui.Modal.OnCancel`, `Ui.Toast.OnDismiss` — the same prefix every
element event carries. A `<dialog>`'s own endings are element events too: `Dialog.OnCancel` for a dismissal and
`Dialog.OnClose` for any close.

### We style, you space

A kit component brings its padding, its border and its colours, and **never an outer margin**. Where it
sits — the gap above a row of tabs, the bleed of a scrolling strip to the screen edge — belongs to the
page that places it, because the same component sits in a card, a toolbar and a page gutter, and a margin
right for one is wrong for the other two. Two exceptions are part of a component's shape rather than its
placement: `Ui.NavTab`'s `-mb-px`, which joins the active tab's border to its nav's hairline, and
`Ui.Toast`'s `mx-auto`, which centres a fixed overlay in the viewport.

## Components that are one element

A button is a `<button>`, and a table cell is a `<td>`. `Ui.Button`, `Ui.Badge`, `Ui.List` and
the parts of a `Ui.Table` do not wrap a raw element; they are the element. They derive from **`UiElement`**, which derives
from `Element`, so every step an element takes works on them unchanged, the events included. What they
show is their **children**, the same as a raw element's:

```csharp
Ui.Button.Primary.Icon(Ui.IconName.Check).Id("save").OnClick(Save)["Save"]

Ui.Badge.Success["Live"]

Ui.TableCell.Id("total").Class("py-0")[Ui.Badge.Success["Paid"]]
```

A button takes its icons as props and sizes them itself. In a badge or an alert a bare `Ui.Icon.Name(…)`
is the right size: the kit's stylesheet sizes an icon nobody sized from what it sits in, and leaves alone
one that has a size class of its own.

## Accordion

`Ui.Accordion` is [Flux's accordion](https://fluxui.dev/components/accordion): a stack of items, each a heading
that opens the content under it.

```csharp
Ui.Accordion[
    Ui.AccordionItem[
        Ui.AccordionHeading["What's your refund policy?"],
        Ui.AccordionContent["Thirty days, no reason needed."]
    ],
    Ui.AccordionItem.Heading("How do I track my order?")["We email a tracking number."]   // the shorthand
]

Ui.Accordion.Exclusive()[ … ]        // opening one item closes the others
Ui.Accordion.Transition()[ … ]       // open and close over 250 ms
Ui.Accordion.Reverse[ … ]            // the chevron before the heading
Ui.AccordionItem.Heading("…").Expanded()[ … ]    // open to begin with
Ui.AccordionItem.Heading("…").Disabled()[ … ]    // cannot be opened or closed
```

**No handler, and no script.** An item is a native `<details>` and its heading the `<summary>`, so a click,
Enter or Space opens it in the browser, Tab moves from heading to heading, and `Exclusive()` is the platform's
own `<details name>` group. Closed content is still in the document, so find-in-page reaches it and the browser
opens the item holding the match. A disabled heading leaves the tab order, takes no pointer and says
`aria-disabled`.

**To own an item from C#**, render it in a field and keep the field in step:

```csharp
Ui.AccordionItem.Heading("Advanced settings").Expanded(_advanced).OnToggle(open => _advanced = open)[ … ]
```

`OnToggle` runs after the browser has opened or closed the item, with the state it is now in — including when
an exclusive accordion closes it because another item opened.

`Transition()` animates the height of the `<details>`' own content box (`::details-content`, with
`interpolate-size`). A browser without those opens and closes at once, which is what an accordion without the
step does everywhere.

A single collapsible section is an accordion of one item; `Ui.Collapse`, `Ui.AccordionSection` and `Ui.Marker`
are gone.

## Icons

`Ui.Icon` is [Flux's icon](https://fluxui.dev/components/icon): every [Heroicon](https://heroicons.com), under
Heroicons' own name in PascalCase — `arrow-down-tray` is `Ui.IconName.ArrowDownTray`, `x-mark` is `XMark`,
`bars-3-bottom-left` is `Bars3BottomLeft`. Search heroicons.com for the drawing, then write its name.

```csharp
Ui.Icon.Name(Ui.IconName.Bolt)          // 24px, outline — the default
Ui.Icon.Name(Ui.IconName.Bolt).Solid    // 24px, filled
Ui.Icon.Name(Ui.IconName.Bolt).Mini     // 20px, filled
Ui.Icon.Name(Ui.IconName.Bolt).Micro    // 16px, filled

Ui.Icon.Name(Ui.IconName.Bolt).Class("size-8")                                     // another size
Ui.Icon.Name(Ui.IconName.Bolt).Solid.Class("text-amber-500 dark:text-amber-300")   // a colour
Ui.Icon.Name(Ui.IconName.Loading)                                                  // Flux's spinner
```

Each variant (`Ui.IconVariant`) is a separate drawing made for its size, so pick the variant for the size you
want rather than resizing one. A `size-*` class still overrides it, from any position in the class list: the
default size carries no specificity. An icon is painted in `currentColor`, so a `text-*` class — or the text
it sits in — colours it. It is `aria-hidden`; give a control that is only an icon an `AriaLabel`.

`Ui.IconName.Loading` is the one name that is not a Heroicon: Flux's spinner, which turns once a second. A
variant only sizes it.

The set is generated by `scripts/flux/icons.mjs` from the `heroicons` npm package (MIT) and compiled into
`Rask.Ui.dll` as UTF-8 path data — no icon font, no sprite sheet, no request. Flux's two Laravel-side
extras do not carry over: `php artisan flux:icon` (importing Lucide) and Blade icon files. An icon outside
the set is an ordinary component that draws its own `Svg`.

A button with an icon and no children is a square holding one glyph, so name it:
`Ui.Button.Icon(Ui.IconName.XMark).AriaLabel("Close")`.

The kit's classes and ARIA compose with yours instead of replacing them. `.Class("mb-0")` is added to the
kit's classes through `ResolveClass()`. A label you set with `.Aria(…)` wins over one the kit would derive
through `ResolveAria()`, which writes into Core's `aria-*` slot so the attribute order stays the one
`Element` documents.

**One tag, and that has a consequence.** An element's children are written straight from the indexer, so
an element-derived component cannot draw anything around them — `Ui.Button` adds its icons, its spinner and
its tooltip INSIDE the tag, beside the label. The two places this shows:

- **`Ui.Table`** sits in a box that scrolls, and a heading wraps its label. Each renders its own tag
  with what it adds inside or around it. The id, classes, data, ARIA and handlers you set stay on the
  `<table>` and the `<th>`, so `#orders tbody tr` finds the rows.
- **`Ui.List.Ordered()`** is an `<ol>`, numbered. Use it when the order means something, such as a log
  or a set of steps. A row is a plain `Li`.

The kit pads a list's rows with a stylesheet rule on its `ui-list` marker, in the layer below your
utilities. A `px-0` on a row therefore gets flush content. A `[&>li]:px-4` variant would have
out-specified it.

## Tables

`Ui.Table` is [Flux's table](https://fluxui.dev/components/table), part for part: `Ui.TableColumns` holds a
`Ui.TableColumn` per heading, and `Ui.TableRows` holds a `Ui.TableRow` of `Ui.TableCell`s per record.

```csharp
Ui.Table.Paginate(Ui.Pagination.Pages(pages).Current(page).OnPage(Go))[
    Ui.TableColumns[
        Ui.TableColumn["Customer"],
        Ui.TableColumn.Sortable().Sorted(sortBy == "date").Direction(direction).OnSort(() => Sort("date"))["Date"],
        Ui.TableColumn["Status"],
        Ui.TableColumn.End["Amount"]
    ],
    Ui.TableRows[
        orders.Select(order => Ui.TableRow.Key(order.Id)[
            Ui.TableCell[order.Customer],
            Ui.TableCell[order.Date],
            Ui.TableCell.Class("py-0")[Ui.Badge.Success["Paid"]],
            Ui.TableCell.Variant(Ui.TableCellVariant.Strong).End[order.Amount]
        ])
    ]
]
```

| Part | Props |
| --- | --- |
| `Ui.Table` | `Bleed()` runs the dividers through the padding of the box it sits in; `Paginate(…)` is the pager under the rows; `ContainerClass("max-h-80")` styles the box around the table. |
| `Ui.TableColumns` | `Sticky()` keeps the headings in view while the rows scroll. |
| `Ui.TableColumn` | `Align` (`.Start` `.Center` `.End`), `Sortable()`, `Sorted(…)`, `Direction(Ui.TableColumnDirection.Asc \| Desc)`, `Sticky()`, and `OnSort`. |
| `Ui.TableRows` | The rows. |
| `Ui.TableRow` | `Key(…)`, `Sticky()`. |
| `Ui.TableCell` | `Align`, `Variant(Ui.TableCellVariant.Strong)`, `Sticky()`. |

**The table sorts nothing and pages nothing.** The page keeps the sorted column, its direction and the
page number. A `Sortable()` heading draws its label as a button with a chevron, and `OnSort` fires when
the heading is clicked or its button is activated from the keyboard. `Sorted` and `Direction` are what
you set in answer.

**Sticky parts need a background.** `Ui.TableColumns.Sticky()` holds the headings at the top of the box,
and a sticky `Ui.TableColumn` with sticky `Ui.TableCell`s under it holds a column at the left. Give each
`.Class("bg-white dark:bg-zinc-900")`, or the rows show through. A sticky column casts a shadow once the
others have scrolled under it; that is a CSS scroll timeline, so a browser without one shows no shadow.

**`Bleed()` needs to know the gutter.** It reads `--ui-bleed`, 1.5rem unless the box says otherwise:
`Div.Class("p-4 [--ui-bleed:1rem]")[Ui.Table.Bleed()[…]]`.

A cell pads itself 12px at zero specificity, so `py-0` or `px-6` on a cell wins. A cell does not wrap;
write `whitespace-normal` on one that should. `colspan` and `scope` go through `.Attributes(("colspan", "3"))`.

`Strong` and `Desc` are also HTML tags, and a tag's entry hides a step of the same name: those two
values are passed as `Variant(Ui.TableCellVariant.Strong)` and `Direction(Ui.TableColumnDirection.Desc)`.

## Callouts

`Ui.Callout` is [Flux's callout](https://fluxui.dev/components/callout), measured from that page and drawn the
same in light and dark: something the page needs its reader to notice, in place. It replaces `Ui.Alert`.

```csharp
Ui.Callout.Danger.Icon(Ui.IconName.XCircle).Heading("Payment failed").Text("Your card was declined.")

Ui.Callout.Icon(Ui.IconName.Clock).Actions([Ui.Button["Renew now"], Ui.Button.Ghost["View plans"]])[
    Ui.CalloutHeading["Subscription expiring soon"],
    Ui.CalloutText[
        "Your current plan will expire in 3 days. ",
        Ui.CalloutLink.Href(Routes.Billing())["Learn more"]
    ]
]

Ui.Callout.Color(Ui.Color.Purple).Icon(Ui.IconName.Sparkles).Inline()      // actions beside the text
    .Heading("Have a question?")
    .Actions(Ui.Button["Ask"])
    .Controls(Ui.Button.Ghost.Icon(Ui.IconName.XMark).AriaLabel("Dismiss").OnClick(Hide))
```

| Flux | Rask |
| --- | --- |
| `variant` | `Variant`, or its members as steps: `.Secondary` `.Success` `.Warning` `.Danger`. With none it is the secondary callout on a white surface, which is what Flux draws for one too |
| `color` | `Color(Ui.Color.Blue)` — any of Tailwind's seventeen hues, each with the border, icon, heading and text shades Flux gives it. It wins over `Variant`; Flux draws one grey, so every grey is zinc |
| `icon`, `icon:variant` | `Icon(Ui.IconName.Clock)`, and `.Outline` `.Solid` `.Mini` `.Micro` (mini, 20px, when unset). On `Ui.CalloutHeading` instead, the icon sits in the heading's own line |
| the `icon` slot | `CustomIcon(component)` |
| `heading`, `text` | `Heading("…")`, `Text("…")` — shorthand for a `Ui.CalloutHeading` and a `Ui.CalloutText` ahead of the children |
| `inline` | `Inline()` — actions beside the text once the CALLOUT is 28rem wide (a container query, so it holds in a narrow column on a wide screen) |
| the `actions` and `controls` slots | `Actions(component)` and `Controls(component)`; several is a collection, `Actions([a, b])` |
| `flux:callout.link` `href`, `external` | `Ui.CalloutLink.Href(route or "https://…")`, `.External()` |

**A callout announces nothing by itself**, exactly as Flux's does not — `Ui.Alert` wrote `role="alert"` or
`role="status"` from its tone. One that is on the page when it loads is content, and a live region there is
read out over the page's own heading. One that APPEARS because something happened says so where it is written:

```csharp
save.IsError ? Ui.Callout.Danger.Role("alert").Heading("Something went wrong.") : null     // interrupts
saved ? Ui.Callout.Success.Role("status").Heading("Saved.") : null                            // waits its turn
```

Dismissing is yours too: `Controls` places the button, and what pressing it does — a field, a row in a table —
is the page's. `Id`, `Class` and `Role` land on the callout itself; `Ui.CalloutText` and `Ui.CalloutLink` are
their elements and take every element step.

## Buttons and links that go somewhere

Every kit component that goes somewhere takes a `RouteUrl`: `Ui.Button.Href`, `Ui.Link.Href`,
`Ui.Stat.Href`, `Ui.NavTab.Href` and `Ui.Brand.Href`. All of them follow one rule. Hand one a
**generated route** and it navigates inside the app, the way `NavLink` does. The anchor carries
`data-rask-nav`, which the runtime intercepts and routes without reloading the page. It also carries the
deploy's path base, so a new tab or a copied link reaches the same page. Hand one a **string** and it
is an ordinary link the browser follows itself, written exactly as given. That is what a URL that
leaves the app wants.

```csharp
Ui.Button.Primary.Href(Routes.CreateProduct())["New product"]    // stays in the app
Ui.Link.Href(Routes.ProductsPage())["Back to the list"]                   // stays in the app
Ui.Button.Href("https://github.com/pal-tamas/rask").NewTab()["GitHub"]     // leaves it
```

A string that happens to name one of your own pages is still a string: it reloads the whole app to get
there. Use the route. `NewTab(true)` is never intercepted, because the reader asked for another tab.

## Heading, text and link

Flux UI's [heading](https://fluxui.dev/components/heading) and [text](https://fluxui.dev/components/text), with
Flux's names, props and look — measured against its docs in light and dark by `scripts/flux/parity.mjs`.

```csharp
Ui.Heading["User profile"]                                    // <div>, 14px, medium
Ui.Heading.Level(3).Lg["Orders"]                              // <h3>, 16px
Ui.Text.Class("mt-2")["This information will be displayed publicly."]

Ui.Text.Variant(Ui.TextVariant.Strong)["Total"]               // or .Subtle, or .Color(Ui.Color.Blue)
Ui.Text["Visit our ", Ui.Link.Href(Routes.ProductsPage())["documentation"], " for more information."]
Ui.Link.Href("https://example.com").External()["The spec"]    // new tab, rel="noopener noreferrer"
Ui.Link.As(Ui.LinkAs.Button).OnClick(Save)["Create account →"] // a <button type="button"> drawn as a link
```

| Component | Props |
| --- | --- |
| `Ui.Heading` | `Size` — `Base` (14px), `Lg` (16px), `Xl` (24px), `Xxl` (36px, Flux's `2xl`); `Level` 1–6, a `<div>` without one; `Accent()` |
| `Ui.Text` | `Size` — `Sm`, `Default`, `Lg`, `Xl`; `Variant` — `Default`, `Strong`, `Subtle`; `Color` — a `Ui.Color` (Tailwind's hues), which wins over the variant; `Inline()` for a `<span>` |
| `Ui.Link` | `Href` — a generated route navigates inside the app, a string is an ordinary link; `Variant` — `Default` (underlined), `Ghost` (underlined under the pointer), `Subtle`; `External()`; `As` — `A`, `Button`; `Accent(false)` to draw it in the page's ink |

Each value is also a step — `Ui.Heading.Xl`, `Ui.Text.Subtle`, `Ui.Link.Ghost` — except the three that are also HTML
tags: write `Variant(Ui.TextVariant.Strong)` and `As(Ui.LinkAs.Button)`, because `.Strong`, `.Button` and `.A` on a
component are the inherited tag entries. Each is one HTML element, so
`Id`, `Class`, `Style`, `Data`, `Aria` and the events are the element's own. There is no subheading: the line under
a heading is a `Ui.Text`, as on Flux's page. A heading is zinc-800 (white in dark), text zinc-500 (white at 70%),
and a link takes the accent with an underline at a fifth of it that fills in under the pointer.

## Application layout

Flux UI's layout pieces, drawn with daisyUI. The sidebar beside the docs on this site is exactly this.

```csharp
Ui.Sidebar.Id("app-nav").Collapsible(Ui.Breakpoint.Lg).Page(Main[Outlet])[
    Ui.Brand.Label("Shop").Href(Routes.HomePage()),
    Ui.NavList.AccessibleLabel("Main")[
        Ui.NavItem.Label("Orders").Href(Routes.OrdersPage()).Icon(Ui.IconName.BookOpen).Badge("12"),
        Ui.NavGroup.Title("Catalogue").Expandable()[
            Ui.NavItem.Label("Products").Href(Routes.ProductsPage()),
            Ui.NavItem.Label("Categories").Href(Routes.CategoriesPage())
        ]
    ],
    Ui.Spacer.Key("spacer"),
    Ui.NavList.AccessibleLabel("Account")[Ui.NavItem.Label("Settings").Href(Routes.SettingsPage())]
]

// in the top bar, shown only while the sidebar is collapsed:
Ui.SidebarToggle.For("app-nav").Collapsible(Ui.Breakpoint.Lg)
```

- **`Ui.Sidebar`** is an `<aside>` beside `Page`: docked — sticky, full height — from `Collapsible` up, and a
  drawer below it that `Ui.SidebarToggle` slides in and a click beside it slides out. The open state is daisyUI's
  checkbox, so it opens on a prerendered page with no runtime; `Open`/`OnToggle` mirror it into C#, which is how a
  navigation closes it. `Ui.SidebarToggle` is a `<label>` for that checkbox with `role="button"` and a tab stop, and
  the runtime presses it on Enter and Space.
- **`Ui.NavList`** is a named `<nav>` around daisyUI's `menu`. **`Ui.NavItem`** is a `NavLink` underneath, so
  **`Current` is worked out from the route** — `menu-active` and `aria-current="page"` — unless you state it;
  `Match` + `MatchPrefix` keep an item current across a section. **`Ui.NavGroup`** is a heading over its items, or a
  `<details>` disclosure with `Expandable`, controlled with `Expanded`/`OnToggle`.
- **`Ui.SidebarHeader`** and **`Ui.SidebarFooter`** hold their place while the navigation between them scrolls —
  Flux's `sidebar.header` and `sidebar.footer`. The footer needs no `Ui.Spacer` in front of it: it pins itself, so
  a nav list long enough to scroll scrolls *between* the two rather than pushing the account row off the bottom.
- **`Ui.Profile`** is that account row: an avatar, a name, an optional caption, and — given children — the button
  that opens the account menu, with the same keyboard contract `Ui.Dropdown` has, because both are
  **`UiMenuButton`** underneath. Without an `Avatar` it draws the **initials** of `Name`, since most accounts have
  no picture and a broken image is worse than a monogram. Its menu opens upward by default, because the row sits
  at the bottom of the sidebar.
- **A docked sidebar can narrow to a rail of icons**, which is a different question from `Collapsible`:
  `Collapsible` says at what width the sidebar stops being beside the page at all, `Collapsable(true)` keeps it
  beside the page and takes the words away. **`Ui.SidebarCollapse`** is the control, a `<label>` for a second
  checkbox — so it needs no runtime either — and it appears exactly where `Ui.SidebarToggle` disappears.
  The words that go are marked `ui-rail-hide` by the components that own them, so a CSS rule never has to
  guess which text is a label and which is content, and each link, the brand and the profile row keep their
  name as a `title` — the rail's tooltip, and the accessible name of a link that is only an icon now. (A drawn
  tooltip would be cut off: the panel clips its overflow.)

  `Collapsed`/`OnCollapse` hand the choice to C#, and remembering it is the app's: the kit stores nothing on
  your behalf. Read it once from `localStorage` ([`Rask.Web`](web-apis.md)) after the first render and write it back as it changes:

  ```csharp
  public sealed partial class AppShell : Component
  {
      private bool _rail;

      // After the first render, because storage lives in the browser. The hook repaints when it completes.
      protected override async Task OnFirstRender()
      {
          if (await LocalStorage.GetItem("sidebar-rail") == "1")
          {
              _rail = true;
          }
      }

      protected override Component? Render() =>
          Ui.Sidebar.Id("nav").Page(Ui.Main[Children ?? []]).Collapsible(Ui.Breakpoint.Lg).Collapsable()
              .Collapsed(_rail)
              .OnCollapse(async rail =>
              {
                  _rail = rail;
                  await LocalStorage.SetItem("sidebar-rail", rail ? "1" : "0");
              })[ … ];
  }
  ```

  The first paint is the open sidebar and a remembered rail follows a frame later; a page that must not flicker
  keeps the choice in a cookie instead and reads it on the server.
- **`Ui.Spacer`** is `flex: 1`: it pushes what follows it to the far end of a row or a column.
- **`Ui.Separator`** is Flux's separator, prop for prop: `Vertical()` (or `Orientation(Ui.SeparatorOrientation.Vertical)`),
  `Text("or")` for a word in the middle of the line, and `.Subtle` (`Variant(Ui.SeparatorVariant.Subtle)`) for a
  line that blends into the background. It is decoration to assistive tech (`role="none"`) and carries **no
  margin** — the page spaces it; a vertical one is as tall as its row, and `.Class("my-2")` shortens it.
- **`Ui.Heading`**, **`Ui.Text`** and **`Ui.Link`** are Flux's own — see [Heading, text and link](#heading-text-and-link).
  `Ui.Header` takes a `TitleLevel` instead of a fixed `<h1>`.

### Card

`Ui.Card` is [Flux UI's card](https://fluxui.dev/components/card), part for part: `Ui.CardHeader`
(`Ui.CardHeading`, `Ui.CardSubheading`, `Ui.CardActions`), `Ui.CardBody`, `Ui.CardFooter` and `Ui.CardBleed`.
The card handles the spacing, dividers and corners between them.

```csharp
Ui.Card.Inset.Soft.Lg[
    Ui.CardHeader[
        Ui.CardHeading.Level(2)["Profile"],
        Ui.CardSubheading["This is how others will see you"],
        Ui.CardActions[Ui.Button["Edit"]]            // centres on the heading, tucks into the corner
    ],
    Ui.CardBody[form],
    Ui.CardFooter[Ui.Text["Last saved 2 minutes ago"], Ui.CardActions[Ui.Button["Save"]]]
]

Ui.Card[Ui.CardHeading.Lg["Are you sure?"], P["This cannot be undone."]]   // parts are optional
```

| Step | Values | What it decides |
|---|---|---|
| `Body` | `Seamless` (default) · `Inset` · `Flush` · `Divided` · `Separated` | how header, body and footer are set apart: by space, a panel in from the edges, a panel out to them, lines, or tinted bands |
| `Variant` | `Default` · `Muted` · `Soft` · `Outline` · `Filled` | the surface — raised, two tints, an edge only, a tint with no edge |
| `Size` | `Xs` · `Sm` · `Md` (default) · `Lg` | padding, corners and the space between parts |
| `Divider` | `Ui.CardDivider.Inset` | with `Divided`, stops the lines at the content's edges |
| `Highlight` | `false` | turns off the faint highlight inside the top edge (light mode) |

Each value is a chain step (`Ui.Card.Divided.Sm`); `Inset` is the body treatment, so the divider is
`.Divider(Ui.CardDivider.Inset)`. `Ui.CardHeading` takes `Size` (`Base`, `Lg`, `Xl`) and `Level`; without a
level it is a `<div>`, outside the document outline.

- **A header or footer outside a card** is a section heading above one — give it the `Size` of the card it sits
  beside. **Inside a `Ui.CardBody`** it titles a sub-section and takes none of the card's treatment.
- **`Ui.CardBleed`** runs media out to the card's edges: always to the sides, to the top or bottom when it is
  the first or last thing, rounding only the corners it reaches. The distances are the `--ui-bleed-x`,
  `--ui-bleed-top`, `--ui-bleed-bottom`, `--ui-bleed-top-radius` and `--ui-bleed-bottom-radius` variables the
  card and its body set, so your own content can bleed the same way.
- **A link card** is a link around a small card, as in Flux — there is no `Href` on the card:
  `A.Href(url)[Ui.Card.Xs.Class("hover:bg-zinc-50 dark:hover:bg-zinc-700")[…]]`. Nothing inside it may be a button.

## Buttons that wait

A button whose handler is still running shows it — with nothing to set. Press "Save" on a slow link and,
once the handler has gone 200 ms without finishing, the label fades out where it stands, Flux's spinner
fades in over it at the same width, the button carries `aria-busy="true"`, takes no pointer events, and
drops a second press until the first one's render has landed. This is Flux UI's answer to the double
submit, and it holds on both hosts: the Server runtime ends the wait on the handler's ack, the WebAssembly
runtime when its dispatch returns.

```csharp
Ui.Button.Primary.OnClick(Save)["Save"]                            // waits automatically
Ui.Button.Type(Ui.ButtonType.Submit)["Sign in"]                    // so does a form's submit
Ui.Button.Icon(Ui.IconName.Plus).Loading(false).OnClick(Step)      // a stepper: presses queue
Ui.Button.Loading(_exporting)["Export"]                            // work that outlives the handler
```

As in Flux, a button carries the spinner when it has something to wait on: an `OnClick`, `type="submit"`,
or a `Loading` you set. `Loading(false)` tells the runtime to leave the button alone.

It is the **runtime** that marks the button, not script in the kit, because only the runtime knows when a
dispatch starts and ends. So every `<button>` with a handler gets the same `data-loading` + `aria-busy`
attributes — `Ui.Button` is what turns them into a spinner, a hand-written daisyUI `btn` still gets the
kit's older one, and your own CSS can style `[data-loading]` on any control. `data-rask-loading="off"` on an element, or on a toolbar around several,
opts them out; `data-rask-loading` on a non-button element opts it in. It is never `disabled`, which would
throw keyboard focus off the control mid-press. A Blazor island's buttons get it too — their handlers
dispatch over the same channel.

## What is in it

Grouped as daisyUI groups them, so its documentation reads straight across.

| | |
| --- | --- |
| **Actions** | `Ui.Button` `Ui.ButtonGroup` `Ui.Dropdown` `Ui.ContextMenu` `Ui.Command` `Ui.Popover` `Ui.Modal` `Ui.Swap` `Ui.Fab` |
| **Data display** | `Ui.Accordion` `Ui.AccordionItem` `Ui.AccordionHeading` `Ui.AccordionContent` `Ui.Avatar` `Ui.Aura` `Ui.Badge` `Ui.Card` `Ui.CardHeader` `Ui.CardHeading` `Ui.CardSubheading` `Ui.CardActions` `Ui.CardBody` `Ui.CardFooter` `Ui.CardBleed` `Ui.Carousel` `Ui.ChatBubble` `Ui.Countdown` `Ui.Diff` `Ui.Empty` `Ui.Hover3d` `Ui.HoverGallery` `Ui.Kbd` `Ui.Highlight` `Ui.List` `Ui.ListRow` `Ui.Stat` `Ui.StatusDot` `Ui.Table` `Ui.TableColumns` `Ui.TableColumn` `Ui.TableRows` `Ui.TableRow` `Ui.TableCell` `Ui.DataGrid` `Ui.Column` `Ui.Tree` `Ui.TextRotate` `Ui.Timeline` `Ui.Chart` |
| **Navigation** | `Ui.Breadcrumbs` `Ui.Dock` `Ui.Link` `Ui.Megamenu` `Ui.MegamenuPanel` `Ui.Menu` `Ui.MenuItem` `Ui.Navbar` `Ui.Pagination` `Ui.Steps` `Ui.Step` `Ui.Tabs` `Ui.Tab` |
| **Feedback** | `Ui.Callout` `Ui.CalloutHeading` `Ui.CalloutText` `Ui.CalloutLink` `Ui.Loading` `Ui.Progress` `Ui.Skeleton` `Ui.SkeletonLine` `Ui.SkeletonGroup` `Ui.Toast` `Ui.Tooltip` |
| **Data input** | `Ui.Input` `Ui.Textarea` `Ui.Select` `Ui.FileInput` `Ui.Checkbox` `Ui.Toggle` `Ui.Radio` `Ui.Range` `Ui.Rating` `Ui.Field` `Ui.Label` `Ui.Description` `Ui.Error` `Ui.Fieldset` `Ui.Legend` `Ui.Validator` `Ui.Otp` `Ui.Filter` `Ui.Calendar` `Ui.DatePicker` `Ui.DatePickerInput` `Ui.DatePickerButton` `Ui.TimePicker` `Ui.DatePicker` |
| **Layout** | `Ui.Separator` `Ui.Drawer` `Ui.Footer` `Ui.Hero` `Ui.Indicator` `Ui.Join` `Ui.Stack` `Ui.Mask` |
| **Mockup** | `Ui.MockupBrowser` `Ui.MockupCode` `Ui.MockupPhone` `Ui.MockupWindow` |
| **Chrome** | `Ui.Shell` `Ui.TopBar` `Ui.Brand` `Ui.Nav` `Ui.NavTab` `Ui.CrumbSwitcher` `Ui.CrumbSeparator` `Ui.TopLink` `Ui.Main` `Ui.Header` `Ui.Grid` `Ui.MetricRow` `Ui.Metric` `Ui.DetailList` `Ui.DetailRow` `Ui.Code` `Ui.Search` |
| **Support** | `Ui.Icon` / `Ui.IconName` / `Ui.IconVariant` (all of Heroicons: outline, solid, mini, micro), `Ui.AppearanceScript` (dark mode), `Ui.Breakpoint`, `UiStyles`, `UiStylesheet` |

## Who owns the state

The kit ships no JavaScript, and that constraint decides the shape of every interactive component. It
resolves three ways, and which one a component takes is a property of what the platform can do rather
than of anyone's preference.

**The browser owns it, declaratively.** `Ui.Modal` with an `Id` and a `Trigger` is a real **modal**
`<dialog>`, opened by an HTML invoker command (`command="show-modal" commandfor`): the browser supplies
the top layer, an inert page behind it so Tab cannot wander out, Escape, and focus handed back to the
trigger on close. Every open and close control also names the dialog as a `popover`, so a browser
without invoker commands (before Chrome 135, Firefox 144, Safari 26.2) opens it as a popover instead —
top layer and Escape, without the inert page. `Ui.Megamenu` is built on the popover the same way. `Ui.Fab`
opens on `:focus-within` because daisyUI defines no class to force it. All of these work on a prerendered
page with no runtime booted, and with scripting off entirely.

```csharp
Ui.Modal.Title("Shortcuts").Id("shortcuts").Trigger("Show shortcuts")[ … ]
Ui.Modal.Title("Filters").Id("filters").Trigger("Filters").Position(Ui.ModalPosition.End)[ … ]  // a flyout
Ui.Modal.Title("Unsaved work").Id("edit").Dismissible(false).Escapable(false)[ … ]
```

Flux UI's switches are all here: `Dismissible(false)` ignores a click outside, `Escapable(false)` ignores
Escape (`closedby="none"`; Safari has not shipped it), `Closable(false)` drops the header's close button, and
`OnClose` hears every way it closed. `OnCancel` hears only a DISMISSAL — Escape or a click outside — and runs
before `OnClose`, so a dialog holding a draft can throw it away when the user backs out and keep it when they
press a button that closes it; the header's close button is not a dismissal. `Position(Ui.ModalPosition.Start|End)` makes it a full-height flyout. While
any kit dialog is open the page behind it does not scroll. The state-driven `Open` path below cannot reach the
top layer, but it is not left without containment: it carries the runtime's `data-rask-focus-trap`, so focus
moves in, Tab cycles inside, Escape runs `OnCancel` then `OnClose`, and focus returns when it closes.

`Ui.Tooltip` takes `Kbd("⌘S")` to teach a shortcut where the reader is already looking, and `Toggleable(true)`
to show on a tap — a touch screen has no hover, so an ordinary tooltip is never seen there.

**The browser owns the open state, C# owns the cursor.** `Ui.Dropdown` is a menu button over a `[popover]`
menu: the browser opens and closes it — top layer, Escape, a click outside, focus back on the trigger — and
the menu takes focus as it opens. What C# owns is the keyboard cursor Flux UI's menus have: the arrows move an
`aria-activedescendant` cursor that skips disabled rows and wraps, Home/End jump, a letter jumps to the next
row starting with it, ArrowRight opens a submenu and ArrowLeft closes it, Enter or Space press the row, Tab
leaves. The runtime supplies the few things C# cannot: pressing the row, closing the popover after a pick, and
closing it when Tab leaves.

```csharp
Ui.Dropdown.Trigger("View").Align(Ui.Align.End)[
    Ui.MenuGroup.Title("Arrange")[
        Ui.MenuSub.Title("Sort by")[
            Ui.MenuRadioGroup.Value(_sort).Options([("name", "Name"), ("date", "Date")]).OnChange(s => _sort = s)
        ],
        Ui.MenuItem.Text("Refresh").Kbd("⌘R").OnClick(Refresh)
    ],
    Ui.MenuSeparator.Key("sep"),
    Ui.MenuCheckbox.Key("archived").Value(_archived).Text("Show archived").OnChange(on => _archived = on),
    Ui.MenuItem.Text("Delete").Error.OnClick(Delete)
]
```

A submenu flies out beside its row, and a pointer moving diagonally toward it — across the row below — does
not close it: the flyout carries a CSS wedge back to its row and waits 300 ms before closing, Flux's **safe
triangle** with no script. A tap opens it on a touch screen. `Ui.MenuCheckbox` keeps the menu open, since
flipping three switches should not mean opening it three times; any row can ask for the same with `KeepOpen`.
Style the rows from `data-highlighted` (the cursor), `data-checked` and the dropdown's `data-open`.

`Key` can go anywhere in an item's chain — `Ui.MenuCheckbox.Value(x).Key("k")` and
`Ui.MenuCheckbox.Key("k").Value(x)` mean the same thing, and a generic item such as `Ui.MenuRadioGroup` takes
one too (see [composition.md](composition.md)).

`Open` is nullable and the three settings mean three things: unset leaves it to the reader; `true` and `false`
hand it to the page, and the runtime shows or hides the popover to match whenever the page changes its mind,
which is what lets a dropdown close itself when the action inside it completes. `OpenOn(Ui.OpenOn.Hover)` keeps
daisyUI's CSS dropdown, which a pointer can open and a popover cannot — without the keyboard cursor.

```csharp
Ui.Dropdown.Trigger("Actions").Open(_open).OnToggle(open => _open = open)[ … ]
```

**The page owns it, in C#.** `Ui.Swap`, `Ui.Tabs` and `Ui.Modal`'s `Open` path hold their state in a field and
redraw through the live diff.

**The browser owns it, and tells the page.** A `Ui.AccordionItem` is a `<details>`: it opens with no handler at
all, and `Expanded` with `OnToggle` is how a page keeps it in a field — see [Accordion](#accordion).

**The markup owns it.** `Ui.Tab` with an `Href` is a real link with a real URL, so a tab is bookmarkable,
survives a refresh and answers the back button. `Ui.Drawer` keeps its checkbox because daisyUI's rules are
written against `.drawer-toggle:checked`; C# sets it and hears it change, but the input is the component.

**And for a view with no URL, the same tab takes a `Name` instead.** Wrap the row in a `Ui.TabGroup` and give
each tab a `Ui.TabPanel`:

```csharp
Ui.TabGroup.Selected(_pane).OnSelect(p => _pane = p)[
    Ui.Tabs[
        Ui.Tab.Label("Details").Name("details"),
        Ui.Tab.Label("History").Name("history")
    ],
    Ui.TabPanel.Name("details")[ /* … */ ],
    Ui.TabPanel.Name("history")[ /* … */ ]
]
```

One component for both, because a reader sees one thing — what it is comes from what it is given. The
`Ui.Tabs` inside the group is not ceremony: a `tablist` may contain only tabs, so the panels cannot be its
siblings, and it is the structure Flux uses for the same reason. Leave `Selected` off and the group shows the
first tab and keeps track itself.

Inside a group the tab is a real `<button>`, not a link — there is nowhere for it to go, and an `href="#"` is
one the browser follows, putting a stray fragment in the address bar and breaking the back button it was meant
to protect. The keyboard is the tabs pattern: **ArrowLeft/ArrowRight move and show as they go**, Home and End
jump to the ends, and they wrap. Only the selected tab is a tab stop, so Tab out of the row lands *in* the
panel rather than walking every remaining tab. Every panel is rendered, with the ones not shown carrying
`hidden`, so their content is still findable by the browser's own in-page search.

**And one that lets you choose.** `Ui.Select` is the platform's `<select>` by default and draws its own
list when `Native` is `false` — a `[popover]` `role="listbox"` under a `role="combobox"` box, with the
arrow keys, Home/End, Enter, and a roving `aria-activedescendant` cursor that skips unavailable
options. Reach for it when the list must carry more than the platform will show, or must escape an
`overflow: hidden` ancestor. Both modes take the same properties and mean the same thing by them; what
differs is that the drawn list **needs the runtime**, where the native control works on a prerendered
page and with scripting off. That is why the default is native.

**And one that lets you choose several — under the same name.** Bind a collection and `Ui.Select` IS the
multi-select. There is no second component to remember and no `Multiple` flag to set: the field's own
type is the answer, so a model that holds many answers cannot accidentally get the control that holds
one.

```csharp
Ui.Select.Bind(() => _order.Country)   // string        → one answer
Ui.Select.Bind(() => _order.Tags)      // List<string>  → several
Ui.Select.Values(_picked)              // controlled, several
Ui.Select.Value(_country)              // controlled, one
```

`List<T>`, `IList<T>`, `HashSet<T>`, `Collection<T>`, `ObservableCollection<T>`, `T[]` and
`ICollection<T>` all open the multi-value control; the write-back refills a get-only collection in
place and otherwise builds whatever the property declares. A field typed `IReadOnlyList<T>` is the one
shape that cannot bind — it is not an `ICollection<T>`, so there is nothing to write back through.
The controlled opening is spelled `Values` rather than `Value` because `["a", "b"]` and `null` are
target-typed: they fit every collection shape equally, so one name could not tell the two controls
apart without guessing.

Native is a real `<select multiple>`; `.Native(false)` draws the list, shows the chosen answers as
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
a bare `Ui.Radio` (bound to its own `bool`) could never give a form.

```csharp
Ui.RadioGroup.Bind(() => _account.Plan).Options(plans).Label("Plan")
    .Layout(Ui.ChoiceLayout.Cards)
    .OptionDescription(v => v == "pro" ? "Everything, billed monthly" : null)

Ui.CheckboxGroup.Bind(() => _account.Topics).Options(topics).Label("Email me about").CheckAll()
```

`Layout` is Flux's set of looks — `List`, `Cards`, `Pills`, `Buttons`, `Segmented`. It is not called
`Variant` because every field already has one (`Ui.Variant`: Solid, Outline, Ghost…) and two properties of
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

## Fields: label, description, error

`Ui.Field`, `Ui.Label`, `Ui.Description`, `Ui.Error`, `Ui.Fieldset` and `Ui.Legend` are
[Flux UI's field](https://fluxui.dev/components/field), part for part. A field stacks a label, a control,
its validation message and help text, and tells the parts which control they belong to:

```csharp
Form.Model(_signUp)[
    Ui.Field[
        Ui.Label.Badge("Required")["Email"],
        Ui.Input.Bind(() => _signUp.Email).ShowValidation(false),
        Ui.Error,                                          // the first message of the bound control
        Ui.Description["We only write about your order."]  // after the control: under it
    ]
]
```

| Part | What it takes |
| --- | --- |
| `Ui.Field` | `Variant` — `Ui.Field.Inline` puts the label beside the control (a checkbox, a switch); `Block` is the default. |
| `Ui.Label` | `Badge("Required")`, a `Trailing(…)` slot at the far end, and `For(id)` when it sits outside a field. |
| `Ui.Description` | Help text. Before the control it sits under the label; after it, under the control. |
| `Ui.Error` | `For(() => model.Email)`, `Name(nameof(model.Email))` or `Message("…")`; `Icon(Ui.IconName.InformationCircle)`, `Icon(false)`. |
| `Ui.Fieldset` | `Legend("Shipping address")`, `Description("…")` — or place a `Ui.Legend` yourself. |

**The label is a real `<label for>`.** Inside a field it points at the field's control — a kit control by
the id it derives, a plain element by its `Id` — so a click on the label focuses the control with no script.
Flux draws the same parts as custom elements wired by its JavaScript; the kit ships none, so each is the
native element that already behaves that way: `<label>`, `<fieldset>`, `<legend>`.

**`Ui.Error` reads the form.** Where Flux's `name` looks a key up in Laravel's error bag, a Rask field is a
member of a model, so the error shows the first message the form holds for that member: bare inside a field
(the control's own `Bind`), `For(() => model.Email)` anywhere, or `Name("Email")` for a member of the form's
model. `Message` shows your own text whatever the form says. It is always in the page — hidden while there
is nothing to say — because it is a `role="alert"` live region, and a screen reader only announces a
message that arrives in one it already knows. Flux's `bag` and `deep` have no counterpart: a form has one
edit context, and a nested member is named by its expression rather than by a dotted path.

Markers mirror Flux's: `data-ui-field`, `data-ui-label`, `data-ui-description`, `data-ui-error`,
`data-ui-fieldset`, `data-ui-legend`. The controls not yet rebuilt on Flux keep drawing their own label,
`Hint` and message from `Label(…)`; as each is rebuilt its `Label` and `Description` draw this field around
it instead.

## Form controls

**All twelve** of the kit's data-input controls implement `IFormControl<T>`, so each works in the two
shapes every Rask input does:

```csharp
Form.Model(_order)[
    Ui.Select.Bind(() => _order.Country).Options(countries).Label("Country"),
    Ui.Select.Value(_country).Options(countries).Label("Country").OnChange(v => _country = v),
    Ui.Select.Bind(() => _order.Tags).Options(tags).Label("Tags")
]
```

**Toasts: the page owns the list.** One `Ui.Toast` is one notice; `Ui.Toaster` stacks them in a corner
(`Position` + `Align`, newest last so an arriving toast never pushes the one being read out from under the
eye). A toast takes `Title`, an `Action` (an Undo, a link to what was made) and `Duration`.

`Duration` is the interesting one. It does **not** hide the element — it asks the runtime to *click the
toast's own dismiss control*, which runs your `OnDismiss`, which takes the toast off your list. Hiding it
instead would leave the page believing a toast is up that nobody can see, and the next render would put it
back. The countdown **pauses** while the pointer is over the toast or focus is inside it, so reaching for the
action does not lose it, and a toast with no `OnDismiss` writes no timer at all, because there would be
nothing to press.

```csharp
Ui.Toaster.Position(Ui.Position.Bottom).Align(Ui.Align.End)[
    _notices.Select(n => Ui.Toast.Key(n.Id).Message(n.Text)
        .Duration(6.Seconds)
        .OnDismiss(() => _notices.Remove(n)))
]
```

The hook is the **runtime's**, not the kit's, and it is generic: any element with
`data-rask-dismiss-after="<ms>"` is dismissed by clicking its own `[data-rask-dismiss]` — the same convention
the focus trap presses on Escape. An `Ui.Tone.Error` toast says `role="alert"`; every other outcome is
announced politely as `status`. `Error` and `Warning` are drawn with the warning icon, everything else
with a check.

**`Ui.ContextMenu` is the same menu, opened by a right-click.** Its children are the rows a `Ui.Dropdown` takes,
and it is the same control underneath (`UiMenuSurface`), so the keyboard is identical; only the opening differs:

```csharp
Ui.ContextMenu.Target(Div.TabIndex(0).Class("card")["Invoice 42"])[
    Ui.MenuItem.Text("Open").OnClick(Open),
    Ui.MenuSeparator,
    Ui.MenuItem.Text("Delete").Error.OnClick(Delete)
]
```

The runtime opens it: an element carrying `data-rask-contextmenu="<popover id>"` shows that popover at the pointer
in place of the browser's menu, straight away and on either host — a round trip first would be a lag felt on every
right-click — and pulls it back inside the viewport near an edge. The ContextMenu key and Shift+F10 open it at the
focused element, which is why the target above is focusable. Nothing in a context menu should be the ONLY way to
do something: iOS Safari never fires the event, so put the same actions somewhere visible too.

**`Ui.Command` is a command palette.** A search field that opens a dialog of commands — from a click, or from
anywhere on the page with its `Shortcut`:

```csharp
Ui.Command.Label("Search commands").Shortcut("mod+k")[
    Ui.MenuGroup.Title("Invoices")[
        Ui.MenuItem.Text("New invoice").Icon(Ui.IconName.Plus).OnClick(NewInvoice)
    ],
    Ui.MenuItem.Text("Settings").Href(Routes.Settings())
]
```

The commands are the same `Ui.MenuItem`s a dropdown takes. The dialog is the platform's modal `<dialog>`, opened by
the invoker command as `Ui.Modal` is. Inside, the search box is a `combobox` over a `listbox` whose options are the
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

**`Ui.Popover` is a panel, not a menu.** `Ui.Dropdown` IS a menu — its children are rows you pick from, it says
`role="menu"` and it walks a keyboard cursor over them. A filter panel, a colour picker or a bubble of help is
none of those, and putting one in a menu tells a screen reader it is a list of commands and traps the arrow
keys inside it. `Ui.Popover` is the same machinery — a `[popover]` the browser lifts into the top layer and
dismisses on Escape and on a click outside, placed with the same `Position`/`Align` — with `role="dialog"` and
ordinary Tab movement inside.

**`Ui.Chart` draws lines, areas and bars as SVG on the server.** No script and no chart library. The series arrive
through a factory whose parameter is the chart, as a data grid's columns do, which is what gives each lambda its
row type:

```csharp
Ui.Chart.Data(months).Label("Revenue and costs").Format("C0").Class("h-64")[c => [
    c.X(m => m.Name),
    c.Area(m => m.Revenue).Label("Revenue"),
    c.Line(m => m.Costs).Label("Costs").Tone(Ui.Tone.Warning),
    showOrders ? c.Bar(m => m.Orders).Label("Orders") : null
]]
```

`Line`, `Area` and `Bar` read a `double`, `decimal`, `int` or `long` without a cast, and share one value axis whose
ends are round numbers with zero on it. A series with no `Tone` takes the next colour in turn, and a legend appears
once there is more than one. Size it with a height class: the plot stretches to the box while its strokes keep
their width, and the axis labels are HTML beside it so they never stretch. Hovering a column shows that row's
values in CSS. The figure is named by `Label`, the drawing is hidden from assistive technology, and a visually
hidden table carries every value it draws — series as columns, rows as rows.

**`Ui.Link.External()` opens in a new tab.** `target="_blank"` with `rel="noopener noreferrer"` (a new tab opened
without it can reach back through `window.opener`). A generated route is one of your own pages and is never
external, so it is ignored there.

**`Ui.Skeleton` is Flux UI's skeleton, part for part.** `Ui.Skeleton` is a block — 16px tall and the width of
its container until the call site sizes and rounds it — `Ui.SkeletonLine` is a line of text (`Base`, or `Lg`
for large text: it keeps the line's full height and draws a bar the height of the letters), and
`Ui.SkeletonGroup` is a `<div>` that draws nothing and animates every skeleton inside it, however deep:

```csharp
Ui.SkeletonGroup.Shimmer.Class("flex items-center gap-4")[
    Ui.Skeleton.Class("size-10 rounded-full"),
    Div.Class("flex-1")[
        Ui.SkeletonLine,
        Ui.SkeletonLine.Class("w-1/2")
    ]
]
```

`Shimmer` carries a band of light across every two seconds and `Pulse` fades to half and back; a skeleton that
states its own (`Ui.Skeleton.Pulse`, `.Animate(Ui.SkeletonAnimate.None)`) does not take its group's. The
shimmer's light is `--ui-shimmer-color` — white, `zinc-900` in dark — so on a surface that is neither, set it
to that surface's colour. The roots are marked `data-ui-skeleton`, `data-ui-skeleton-line` and
`data-ui-skeleton-group`. As in Flux, neither animation stops under `prefers-reduced-motion`, and a skeleton
carries no ARIA of its own: say that the region is loading on the region (`aria-busy`).

**`Ui.Progress` is Flux UI's progress bar.** A `<div role="progressbar">` — a 6px track the width of
its container and the bar filling it — with `Value` (0 when unset), `Max` (100) and `Color` (a `Ui.Color`, any
Tailwind hue; the accent when unset):

```csharp
Ui.Progress.Value(75)
Ui.Progress.Value(3).Max(7)
Ui.Progress.Value(42).Color(Ui.Color.Blue).Class("h-3").Aria("label", "Upload progress")
```

`aria-valuenow` and `aria-valuemax` are the value and maximum exactly as given; the bar is their ratio held
between empty and full, and moves to a new value over 300ms. The same share is on the element as
`--ui-progress` (a number, 0–100) and `--ui-progress-percentage`, for a label drawn from the same figure. It is
a `UiElement`, so name it with `.Aria("label", …)` or `.Aria("labelledby", id)`. There is no radial progress:
Flux has none, and `Ui.RadialProgress` is gone.

**`Ui.Avatar` draws initials when there is no picture.** `Src` is optional; give it a `Name` and it renders the
monogram — the first letter of each of the first two words, deliberately not first-and-last, since a name is
not reliably two words in that order. The letters are `aria-hidden` and the frame carries the name, because
"AL" read letter by letter tells a reader nothing. Same frame, same rounding either way, so a list does not
change shape when somebody removes their photo.

**`Ui.Input` and `Ui.Textarea` are Flux UI's.** Same props, same look, same markers
([fluxui.dev/components/input](https://fluxui.dev/components/input), [textarea](https://fluxui.dev/components/textarea)),
over Rask's binding: `Bind` or `Value` where Flux says `wire:model`.

```csharp
Ui.Input.Bind(() => m.Email).Label("Email").Description("We never share it.")     // a field: label, help, error
Ui.Input.Bind(() => m.Query).Icon(Ui.IconName.MagnifyingGlass).Kbd("⌘K").Clearable().Placeholder("Search...")
Ui.Input.Bind(() => m.Password).Type(InputType.Password).Viewable()                // reveal button
Ui.Input.Value(key).ReadOnly().Filled                                              // variant="filled"
Ui.Input.Bind(() => m.Phone).Mask("(999) 999-9999")                                // 9 digit, a letter, * either
Ui.Input.Of<string>().Type(InputType.File).Multiple().OnFiles(Save)                // "Choose files" + the chosen name
Ui.Input.Of<string>().As(Ui.InputAs.Button).Placeholder("Search...").OnClick(Open) // a button drawn as the input
Ui.InputGroup[Ui.InputGroupPrefix["https://"], Ui.Input.Bind(() => m.Site)]        // fused borders
Ui.Textarea.Bind(() => m.Notes).Label("Notes").Rows(UiTextareaRows.Auto).None      // grows by CSS; resize="none"
```

- **Props.** `Label`, `Description`, `DescriptionTrailing` (and `Badge` on a textarea) wrap the control in a
  [`Ui.Field`](#fields-label-description-error) with its `Ui.Error`; without them it is the control alone. `Size`
  (`Sm`, `Xs`), `Variant` (`Filled`), `Disabled`, `ReadOnly`, `Invalid`, `Icon` / `IconTrailing` (a `Ui.IconName`, or
  content of your own such as a button), `Kbd`, `Clearable`, `Viewable`, `Mask`, `As`, `Multiple`, and
  `Class` for the wrapper with `InputClass` for the `<input>`. A textarea takes `Rows` (4 unless set) and `Resize`
  (`Vertical`, `Horizontal`, `Both`, `None`).
- **A bound control is invalid on its own** while its form holds a message for the member: `aria-invalid`,
  `data-invalid` and the red border, with `aria-describedby` naming the field's error and description.
  `ShowValidation(false)` leaves the message to a `Ui.Error` you place yourself. A control with no label draws no
  field, so its message is yours to place too: `Ui.Field[Ui.Input.Bind(…), Ui.Error]`.
- **No script.** The clear button is hidden by CSS while the input is empty and clears through a handler; the
  reveal button is a handler; `Rows(UiTextareaRows.Auto)` is `field-sizing: content`; a file input is a `<label>`
  around the real input. `Mask` is applied to the value drawn and the value committed, not keystroke by keystroke.
  Flux's `copyable` and `mask:dynamic` need script in the page and are not built.
- **In a group, label the group.** `Ui.Field[Ui.Label["Website"], Ui.InputGroup[…], Ui.Error]` — the group stands
  for its input, so the field's label and error reach it. A neighbour that is not an input (a button, a select)
  joins the outline by carrying `data-ui-group-target`.
- **Gone with daisyUI's input:** floating labels (`Floating`), `Hint` (now `Description`), `Tone`, `Error("…")`
  (now `Invalid()` beside a `Ui.Error.Message("…")`), `AccessibleLabel`, `ShowValidating`, `AutoSize` (now
  `Rows(UiTextareaRows.Auto)`), `Ui.Resize` (now `Ui.TextareaResize`) and `Ui.Search` (now
  `Ui.Input.Icon(Ui.IconName.MagnifyingGlass)`).

**The opening step fixes the type argument and the mode together.** `Bind` opens a bound control and
`Value` a controlled one; they are mutually exclusive because a control with both would have two
sources of truth for one field, and the compiler enforces it — both live on the control's entry, so
taking one leaves the other unreachable. `Label`, `Options` and the rest follow in any order, since none
of them says anything about `T`. Bound mode drives the surrounding `Form`'s validation — per-field
`Validate`, `AfterBind`, and the `aria-invalid`/`aria-describedby` display — and controlled mode leaves
the value with the parent. See [building form controls](building-form-controls.md).

**The controls still on daisyUI share one field shape.** `Ui.Select` (single or multiple), `Ui.Otp`,
`Ui.FileInput` and the radio and checkbox groups all take the same members from
`UiFormField<T>`, until each is rebuilt on Flux as `Ui.Input` and `Ui.Textarea` have been: a visible `Label` (a `<label for>` over the control, with an optional `Badge` beside it) or,
without one, an invisible `AccessibleLabel`; a `Hint` and a controlled `Error` under it; an `Id`, derived from the
bound member or the label when you give none; and `aria-describedby`, `aria-invalid` and `aria-required` worked out
from those and from the bound member's `[Required]` and messages. `Label` is never a required step, so write it
anywhere after the opening — `Ui.Otp.Value(code).Length(6).Label("Verification code").Hint("Sent to your phone")`.

**Generic where the value type varies, concrete where it does not.** `UiInput<T>`, `UiTextarea<T>`,
`UiSelect<T>` and `UiFilter<T>` are generic — the model decides what they hold, and `Ui.Input` even
takes its `type` attribute from `T`, so a bound `int` is a number field with nothing said at the call
site. The rest are closed over the one type they can have: `Ui.Checkbox`, `Ui.Toggle` and `Ui.Radio` over
`bool`, `Ui.Range` over `double`, `Ui.Rating` over `int`, `Ui.Otp` and `Ui.FileInput` over `string`,
`Ui.Calendar` and `Ui.DatePicker` over `DateOnly`, a collection of days or a `UiDateRange`. A checkbox's value is a `bool` and nothing else; a type parameter there
would have exactly one legal argument.

| | Binds |
|---|---|
| `UiInput<T>` `UiTextarea<T>` `UiSelect<T>` | what the field holds |
| `UiSelect<T>` over a collection | the ELEMENT type — it binds an `ICollection<T>` |
| `UiFilter<T>` | the chosen option of a whole radio group |
| `Ui.Radio` | whether **this** option is the chosen one — the group's value belongs to `UiFilter<T>` |
| `Ui.Checkbox` `Ui.Toggle` | on or off |
| `Ui.Range` `Ui.Rating` `Ui.Calendar` | the position, the star count, the day |
| `Ui.Otp` | the code — `OnComplete` fires on the transition into a full one, in both modes |
| `Ui.FileInput` | the chosen file's name, **write-only** — a browser refuses to have a file input's value set, so binding fills the model and never the box. The bytes come through `OnFiles`. |


**A file drop area is the same file input.** `Ui.FileInput.Dropzone()` draws Flux UI's large area in place
of the compact box, with `Title` (the `Label` by default) and `Text` for what is accepted:

```csharp
Ui.FileInput.Value("").Label("Receipts")
    .Dropzone()
    .Title("Drop receipts here, or click to choose")
    .Text("PDF or JPG, several at once")
    .Accept(".pdf,.jpg")
    .Multiple()
    .OnFiles(files => _receipts.AddRange(files.Select(f => f.Name)))
```

The native input is stretched invisibly over the whole area, so a click anywhere opens the picker and a file
dropped anywhere lands in the input — the browser already turns a drop on a file input into a chosen file, so no
script decides where a drop goes and it works before the runtime boots. The one thing CSS cannot say is "a file is
being dragged over this", so the runtime sets `data-dragging` on the nearest `[data-rask-dropzone]` while a drag
carrying files is over it, and the area styles itself from that. `Text` is the input's `aria-describedby`, ahead of
any `Hint` or validation message. The heading is the area's caption, so a dropzone draws no legend over it.

**A field with no value yet opens on its type alone**: `Ui.Input.Of<string>().Label("Search")`. A form
control's openings are its mode pins, so a required step like `Label` never gets to pin `T` — without
`Of` a controlled field with nothing in it would have to invent a value to compile. `Of` is the
controlled mode: the parent still owns whatever the field ends up with.

### Calendar

`Ui.Calendar` is Flux UI's calendar: a month of day buttons in a grid, with the month steps over it.

**A day, several days or a range is Flux's `mode`, taken as the step that opens the calendar.** No step is
Flux's default, one day; `.Multiple` and `.Range` hand back the calendar that binds that mode's type, so a range
bound to a single day does not compile:

```csharp
Ui.Calendar.Bind(() => model.Delivery)                     // single (Flux's default): DateOnly or DateOnly?
Ui.Calendar.Multiple.Bind(() => model.DaysOff)             // mode="multiple": List<DateOnly>, HashSet<DateOnly>, …
Ui.Calendar.Range.Bind(() => model.Stay)                   // mode="range": UiDateRange or UiDateRange?, two months
Ui.Calendar.Value(day).OnChange(d => day = d)              // one day, controlled
Ui.Calendar.Multiple.Values([monday, friday])              // several, controlled
Ui.Calendar.Range.Value(new UiDateRange(from, to))         // a range, controlled
Ui.Calendar.Value(default(DateOnly))                       // nothing chosen yet; it keeps the pick itself
Ui.Calendar.Mode(mode).Bind(() => model.Stay)              // the mode as a value (Ui.CalendarMode)
```

`Ui.Calendar.Single` is `Ui.Calendar`. A mode given as a value — `Ui.Calendar.Mode(Ui.CalendarMode.Range)`, or
`.Mode(…)` / Flux's bare `.Multiple()` on an opened calendar — has to agree with what is bound; when it does not,
rendering throws an `InvalidOperationException` that names both ("Ui.Calendar is in Range mode but what it binds
is Single's (a DateOnly). Open it with Ui.Calendar.Range and bind a UiDateRange.").

A second click on the chosen day clears it (`default(DateOnly)`, or `null` through a nullable binding).
`UiDateRange(Start, End)` is always whole: the first click is held by the calendar and drawn as the start, the
stretch to the day under the pointer or the keyboard is drawn as it would be, and the second click — on that day
or a later one — writes the range; a click before the start begins again from there. `Count`, `Contains(day)`
and `Between(a, b)` read it, and `default(UiDateRange)` is nothing chosen.

| Step | Flux | |
|---|---|---|
| `Min(day)` `Max(day)` | `min` `max` | days outside are disabled, and a month step is too once the whole month that way is out of reach (`min="today"` is `Min(DateOnly.FromDateTime(DateTime.Today))`) |
| `Unavailable([..])` | `unavailable` | struck through and disabled |
| `MinRange(3)` `MaxRange(10)` | `min-range` `max-range` | while a range waits for its end, the days that would make it too short or too long are disabled, and so is every day before the start |
| `.Xs` `.Sm` `.Lg` `.Xl` `.Xxl` | `size` | 36, 40, 48, 56 and 64px cells; 44px unset |
| `StartDay(DayOfWeek.Monday)` | `start-day` | the locale's own first day unless set |
| `Months(2)` | `months` | side by side; one, or two for a range |
| `OpenTo(day)` `ForceOpenTo()` | `open-to` `force-open-to` | the month shown while nothing is chosen — or whatever is chosen |
| `Navigation(false)` `Static()` | `navigation` `static` | no month steps; a calendar to look at, with no buttons |
| `WeekNumbers()` `FixedWeeks()` | `week-numbers` `fixed-weeks` | the ISO week of each row; always six rows |
| `SelectableHeader()` `WithToday()` | `selectable-header` `with-today` | month and year selects; a shortcut that comes back to this month, then picks today |
| `Locale("ja-JP")` | `locale` | month, weekday and day names, and the first day of the week; the app's culture ([localization](localization.md)) unless set |

**Keyboard.** One day is in the Tab order — the chosen one, else today, else the 1st. The arrows walk days and
weeks, stepping over disabled days and into the neighbouring month; PageUp/PageDown and Home/End page a month;
Enter or Space picks. Each cell is a `gridcell` with `aria-selected` and its button carries the full date as its
name ("Thursday, January 15, 2026").

### Date picker

`Ui.DatePicker` is Flux UI's date picker: a field-shaped button showing the choice ("Jan 20, 2026"), with the
calendar in a popover — the browser's, so the top layer, Escape, a click outside and focus back on the button come
with it. A day closes it on the pick, a range on its second click.

```csharp
Ui.DatePicker.Bind(() => booking.Arrival).Label("Arrival")                         // DateOnly
Ui.DatePicker.Range.Bind(() => booking.Stay).Label("Stay").Min(today).MinRange(3)  // mode="range": UiDateRange
Ui.DatePicker.Bind(() => booking.Arrival).Type(Ui.DatePickerType.Input)            // month / day / year fields to type into
Ui.DatePicker.Range.Bind(() => report.Range).WithPresets().Min(new DateOnly(2012, 1, 1)) // Today, Yesterday, This Week, …, All Time
Ui.DatePicker.Range.Bind(() => report.Range)
    .Presets([Ui.DateRangePreset.Today, Ui.DateRangePreset.Last30Days, Ui.DateRangePreset.Custom])
Ui.DatePicker.Range.Bind(() => booking.Stay).Trigger(
    Div.Class("flex gap-4")[Ui.DatePickerInput.Label("Start"), Ui.DatePickerInput.Label("End")])
```

It takes the calendar's steps (`Min`, `Max`, `Unavailable`, `MinRange`, `MaxRange`, `Months`, `OpenTo`,
`ForceOpenTo`, `StartDay`, `WeekNumbers`, `SelectableHeader`, `WithToday`, `FixedWeeks`, `Locale`), a field's
(`Label`, `Description`, `DescriptionTrailing`, `Badge`, `Invalid()`, `Disabled()`, validation through the form), and
its own: `Placeholder`, `Size` (the calendar's cells), `WithConfirmation()` (nothing is written until "Select
date"), `WithPresets()` / `Presets([..])`, and `Trigger(…)` for a trigger of your own built from
`Ui.DatePickerButton` or `Ui.DatePickerInput`. A preset writes `UiDateRange.Of(preset, today, startDay, Min)`,
whose `Preset` names it, and the button then shows the preset's label. There is no multiple mode, as in Flux:
several days are `Ui.Calendar`'s.

### Time picker

`Ui.TimePicker` is Flux UI's time picker: a button showing the chosen time (or, with `.Type(Ui.TimePickerType.Input)`,
hour / minute / AM-PM fields to type into) over a list of times in a popover.

```csharp
Ui.TimePicker.Bind(() => model.StartsAt).Label("Starts at")              // TimeOnly? — one time or none
Ui.TimePicker.Bind(() => model.Slots)                                     // List<TimeOnly> — several; the list stays open
Ui.TimePicker.Value(time).OnChange(t => time = t).Interval(15).Min(new(9, 0)).Max(new(17, 0))
Ui.TimePicker.Of<TimeOnly?>().Unavailable([new TimeOnly(12, 0), new UiTimeRange(new(13, 0), new(13, 59))])
```

The bound type says how many it picks: `TimeOnly?` one or none, `TimeOnly` always one, a collection of `TimeOnly`
several (Flux's `multiple`). Steps: `Interval` (minutes, 30), `Min`/`Max`, `Unavailable` (times and `UiTimeRange`
stretches, listed but disabled), `OpenTo`, `.TwelveHour`/`.TwentyFourHour` (the culture's own clock unless set),
`Locale("ja-JP")`, `Placeholder`, `.Sm`/`.Xs`, `Clearable()`, `Disabled()`, `Invalid()`, `Dropdown(false)` (typed
trigger without its list), and `Label`/`Description`/`DescriptionTrailing`/`Badge` for the field around it. The button
is the combobox and keeps focus: arrows move a cursor through the list, Enter picks, Escape closes.

## The rule the whole kit rests on

daisyUI emits a component's CSS only where Tailwind can **see** its class name in the scanned source.
A name built by concatenation — `"btn-" + tone` — is a name no scanner ever reads, so the class is
absent from the compiled sheet and the component renders **with no styling whatsoever**. Not
misaligned, not the wrong colour: unstyled. The build stays green and the markup carries exactly the
class the call site asked for.

That is why every class the kit can write is spelled out as a complete literal in `UiClassNames.cs`,
why the axes above are closed enums rather than strings, and why `Ui.TextRotate` has no `Interval`
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

**The whole kit hangs off one class, `Ui`.** Typing `Ui.` lists every component and every option it takes:

```csharp
using Rask;   // every template's GlobalUsings.cs already says this

Ui.Card[
    Ui.Input.Of<string>().Label("Email").Description("We never share it"),
    Ui.Button.Primary.Sm.OnClick(Save)["Save"],
    Button["a plain <button>"]
]
```

A component is reached through `Ui` and never by a bare name of its own, so the kit cannot shadow an HTML
tag: `Ui.Button` is the kit's button, `Button` the `<button>`, `Ui.Select` the kit's select, `Select` the
`<select>`. The options are nested in the same class — `Ui.Tone`, `Ui.Size`, `Ui.Variant`, `Ui.IconName`, `Ui.IconVariant`.

The component **types** keep the `Ui` prefix — `UiButton`, `UiDataGrid<T>` — because a type and a member of
one name cannot both sit on `Ui`. That is the name a field, a parameter, a hover or a compiler message uses;
the call site never needs it.

`Ui` lives in the `Rask` namespace, so `using Rask;` is all it takes — and a bare `Ui` means this class from
inside any `Rask.*` namespace as well, which a `Rask.Ui` namespace would have shadowed.

Each component lives in a file named after it, so the file list in `src/Rask.Ui` is the component list.

## See also

- [Dashboard](dashboard.md) — the operator console this kit was extracted from
- [Tailwind](tailwind.md) — how the compiler is wired into a Rask build
- [Data grid](data-grid.md) — `Ui.DataGrid`, whose columns arrive through a factory and whose row key is required
- [Tree](tree.md) — `Ui.Tree`, whose children arrive through the indexer and whose cursor is one focusable element
- [Building components](building-components.md) — the chain the kit is composed with
