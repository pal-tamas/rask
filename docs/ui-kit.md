# The UI kit (`Rask.Ui`)

**Every daisyUI component, as a typed Rask component.** The kit wraps
[daisyUI](https://daisyui.com) 5 — vendored, compiled at the kit's own build, and shipped inside the
package — so a Rask app gets the whole component vocabulary without a single utility string, an npm
install, or a Tailwind configuration of its own.

It is **markup and nothing else**: no data access, no host dependency, and no JavaScript — with one exception, the
[rich text editor](#rich-text-editor), whose engine is a script a page loads only when it draws one. It runs on
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

> **An app on `RaskApp` or the WASM host needs none of this in its code.** `rask new` writes the one
> import below into `Styles/app.css`; the host links the compiled `css/app.css` and puts the theme scope
> on `<html>`, so `App.cs` is a title and a router. `app.Configure(c => c.Ui.Off())` leaves the kit out.
> This section is for a hand-wired `AddRask()`/`MapRask<App>()` host, or an App that overrides `Shell`.

Two things, and forgetting either produces a page that renders structurally correct components with
**no styling, or no colour at all** — so both are worth doing before anything else.

**1. Take the kit into your stylesheet.** One line in `Styles/app.css`, in place of
`@import "tailwindcss";`:

```css
@import "./vendor/rask-ui.css";
```

Your app then compiles **one stylesheet** — Tailwind, the kit's theme, its `dark` variant, daisyUI while
it lasts, and the classes the kit's components write, beside the classes you write — exactly as a
[Flux](https://fluxui.dev) app's one Tailwind build scans Flux's own views. The kit's class names are
C# string literals inside a compiled assembly, where no scan can find them, so the package ships them
as a list and the import reads it. Link that sheet and nothing else:

```csharp
protected override Component? HeadAssets =>
[
    Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/css/app.css"),
];
```

There is nothing to set in the `.csproj`. The build sees the import, writes the kit's Tailwind sources
into `Styles/vendor/` before Tailwind runs — `rask-ui.css`, `rask-ui.kit.css`, `rask-ui.classes.txt`
and `daisyui.mjs`; generated, not committed, no npm — and records in the assembly that the kit is
already in the app's sheet. [The Tailwind guide](tailwind.md#what-a-new-project-starts-with) walks
through the four lines of `rask-ui.css`, and how to write them out yourself when you want Tailwind
without its preflight or a layer of your own.

**2. Turn the theme on.** Nothing in the kit has a colour until an ancestor carries the theme scope:

```csharp
protected override Component Shell(Component head, Component body) =>
    Html.Lang("en").Attributes((UiStylesheet.ThemeScopeAttribute, ""))[head, body];
```

The scope exists so that *referencing* this package cannot repaint an application that only wanted a
button. daisyUI paints `:root` by default; the kit confines it to `[data-rask-ui]` instead.

**The layer order is one statement, and the import owns it.** `rask-ui.css` opens with

```css
@layer properties, theme, base, components, daisyui, rask, utilities;
```

which puts your utilities above Tailwind's preflight, above daisyUI, and above the kit's own rules — a
browser orders `@layer` names by *first appearance* and nothing later can reorder a name already
placed, which is why the import is the first line of your sheet. `OneStylesheetCascadeTests` holds the
order in a compiled app sheet, and `UiLayerOrderTests` in the kit's precompiled one.

### One sheet, never two

The kit also compiles a sheet of its own (`UiStylesheet.Css`, or `UiStylesheet.Href()` after
`<RaskUiWriteStylesheet>true</RaskUiWriteStylesheet>` writes it to `wwwroot/css/rask-ui.css`). **That
sheet is for a surface with no Tailwind build of its own** — `Rask.DevTools` inlines it into a panel
drawn inside somebody else's page; an app that draws only with `Ui.*` components and writes no
utilities can link it and run no Tailwind at all.

It must not sit in a document beside an app's own Tailwind output, which is how every Rask app used to
be wired. Both sheets put utilities in `@layer utilities`, CSS layers do not merge across `<link>`
elements in any way that source order inside a sheet can settle, and the kit's `dark` variant is a
`:where()` with no specificity of its own. So between a kit **variant** and any base utility the app
wrote *anywhere*, layer and specificity tie and link order decides — the app's sheet, linked last:

| the kit's component writes | the app writes, on some other element | computed, with two sheets |
|---|---|---|
| `bg-white dark:bg-white/4` (Flux's card) | `bg-white` | `rgb(255, 255, 255)` in dark mode |
| `text-zinc-500 dark:text-zinc-300` | `text-zinc-500` | zinc-500 in dark mode |
| `flex-col sm:flex-row` | `flex-col` | a column at every width |

Measured on rask.sh, with every class in the markup correct and every gate green. In one sheet each of
those utilities exists once and Tailwind's own order holds — a base utility before its variants, a
shorthand before its longhands — so the build **refuses** the pairing: `RaskUiWriteStylesheet` in a
project that compiles its own Tailwind stylesheet is an error that names the line to write instead.

## Writing daisyUI class names yourself

Nothing more to do. The import in step 1 loads daisyUI's plugin into **your** Tailwind build, so a
`card-body` or `navbar` you write in your own markup is compiled from your own source, in the same
sheet as the kit's components and under the same scoped theme — one copy of daisyUI, not two. (An app
that linked the kit's precompiled sheet had to run the plugin a second time for its own markup, with
its own layer statement and a `@source not` to keep the bundle from being scanned as a safelist. All of
that is inside `rask-ui.kit.css` now.)

The plugin is the copy `Rask.Ui` ships (`Styles/vendor/daisyui.mjs`), loaded by relative path because
Tailwind resolves a plugin the way Node does and the standalone engine a C# host compiles with carries
no package tree — so there is still no npm and no `node_modules`.

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

The `link-*` tones render daisyUI classes, and
daisyUI labels each tone with its own `-content` colour — generated for 3:1, so small text on them fails
AA on between two and ten palettes per tone (`secondary` is 3.05:1 on daisyUI's own `dark`, `error`
under AA on ten). The kit corrects them to the `-ink` fill with the ground as the label, in
`@layer rask-ui-corrections`, pointing at the tokens above so the per-theme corrections apply for free.

Two consequences worth knowing:

- **It is all custom properties** (`--btn-color`) except where daisyUI declares
  `color` outright — alert and link. Those two therefore also outrank your own `text-*`
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
Ui.Button.Icon(Ui.IconName.Cog6Tooth).Tooltip("Settings")     // a tooltip names it too: Flux's wrapper, by aria-labelledby
Ui.Button.Icon(Ui.IconName.Moon).Tooltip("Toggle dark mode").TooltipKbd("D").TooltipPosition(Ui.TooltipPosition.Bottom)
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
| `Tooltip`, `TooltipPosition`, `TooltipKbd` | Flux's shorthand: the button is wrapped in a [`Ui.Tooltip`](#tooltips) that says this, on that side (`Ui.TooltipPosition`), with that shortcut after it. It names a button that has only an icon (`aria-labelledby`), and still fuses inside a `Ui.ButtonGroup` |
| `Kbd` | a shortcut drawn inside the button, after its label: `Ui.Button.Kbd("esc")["Cancel"]` |
| `Href` | see [Buttons and links that go somewhere](#buttons-and-links-that-go-somewhere) |
| `As` | `Ui.ButtonAs.Div` for the look of a button on something that is not one |
| `Type`, `Disabled`, `Command`, `CommandFor` | the `<button>`'s own attributes |

`Ui.Tone`, `Ui.Variant` and `Ui.Size` do not apply to it: a button has Flux's variants, not daisyUI's tones.
A selected toggle is `.AriaPressed(AriaPressed.True)` on the variant that reads as selected — `Filled`.

## The three axes

On the components daisyUI still draws, colour, fill and size are independent and compose, so a small
error-toned range needs no member of its own:

```csharp
Ui.Range.Bind(() => settings.Volume).Tone(Ui.Tone.Error).Size(Ui.Size.Sm)
```

| Enum | Members |
| --- | --- |
| `Ui.Tone` | `Neutral` `Primary` `Secondary` `Accent` `Info` `Success` `Warning` `Error` |
| `Ui.Variant` | `Solid` `Outline` `Soft` `Dash` `Ghost` `Link` |
| `Ui.Size` | `Default` `Xs` `Sm` `Md` `Lg` `Xl` |

These are daisyUI's own words, deliberately. Translating them into a private vocabulary was the first
thing this kit did and the first thing it stopped doing: daisyUI's documentation is the documentation
for everything the components render, and a second set of words made every example a translation.

Not every component honours every member — daisyUI defines no `input-outline` — and **a member a component has no class for writes nothing**, rather than a class that would sit in
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
Ui.Tooltip.Content("Copy").Right[ … ]   // Flux's own Ui.TooltipPosition / Ui.TooltipAlign — see Tooltips
Ui.Tabs.Position(Ui.Position.Bottom)[ … ]
Ui.Modal.Name("details").Flyout().Left[ … ]   // a flyout is placed against the viewport: Ui.ModalPosition
```

Events are always `On…` — `Ui.Modal.OnClose`, `Ui.Modal.OnCancel` — the same prefix every
element event carries. A `<dialog>`'s own endings are element events too: `Dialog.OnCancel` for a dismissal and
`Dialog.OnClose` for any close.

### We style, you space

A kit component brings its padding, its border and its colours, and **never an outer margin**. Where it
sits — the gap above a row of tabs, the bleed of a scrolling strip to the screen edge — belongs to the
page that places it, because the same component sits in a card, a toolbar and a page gutter, and a margin
right for one is wrong for the other two. Two exceptions are part of a component's shape rather than its
placement: `Ui.NavTab`'s `-mb-px`, which joins the active tab's border to its nav's hairline, and
`Ui.Toast`'s 24px from the viewport's edges, which is where Flux puts a toast.

## Components that are one element

A button is a `<button>`, and a table cell is a `<td>`. `Ui.Button`, `Ui.Badge`, `Ui.BadgeClose`, `Ui.List` and
the parts of a `Ui.Table` do not wrap a raw element; they are the element. They derive from **`UiElement`**, which derives
from `Element`, so every step an element takes works on them unchanged, the events included. What they
show is their **children**, the same as a raw element's:

```csharp
Ui.Button.Primary.Icon(Ui.IconName.Check).Id("save").OnClick(Save)["Save"]

Ui.Badge.Color(Ui.Color.Green)["Live"]

Ui.TableCell.Id("total").Class("py-0")[Ui.Badge.Sm.Green["Paid"]]
```

A button and a badge take their icons as props and size them themselves. Inside a hand-written daisy `btn` a
bare `Ui.Icon.Name(…)` is the right size too: the kit's stylesheet sizes an icon nobody sized from the button
it sits in, and leaves alone one that has a size class of its own.

## Badges

`Ui.Badge` is [Flux's badge](https://fluxui.dev/components/badge), prop for prop and pixel for pixel: a
status, a category or a count. It is a `<div>` whose children are what it says.

```csharp
Ui.Badge["Draft"]                                   // zinc, 14px type in a 28px badge
Ui.Badge.Color(Ui.Color.Lime)["New"]                // any Tailwind colour
Ui.Badge.Solid.Color(Ui.Color.Red)["3"]             // the colour itself under white text
Ui.Badge.Sm["Small"]   Ui.Badge.Lg["Large"]         // Ui.BadgeSize: Base, Sm, Lg
Ui.Badge.Rounded().Icon(Ui.IconName.User)["Users"]  // round ends; an icon before the words
Ui.Badge.IconTrailing(Ui.IconName.VideoCamera)["Videos"]

// The whole badge pressed: a <button type="button">, and OnClick is the element's own.
Ui.Badge.As(Ui.BadgeAs.Button).Rounded().Icon(Ui.IconName.Plus).Lg.OnClick(Add)["Amount"]

// Removable: a close button among its children.
Ui.Badge[role, Ui.BadgeClose.Aria("label", "Remove " + role).OnClick(() => Remove(role))]

// In a line of text, the padding is given back so the line is no taller.
Ui.Heading["Page builder ", Ui.Badge.Color(Ui.Color.Lime).Inset(Ui.Inset.Top | Ui.Inset.Bottom)["New"]]
```

| Prop | Values | Unset |
| --- | --- | --- |
| `Color` | `Ui.Color` — Tailwind's seventeen hues `Red` … `Rose`, then `Slate` `Gray` `Zinc` `Neutral` `Stone` | zinc |
| `Size` | `Ui.BadgeSize.Base` · `Sm` · `Lg` (steps `.Sm`, `.Lg`) | `Base` |
| `Variant` | `Ui.BadgeVariant.Soft` · `Solid` (steps `.Soft`, `.Solid`) | `Soft` |
| `Rounded` | `.Rounded()` | square-ish, 6px |
| `Icon`, `IconTrailing` | `Ui.IconName` | none |
| `IconVariant` | `Ui.IconVariant` | `Micro` (16px) |
| `As` | `Ui.BadgeAs.Div` · `Button` | `Div` |
| `Inset` | `Ui.Inset.Top` · `Bottom` · `Left` · `Right`, combined with `\|` | none |

`Ui.BadgeClose` takes `Icon` (unset, `XMark`) and `IconVariant`. Like Flux's it writes no accessible name
of its own: name it at the call site, `.AriaLabel("Remove " + role)`. A badge holds its words on one line; for
a long token that has to break instead — a request id in a table cell — hand it the utilities,
`.Class("font-mono max-w-full break-all whitespace-normal!")`.

`.Button` and `.Div` are not steps on a badge (they are the HTML entries a component inherits): write
`.As(Ui.BadgeAs.Button)`. `.Outline`, `.Mini` and `.Micro` on a badge set its ICON's variant.

There is no tone: a status names its colour. `Green` for success, `Red` for an error, `Yellow` for a warning
(the hue Flux's own warning callout uses) and `Blue` for information are what the kit's own surfaces use.

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

`Expanded()` is the state an item starts in. As in Flux, the accordion reports nothing back: the browser
owns which items are open.

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
its `Kbd` INSIDE the tag, beside the label. A part that needs markup around its tag renders that markup
with its own tag inside, as `Ui.Button` does once it has a `Tooltip`. The two other places this shows:

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
            Ui.TableCell.Class("py-0")[Ui.Badge.Sm.Green["Paid"]],
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

## Tooltips

`Ui.Tooltip` is [Flux's tooltip](https://fluxui.dev/components/tooltip): a line of help beside whatever it
wraps, shown while that is hovered or has keyboard focus. Its first child is the trigger.

```csharp
Ui.Tooltip.Content("Settings")[Ui.Button.Icon(Ui.IconName.Cog6Tooth)]   // above, centred, 5px away

Ui.Tooltip.Content("Settings").Right[ … ]            // .Top (default) .Right .Bottom .Left — Ui.TooltipPosition
Ui.Tooltip.Content("Settings").Bottom.Start[ … ]     // .Center (default) .Start .End        — Ui.TooltipAlign
Ui.Tooltip.Content("Save").Kbd("⌘S")[ … ]            // a shortcut after the text
Ui.Tooltip.Content("Settings").Gap(12).Offset(20)[ … ]   // pixels: away from the trigger, and along its side
Ui.Tooltip.Content("Settings").Disabled(!ready)[ … ]     // never shows

// More than a line of text, and reachable on a phone: Toggleable opens it on a click.
Ui.Tooltip.Toggleable()[
    Ui.Button.Sm.Ghost.Icon(Ui.IconName.InformationCircle),
    Ui.TooltipContent.Class("max-w-[20rem] space-y-2")[
        P["For US businesses, enter your 9-digit Employer Identification Number (EIN) without hyphens."],
        P["For European companies, enter your VAT number including the country prefix (e.g., DE123456789)."]
    ]
]
```

It looks and behaves as Flux's does — measured on fluxui.dev, in light and dark, by
`scripts/flux/parity-tooltip.mjs`: zinc-800 with white 12px text (zinc-700 inside a hairline in dark), 5px
from its trigger, no arrow, no shadow, no transition and **no delay**; it flips to the other side at the
viewport's edge and slides along it to stay 5px inside; the pointer cannot rest on it; pressing the trigger
hides it. One look — the daisyUI tones are gone.

**It is announced.** The kit wires the trigger to the tooltip the way Flux's script does, at render: a trigger
with text of its own gets `aria-describedby`, and one without — an icon button — gets `aria-labelledby`, so
the tooltip is its name and it needs no `AccessibleLabel`. `Interactive()` says the content is more than a
description: the trigger gets `aria-controls` instead and the content stays in the reading order. A
`Toggleable()` tooltip is Flux's dropdown to a screen reader: its button gets `aria-controls` and
`aria-haspopup`, and the content is no `role="tooltip"`. This needs the trigger to be ONE element — an HTML
element (`Button[…]`, `A[…]`, `Span[…]`) or a kit component that is one, like `Ui.Button`. Around anything
else nothing is wired, as Flux wires nothing but the trigger: use an element.

**The kit ships no script; the runtime shows it.** The content is a `popover` placed by CSS anchor
positioning, and the wrapper carries
[`data-rask-tooltip="<content id>"`](js-interop-runtime.md#pointer-opened-popovers), one of the runtime's
generic hooks. So every tooltip, whatever its trigger — a button, a link, a `Span`, a disabled button — is
in the top layer, where no card can clip it, and behaves as Flux's does, step for step (`parity-tooltip.mjs`
walks both): shown the moment the pointer arrives or keyboard focus does, gone the moment the pointer leaves
unless that focus is still there, dismissed by Escape, and hidden by a press on the trigger until the pointer
has left and come back. An `Interactive()` trigger's `aria-expanded` is `false` at rest and `true` while it
shows.

| Tooltip | Opened by | Escape |
| --- | --- | --- |
| any trigger | the runtime — pointer and keyboard focus | dismisses it |
| `Toggleable()` around a `<button>` | the browser — `popovertarget`, a click; a hover does nothing | closes it; so does a click outside |
| `Toggleable()` around anything else | focus on the wrapper, which a tap gives it — in place, not in the top layer | does nothing |

What is left: a `Toggleable()` tooltip around something that is not a `<button>` has no click to open it by
(no runtime hook toggles a popover from an arbitrary element), and a toggleable button's expanded state is
the browser's own rather than a written `aria-expanded`. On a page whose scripts never run
(`@media (scripting: none)`) the stylesheet shows a tooltip in place on `:hover` and keyboard focus.

## Rich text editor

`Ui.Editor` is [Flux's editor](https://fluxui.dev/components/editor): a toolbar over an editable area, and
a value that is HTML. Its engine is [Tiptap](https://tiptap.dev) on [ProseMirror](https://prosemirror.net),
as Flux's is.

```csharp
Ui.Editor.Bind(() => _post.Body).Label("Release notes").Description("Explain what's new in this release.")

Ui.Editor.Value(_html).OnChange(html => _html = html)          // unbound: you keep the value
Ui.Editor.Placeholder("Write something...")                    // shown while the document is empty
Ui.Editor.Toolbar("heading | bold italic underline | align ~ undo redo")   // | separator, ~ spacer
Ui.Editor.Disabled(locked)                                     // read-only, toolbar off
Ui.Editor.Invalid(!ok)                                         // error styling
Ui.Editor.Class("**:data-[slot=content]:min-h-[100px]!")       // the area is 200–500px tall unless you say
```

The value is the document as HTML — `<p>Hello <strong>world</strong></p>` — and an empty document is the
empty string. `Bind` writes it to the model on every change and validates the field; `Value` with
`OnChange` leaves it to you. A value the app changes afterwards is shown in the editor.

**Nothing to configure.** The engine is 374 KB (117 KB gzipped), so it is in neither Rask's runtime nor
the kit's assembly: it is a static file, `wwwroot/js/rask-ui-editor.js`, which the build of every app that
references the kit writes — add it to `.gitignore` — and which the browser fetches the first time an
editor mounts. A page without an editor never requests it, and nothing preloads or precaches it; an app
that will never draw one can keep it out of its publish folder:

```xml
<PropertyGroup>
  <RaskUiEditorEngine>false</RaskUiEditorEngine>
</PropertyGroup>
```

The host has to serve its static files, as every Rask app does (`RaskApp`, `MapRaskSpa`, a static host for
a browser-WASM publish). A host that is not a Web or WebAssembly SDK project sets the same property to
`true`.

Until the script has loaded — and with scripting off — the editor shows its value as plain markup. A
strict `Content-Security-Policy` needs nothing added: the file is same-origin script.

**Third-party code.** The engine bundles Tiptap 2.11.7 and ProseMirror (47 packages, all MIT), and eleven
of the toolbar's icons are drawn from [Lucide](https://lucide.dev) 0.300.0 path data (ISC), as Flux's are.
Their notices are `rask-ui-editor.LICENSES.txt`: in the `Rask.Ui` package, and written beside the script
in `wwwroot/js`, so they travel with the copy your app serves.

**The value is the user's HTML.** The editor itself only produces the tags of its schema (paragraphs,
headings, lists, quotes, code, links, marks), but what you bind may have come from anywhere — a database
row, an import, a request made by hand. `Ui.Editor` writes its value into the page as it is, exactly as
`Raw` does. So: sanitize HTML on the server before you store it or bind it, and never render a stored
value with `Raw` without doing so. Showing it as text (`Pre[_post.Body]`) is always safe.

### Toolbar

The default is `heading | bold italic strike | bullet ordered blockquote | link | align`. Every item:

| Item | Does | Shortcut |
|---|---|---|
| `heading` | text, or a heading of level 1–3, from a list | `Ctrl`+`Alt`+`0`…`3` |
| `bold` `italic` `strike` `underline` | the mark | `Ctrl`+`B`, `I`, `Shift`+`S`, `U` |
| `subscript` `superscript` `highlight` `code` | the mark | `Ctrl`+`,` `.` `Shift`+`H`, `E` |
| `bullet` `ordered` `blockquote` | the block | `Ctrl`+`Shift`+`8`, `7`, `B` |
| `link` | a panel with the address, a button to set it and one to remove it | `Ctrl`+`K` |
| `align` | left, center or right, from a list | `Ctrl`+`Shift`+`L`, `E`, `R` |
| `undo` `redo` | history | `Ctrl`+`Z`, `Ctrl`+`Shift`+`Z` |

`Cmd` on a Mac. The toolbar is one tab stop; the arrow keys walk its controls. Markdown works while
typing: `#`, `##`, `###`, `**bold**`, `*italic*`, `~~strike~~`, `-`, `1.`, `>`, `` `code` ``, three
backticks for a code block and `---` for a rule.

For anything the list cannot say, compose the editor from its parts — each item is a component
(`Ui.EditorBold`, `Ui.EditorHeading`, `Ui.EditorLink`, `Ui.EditorSeparator`, `Ui.EditorSpacer`, …), and
`Ui.EditorButton` is a button of your own:

```csharp
Ui.Editor.Bind(() => _post.Body)[
    Ui.EditorToolbar[
        Ui.EditorHeading, Ui.EditorSeparator,
        Ui.EditorBold, Ui.EditorItalic, Ui.EditorSeparator,
        Ui.EditorLink,
        Ui.EditorSpacer,
        Ui.EditorButton.Icon(Ui.IconName.Clipboard).Tooltip("Copy to clipboard").OnClick(Copy)
    ],
    Ui.EditorContent
]
```

A button of your own runs C# and reads the document from what the editor is bound to. Flux resolves a
custom item's *name* to a Blade file; here it is a child of the toolbar.

### Extensions

Highlight, Link, Placeholder, StarterKit, Subscript, Superscript, TextAlign and Underline are on; Table,
TableRow, TableCell and TableHeader are bundled and off. Before an editor is created it raises
`ui:editor` on itself (it bubbles) — Flux's `flux:editor` event under the kit's name — and a script of the
page can change the set or reach the Tiptap instance:

```js
document.addEventListener('ui:editor', e => {
    e.detail.enableExtension('table');
    e.detail.disableExtension('underline');
    e.detail.registerExtensions([Youtube.configure({ nocookie: true })]);   // an extension of the same name is replaced
    e.detail.init(({ editor }) => editor.on('update', () => { /* … */ }));
});
```

The editor's root also has what Flux's has: `element.value` (get and set) and the `editor` instance, and
it raises `input` and `change` for every change of the document.

`code` is inline code, as it is on Flux's live editor: a control of that name wraps the selection in
`<code>` and shows as pressed, and three backticks start a block. (Flux's reference calls the item "code
block formatting"; its own element, its "Code" label and its `Ctrl`+`E` say otherwise, and the kit follows
what the element does.) `subscript`, `superscript` and `highlight` are held to Flux the same way.

Not measured, because no example on Flux's page shows these buttons: the icons of `subscript`,
`superscript`, `highlight` and `code` (drawn from Lucide, as Flux draws its other non-Heroicon toolbar
icons), their tooltips' shortcut hints, and the exact red of an `Invalid` editor.

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
Ui.Button.Href("https://github.com/pal-tamas/rask")["GitHub"]              // leaves it
```

A string that happens to name one of your own pages is still a string: it reloads the whole app to get
there. Use the route. As in Flux, a new tab is the anchor's own attribute — `.Attributes(("target", "_blank"), ("rel", "noopener noreferrer"))` —
and the runtime never intercepts one, because the reader asked for another tab.

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
| `Ui.Heading` | `Size` — `Base` (14px), `Lg` (16px), `Xl` (24px), `Xxl` (36px, Flux's `2xl`); `Level` 1–4 as in Flux, a `<div>` without one; `Accent()` |
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

### Kanban

`Ui.Kanban` is [Flux UI's kanban](https://fluxui.dev/components/kanban), part for part: cards arranged in
columns, one column per stage of a workflow.

```csharp
Ui.Kanban[
    columns.Select(column => Ui.KanbanColumn.Key(column.Id)[
        Ui.KanbanColumnHeader.Heading(column.Title).Count(column.Cards.Count),
        Ui.KanbanColumnCards[
            column.Cards.Select(card => Ui.KanbanCard.Key(card.Id).Heading(card.Title))
        ]
    ])
]
```

| Part | Takes | What it is |
|---|---|---|
| `Ui.Kanban` | columns | the row the columns stand in, 16px apart |
| `Ui.KanbanColumn` | a header, the cards, optionally a footer | one stage: a 320px tinted panel as tall as what is in it |
| `Ui.KanbanColumnHeader` | `Heading` · `Subheading` · `Count` · `Actions` | the stage's name, a second line, how many cards, and buttons at the end of the row; children replace the heading and count |
| `Ui.KanbanColumnCards` | cards | the cards, one under the other |
| `Ui.KanbanColumnFooter` | anything | what sits under the cards: a "New card" button, a form |
| `Ui.KanbanCard` | `Heading` · `As` · `Header` · `Footer` | a card; `Header` goes above the heading (badges), `Footer` under it (an icon, avatars); children replace the heading |

```csharp
Ui.KanbanColumnHeader.Heading("Planned").Count(4).Actions([
    Ui.Button.Subtle.Sm.Icon(Ui.IconName.EllipsisHorizontal).AriaLabel("Column options"),
    Ui.Button.Subtle.Sm.Icon(Ui.IconName.Plus).AriaLabel("New card").OnClick(Add)
])

Ui.KanbanCard.As(Ui.KanbanCardAs.Button).OnClick(() => Edit(card)).Heading(card.Title)
    .Header(Div.Class("flex gap-2")[Ui.Badge.Sm.Color(Ui.Color.Blue)["UI"], Ui.Badge.Sm.Color(Ui.Color.Red)["Bug"]])
    .Footer(Ui.Icon.Name(Ui.IconName.Bars3BottomLeft).Variant(Ui.IconVariant.Micro).Class("text-zinc-400"))
```

- **A card is something to read** until `.As(Ui.KanbanCardAs.Button)` makes the whole card a `<button>`:
  focused with Tab, pressed with Enter or Space, lighter under the pointer. `OnClick` is what it does.
- **It draws a board and moves nothing**, exactly as Flux's does: no card is draggable, no key reorders one,
  and there is no drop event. Adding or moving a card is your page changing its own lists and rendering again;
  for dragging, [`DragDrop`](composition-lists.md) is the framework's primitive.
- **A column does not shrink**, so a board wider than its place needs a box that scrolls:
  `Div.Class("overflow-x-auto")[Ui.Kanban[…]]`.
- Flux's `badge` prop on the column header is not carried: no example on Flux's page draws it, so there is
  nothing to measure it against. A `Ui.Badge` of your own goes in as a child, beside the heading you write there.

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
or a `Loading` you set. `Loading(false)` tells the runtime to leave the button alone. `Loading(true)` writes
`data-loading` and nothing else, as Flux's loading button does; `aria-busy` is the runtime's, for a wait it
started.

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
| **Actions** | `Ui.Button` `Ui.ButtonGroup` `Ui.Dropdown` `Ui.ContextMenu` `Ui.Command` `Ui.Popover` `Ui.Modal` `Ui.ModalTrigger` `Ui.ModalClose` `Ui.Swap` `Ui.Fab` |
| **Data display** | `Ui.Accordion` `Ui.AccordionItem` `Ui.AccordionHeading` `Ui.AccordionContent` `Ui.Avatar` `Ui.Aura` `Ui.Badge` `Ui.BadgeClose` `Ui.Card` `Ui.CardHeader` `Ui.CardHeading` `Ui.CardSubheading` `Ui.CardActions` `Ui.CardBody` `Ui.CardFooter` `Ui.CardBleed` `Ui.Carousel` `Ui.ChatBubble` `Ui.Countdown` `Ui.Diff` `Ui.Empty` `Ui.Hover3d` `Ui.HoverGallery` `Ui.Kanban` `Ui.KanbanColumn` `Ui.KanbanColumnHeader` `Ui.KanbanColumnCards` `Ui.KanbanColumnFooter` `Ui.KanbanCard` `Ui.Kbd` `Ui.Highlight` `Ui.List` `Ui.ListRow` `Ui.Stat` `Ui.StatusDot` `Ui.Table` `Ui.TableColumns` `Ui.TableColumn` `Ui.TableRows` `Ui.TableRow` `Ui.TableCell` `Ui.DataGrid` `Ui.Column` `Ui.Tree` `Ui.TextRotate` `Ui.Timeline` `Ui.Chart` `Ui.ChartSvg` `Ui.ChartViewport` `Ui.ChartLine` `Ui.ChartArea` `Ui.ChartPoint` `Ui.ChartBar` `Ui.ChartGroup` `Ui.ChartStack` `Ui.ChartPie` `Ui.ChartAxis` `Ui.ChartAxisTick` `Ui.ChartAxisGrid` `Ui.ChartAxisLine` `Ui.ChartAxisMark` `Ui.ChartZeroLine` `Ui.ChartCursor` `Ui.ChartTooltip` `Ui.ChartTooltipHeading` `Ui.ChartTooltipValue` `Ui.ChartTooltipIndicator` `Ui.ChartSummary` `Ui.ChartSummaryValue` `Ui.ChartLegend` `Ui.ChartLegendIndicator` |
| **Navigation** | `Ui.Breadcrumbs` `Ui.Dock` `Ui.Link` `Ui.Megamenu` `Ui.MegamenuPanel` `Ui.Menu` `Ui.MenuItem` `Ui.Navbar` `Ui.Pagination` `Ui.Steps` `Ui.Step` `Ui.Tabs` `Ui.Tab` |
| **Feedback** | `Ui.Callout` `Ui.CalloutHeading` `Ui.CalloutText` `Ui.CalloutLink` `Ui.Loading` `Ui.Progress` `Ui.Skeleton` `Ui.SkeletonLine` `Ui.SkeletonGroup` `Ui.Toast` `Ui.ToastGroup` `Ui.Tooltip` `Ui.TooltipContent` |
| **Data input** | `Ui.Input` `Ui.Textarea` `Ui.Select` `Ui.SelectOption` `Ui.SelectGroup` `Ui.SelectOptionCreate` `Ui.SelectOptionEmpty` `Ui.SelectButton` `Ui.SelectInput` `Ui.SelectSearch` `Ui.Autocomplete` `Ui.AutocompleteItem` `Ui.Pillbox` `Ui.PillboxOption` `Ui.PillboxOptionCreate` `Ui.PillboxOptionEmpty` `Ui.PillboxSearch` `Ui.PillboxTrigger` `Ui.PillboxInput` `Ui.FileUpload` `Ui.FileUploadDropzone` `Ui.FileItem` `Ui.FileItemRemove` `Ui.Checkbox` `Ui.CheckboxGroup` `Ui.CheckboxAll` `Ui.CheckboxIndicator` `Ui.RadioGroup` `Ui.Radio` `Ui.RadioIndicator` `Ui.Switch` `Ui.Range` `Ui.Rating` `Ui.Field` `Ui.Label` `Ui.Description` `Ui.Error` `Ui.Fieldset` `Ui.Legend` `Ui.Validator` `Ui.Otp` `Ui.Filter` `Ui.Calendar` `Ui.DatePicker` |
| **Layout** | `Ui.Separator` `Ui.Footer` `Ui.Hero` `Ui.Indicator` `Ui.Join` `Ui.Stack` `Ui.Mask` |
| **Mockup** | `Ui.MockupBrowser` `Ui.MockupCode` `Ui.MockupPhone` `Ui.MockupWindow` |
| **Chrome** | `Ui.Shell` `Ui.TopBar` `Ui.Brand` `Ui.Nav` `Ui.NavTab` `Ui.CrumbSwitcher` `Ui.CrumbSeparator` `Ui.TopLink` `Ui.Main` `Ui.Header` `Ui.Grid` `Ui.MetricRow` `Ui.Metric` `Ui.DetailList` `Ui.DetailRow` `Ui.Code` `Ui.Search` |
| **Support** | `Ui.Icon` / `Ui.IconName` / `Ui.IconVariant` (all of Heroicons: outline, solid, mini, micro), `Ui.AppearanceScript` (dark mode), `Ui.Breakpoint`, `UiStyles`, `UiStylesheet` |

## Who owns the state

The kit ships no JavaScript (the [editor](#rich-text-editor)'s engine apart), and that constraint decides the shape of every interactive component. It
resolves three ways, and which one a component takes is a property of what the platform can do rather
than of anyone's preference.

**The browser owns it, declaratively.** `Ui.Modal` is [Flux's modal](https://fluxui.dev/components/modal),
measured against it: a real **modal** `<dialog>` with a `Name`, opened by the button inside a
`Ui.ModalTrigger` of that name. No handler runs — the trigger makes its button an HTML invoker
(`command="show-modal" commandfor`), so the browser supplies the top layer, an inert page behind it so Tab
cannot wander out, Escape, and focus handed back to the trigger on close. What Flux adds in script the dialog
asks Rask's runtime for by attribute ([behaviour hooks](js-interop-runtime.md#behaviour-hooks-data-rask-)):
`data-rask-modal` for a click outside and for which of the two dismissals are allowed,
`data-rask-lock="scroll"` for the page held still behind it, and the invoker commands themselves in a browser
that has none (before Chrome 135, Firefox 144, Safari 26.2). `Ui.Megamenu` is built on a `popover`.
`Ui.Fab` opens on `:focus-within` because daisyUI defines no class to force it.

```csharp
Ui.ModalTrigger.Name("edit-profile")[Ui.Button["Edit profile"]],
Ui.Modal.Name("edit-profile").Class("md:w-96")[
    Div.Class("space-y-6")[
        Ui.Heading.Lg["Update profile"],
        Ui.Input.Bind(() => _profile.Name).Label("Name"),
        Div.Class("flex gap-2")[
            Ui.Spacer,
            Ui.ModalClose[Ui.Button.Ghost["Cancel"]],          // closes the modal it is in
            Ui.Button.Primary["Save changes"]
        ]
    ]
]

Ui.Modal.Name("filters").Flyout()[ … ]                         // against the right edge, full height
Ui.Modal.Name("nav").Flyout().Left[ … ]                        // Ui.ModalPosition: Right, Left, Bottom
Ui.Modal.Name("edit").Flyout().Floating.Class("md:w-lg")[ … ]  // standing off the edges, rounded
Ui.Modal.Name("search").Bare[ … ]                              // no panel, no close button
Ui.Modal.Name("terms").Scroll(Ui.ModalScroll.Body)[ … ]        // the whole layer scrolls, not the dialog
Ui.Modal.Name("draft").Dismissible(false).Escapable(false)[ … ]
Ui.ModalTrigger.Name("search").Shortcut("mod+k")[Ui.Button["Search"]]   // Flux's cmd.k
```

A modal draws the panel and the close button in its corner; what it holds — a heading, fields, a row of
buttons — is yours, as in Flux. Flux UI's switches are all here: `Dismissible(false)` ignores a click outside,
`Escapable(false)` ignores Escape, `Closable(false)` drops the corner's close button, and `OnClose` hears every
way it closed. `OnCancel` hears only a DISMISSAL — Escape or a click outside — and runs before `OnClose`, so a
dialog holding a draft can throw it away when the user backs out and keep it when they press a button that
closes it; the close button and a `Ui.ModalClose` are not dismissals. Both are the dialog's own events: a
dismissal raises `cancel` before `close`, in every browser, and all four combinations of the two switches
work — `Escapable(false)` alone still closes on a click outside. While a modal is open the page behind it
does not scroll, and keeps its scrollbar's gutter so it does not shift sideways.

Where Flux controls a modal from PHP (`Flux::modal('confirm')->show()`), a Rask page owns the state instead:
`Ui.Modal.Open(_confirming).OnClose(() => _confirming = false)[ … ]`, which is Rask's `wire:model`. It is
the same modal: the dialog says `data-rask-modal-open` and the runtime shows and closes it to match, so it is
in the top layer behind the same backdrop, Escape and a click outside run `OnCancel` then `OnClose`, and focus
returns when it closes. `OnClose` is where the page hears that the reader closed it — set the field there, or
the page goes on saying it is open. A modal with neither a `Name` nor `Open` is open for as long as the page
renders it. Where one modal on a page has a `Key`, give every modal there one: a parent that identifies a
type by key no longer reuses the unkeyed ones by position, and a modal rebuilt on every render loses the
`close` that follows a `cancel`. `Ui.Drawer` is gone: a panel that slides in from an edge is a flyout.

`Ui.Tooltip` is Flux's: a popover too, which the runtime shows under the pointer. See [Tooltips](#tooltips).

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
all, and `Expanded` is only the state it starts in — see [Accordion](#accordion).

**The markup owns it.** `Ui.Tab` with an `Href` is a real link with a real URL, so a tab is bookmarkable,
survives a refresh and answers the back button.


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

**And one that lets you choose.** `Ui.Select` is the browser's own `<select>` by default, and the browser
owns all of it: it works on a prerendered page and with scripting off, which is why it is the default.
`.Listbox` and `.Combobox` draw the list instead — a native `popover` under a `role="combobox"` trigger. The
browser still opens and dismisses it (a click, Escape, a click elsewhere, focus back on the button, with no
handler at all), while the cursor, type-ahead, the search and the picking are C#, so the drawn list **needs
the runtime**. Reach for it when an option must carry more than words, or the list must be searched. The
whole of it is under [Select](#select).

**And one that lets you choose several — under the same name.** Open `Ui.Select` on a collection and the
same chain builds the select that holds several answers; the model's type decides which, so a model that
holds many answers cannot get the control that holds one.

```csharp
Ui.Select.Bind(() => _order.Country)[ … ]                      // string        → one answer
Ui.Select.Bind(() => _order.Tags).Listbox.Multiple()[ … ]      // List<string>  → several
Ui.Select.Values(_picked).OnChange(Pick).Listbox.Multiple()[ … ]   // controlled, several
Ui.Select.Value(_country).OnChange(Pick)[ … ]                  // controlled, one
```

Several answers are the listbox's, as on Flux: the browser's own select holds one here, and a select over a
collection left on the native variant throws and says so. Picking a row switches it and leaves the list OPEN,
because choosing three answers should not mean opening it three times.

**And one you type into.** There is no `UiCombobox`, because a box you type into to narrow a set of answers
is the same question a select asks: `.Listbox.Searchable()` puts a search field over the options, and
`.Combobox` makes the box itself the text input. Either matches case- and accent-insensitively, and
`Filter(false)` hands the typing to the page instead, for a list that comes from a server.

A short list needs none of it: a letter typed on a listbox's closed button picks the next option starting
with it, which is what a native select does.

A row of a drawn select that needs more than words says so on the option — `Icon`, `Avatar`, `Description`,
or children of its own. Those are the listbox's and the combobox's: a native `<option>` holds text and
nothing else.

The browser still owns dismissal there — Escape and click-outside — and C# hears the popover's toggle,
which is what keeps `aria-expanded` truthful rather than drifting the moment the list is dismissed.

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

## Select

`Ui.Select` is [Flux UI's select](https://fluxui.dev/components/select), part for part, over Rask's binding:
`Bind` or `Value` where Flux says `wire:model`. **Options are children**, as in Flux, typed by the select's
own value type; an option with no `Value` stands for its own text.

```csharp
Ui.Select.Bind(() => model.Country).Label("Country").Placeholder("Choose…")[
    countries.Select(c => Ui.SelectOption.Key(c.Code).Value(c.Code)[c.Name])   // made from a list: give it a Key
]
Ui.Select.Value(_pick).OnChange(v => _pick = v)[
    Ui.SelectOption["Photography"],
    Ui.SelectOption["Design services"]
]
Ui.Select.Of<string>().Sm.Placeholder("Choose industry...")[ … ]              // no binding, no value yet
```

| Part | What it is |
| --- | --- |
| `Ui.Select` | The select. `Variant` (`Ui.SelectVariant`): `Default` — the browser's own `<select>` — `.Listbox`, `.Combobox`. `Size` (`Ui.SelectSize`): 40px, `.Sm` 32px, `.Xs` 24px with smaller text. |
| `Ui.SelectOption` | One answer: `Value`, and its words as children or `Label`. `SelectedLabel`, `Keywords`, `Icon` (`IconVariant`, `IconClass`), `Avatar`, `Description`, `Disabled()`. |
| `Ui.SelectGroup` | Options under one `Label`: an `<optgroup>` in the native variant, a headed run of rows otherwise. |
| `Ui.SelectOptionCreate` | The row that makes a new option of what was typed: `MinLength`, and `OnClick` handed the search. |
| `Ui.SelectOptionEmpty` | What a search that matches nothing says, and `WhenLoading` while results are on their way. |
| `Ui.SelectButton` `Ui.SelectInput` `Ui.SelectSearch` | Flux's `button`, `input` and `search` slots, written as children to set what the select's own props do not reach. |

`Label`, `Description`, `DescriptionTrailing` and `Badge` draw the [field](#fields-label-description-error)
around the select, with the bound member's message under it; `ShowValidation(false)` leaves that message to
a `Ui.Error` placed elsewhere. `Invalid()` is error styling the form did not ask for, `Disabled()` a select
that cannot be opened or changed, and `Name` what the answer is posted under.

**Native, small, grouped.** The default variant is the browser's `<select>` in the input's box: it needs no
runtime, renders complete on a prerendered page and gets a phone's own picker. `Placeholder` is shown while
nothing is picked, and a native `<option>` holds text and nothing else.

```csharp
Ui.Select.Bind(() => m.Industry).Placeholder("Choose an industry...")[
    Ui.SelectGroup.Label("Creative")[
        Ui.SelectOption.Value("photography")["Photography"],
        Ui.SelectOption.Value("design")["Design services"]
    ],
    Ui.SelectGroup.Label("Technology")[
        Ui.SelectOption.Value("web-development")["Web development"]
    ]
]
```

**Listbox: a button over a drawn list.** For options that carry more than words — a `Prefix` that stays in
the button, icons, descriptions, avatars, content of your own:

```csharp
Ui.Select.Bind(() => m.Range).Listbox.Prefix("Compare to")[
    Ui.SelectOption.Value("last-month")["Last month"],
    Ui.SelectOption.Value("last-year")["Last year"]
]
Ui.Select.Bind(() => m.Method).Listbox.Clearable().Placeholder("Choose method...")[
    Ui.SelectOption.Value("card").Label("Credit card").Icon(Ui.IconName.CreditCard),
    Ui.SelectOption.Value("bank").Label("Bank transfer").Icon(Ui.IconName.BuildingLibrary)
        .Description("Two working days")
]
Ui.Select.Bind(() => m.Owner).Listbox[
    people.Select(p => Ui.SelectOption.Key(p.Id).Value(p.Id).Label(p.Name).Avatar(p.PhotoUrl))
]
Ui.Select.Bind(() => m.Colour).Listbox[
    Ui.SelectOption.Value("red")[
        Div.Class("flex items-center gap-2")[Div.Class("size-4 rounded-full bg-red-500"), "Red"]
    ]
]
```

A `Description` is the list's only: the button does not repeat it once the option is picked, and
`SelectedLabel` is what the button shows when that should not be the option's own words. An `Avatar` wins
over an `Icon`. `Clearable()` adds a button that puts the select back to nothing chosen, shown while there is
an answer. The list is as wide as its button; `OptionsClass` widens it, and `Position` and `Align` (the kit's
`Ui.Position` and `Ui.Align`) say which side it opens on and which edge it lines up with:

```csharp
Ui.Select.Bind(() => m.Visibility).Listbox.Class("max-w-32").OptionsClass("min-w-72")[ … ]
```

`Ui.SelectButton` is Flux's button slot, for what the select's props do not reach — its own `Placeholder`,
`Invalid`, `Size`, `Disabled`, `Clearable` and `Class`:
`Ui.Select.Bind(() => m.Plan).Listbox[Ui.SelectButton.Class("rounded-full!"), …]`.

**Searchable, and keywords.** `Searchable()` puts a search field at the top of the listbox's options.
Opening focuses it; typing filters case- and accent-insensitively over each option's text and its
`Keywords` — more words to find it by, never shown — and puts the cursor on the first match. "No results
found" shows when nothing matches; `Empty("…")` says it in your own words.

```csharp
Ui.Select.Bind(() => m.Category).Listbox.Searchable().Placeholder("Choose category...")[
    Ui.SelectSearch.Placeholder("Search categories..."),
    Ui.SelectOption.Value("fruit").Keywords("apple orange pear")["Fruit"],
    Ui.SelectOption.Value("drinks").Keywords("coffee tea juice")["Drinks"]
]
```

`Ui.SelectSearch` is Flux's search slot: `Placeholder` ("Search..." when unset), `Icon` (a magnifying glass
when unset), `Clearable(false)` to leave out the button that empties the field, and `Value` with `OnInput`
for a page that holds the search itself.

**Multiple.** Open the select on a collection — `Bind` over a `List<T>`, a `T[]` or a `HashSet<T>`, or
`Values(tags)` with `OnChange` — and `.Listbox.Multiple()` picks several. Picking a row switches it and
leaves the list open; the button names the one picked option, or counts them:

```csharp
Ui.Select.Bind(() => m.Industries).Listbox.Multiple().Searchable()
    .SelectedSuffix("industries selected")          // "3 industries selected"; "3 selected" when unset
    .Clear(Ui.SelectClear.Close)[                   // the search survives each pick and empties when the list closes
    industries.Select(name => Ui.SelectOption.Key(name)[name])
]
```

A bound list, array or set each gets its own kind back. `Multiple()` on a select that holds one answer
throws and says to open it on a collection, and so does a select over a collection left on the native
variant: several answers are the listbox's, as on Flux.

**Combobox: a text input that filters.** A click or typing opens the list, typing filters it, Enter picks
and the input shows the answer. `Ui.SelectInput` is Flux's input slot (`Placeholder`, `Invalid`, `Size`,
`Value`, `OnInput`):

```csharp
Ui.Select.Bind(() => m.Industry).Combobox.Placeholder("Choose industry...")[
    industries.Select(name => Ui.SelectOption.Key(name)[name])
]
```

**Backend search, and a create row.** `Filter(false)` leaves every option in the list whatever is typed:
the page filters, answering `OnInput` by rendering the options that match from wherever it keeps them.
`Ui.SelectOptionEmpty` is what the list says when there are none, and while results are on their way.
`Ui.SelectOptionCreate` offers to make an option of what was typed once the search is `MinLength` long and
names none that exists; its `OnClick` is a `Callback<string>` handed the search as typed, and the page adds
the option and selects it.

```csharp
Ui.Select.Bind(() => m.UserId).Combobox.Filter(false)[
    Ui.SelectInput.OnInput(text => _users = Users.Named(text)),
    _users.Select(u => Ui.SelectOption.Key(u.Id).Value(u.Id)[u.Name]),
    Ui.SelectOptionCreate.MinLength(2).OnClick(name => Create(name))["Create new"],
    Ui.SelectOptionEmpty.WhenLoading("Loading users...")["No users found."]
]
```

In a listbox with no search the create row is always shown and `OnClick` is handed an empty string — the
case where the page opens a form of its own.

**No script.** The drawn list is a native `popover` placed by CSS anchor positioning: as wide as its
trigger, 5px under it, at most 20rem tall, and it flips when it would not fit. The browser opens it on a
click and closes it on Escape or a click elsewhere, returning focus to the button; the keyboard below is C#,
recorded on fluxui.dev and matched.

| Key | Listbox |
| --- | --- |
| Click, Space, ArrowDown, ArrowUp | Open, with the cursor on the picked option — the first when there is none. |
| ArrowDown / ArrowUp | Move the cursor, skipping a disabled option. They stop at either end; there is no wrap. |
| Home, End, PageUp, PageDown | Nothing, as on Flux. |
| Enter | Picks the active row and closes. With `Multiple()` it switches the row and the list stays open. |
| Escape, a click elsewhere | Close; focus returns to the button. |
| Tab | Closes, picks nothing, and moves on. |
| A letter | On the **closed** button it picks the next option starting with it; on the open list it moves the cursor. |
| The pointer | Moves the cursor to the row under it. |

In a combobox, Escape closes the list and a second Escape empties the text but keeps the answer; leaving
the input with words that name nothing puts the answer's text back.

**ARIA.** The trigger — the listbox's `<button>`, the combobox's `<input>` — carries `role="combobox"`,
`aria-haspopup="listbox"`, `aria-expanded`, `aria-controls` and, while open, `aria-activedescendant` naming
the row the cursor is on. The list is `role="listbox"`, with `aria-multiselectable` only when it takes
several; each row is `role="option"` with `aria-selected`, and a disabled one adds `aria-disabled="true"`.
A drawn select posts nothing by itself, so one with a `Name` carries its answer in hidden inputs.

Markers follow Flux's: `data-ui-select` on a drawn select's root, `data-ui-select-button`,
`data-ui-select-native`, `data-ui-options`, `data-ui-option`, and on a row `data-selected` and `data-active`
(the cursor); `data-open` while the list is.

Two things Flux does in script are the runtime's here, by attribute: the closed listbox button ignores Enter
and its arrows do not scroll the page (`data-rask-listbox-button`), and while a list is open the page behind
it neither scrolls nor takes the pointer (`data-rask-lock` on the popover). Every list says
`aria-multiselectable="true"`, one answer or several, as Flux's does; the "no results" row and the create row
carry no role, as Flux's do not. The listbox's search field is Flux's `role="combobox"` and nothing more; it
keeps the list's keys through `data-rask-contain-keys`, so Enter picks instead of submitting a form around the
select and the arrows walk the rows instead of the caret.

**Gone with daisyUI's select:** `Options(list)` (options are children), `Native()` / `Native(false)` (now
the variant: `.Listbox`), `OptionGroup` (now `Ui.SelectGroup`), `OptionDisabled` (now
`Ui.SelectOption.Disabled()`), `OptionTemplate` (now the option's children, `Icon`, `Avatar`,
`Description`), `Filter((value, text) => …)`, `OnSearch` and `Loading` (now `Filter(false)` and the page
filters), `EmptyText` / `LoadingText` (now `Empty("…")` and `Ui.SelectOptionEmpty`), `SelectAll()`,
`Chips(n)`, floating labels (`Floating`), `Tone`, daisyUI's `Variant` and `Size(Ui.Size)`, `Hint` (now
`Description`), `AccessibleLabel` (now `Label`) and `Ui.MultiSelect` as a name.

## Autocomplete

`Ui.Autocomplete` is [Flux UI's autocomplete](https://fluxui.dev/components/autocomplete): Flux's input over a
list of suggestions. What it holds is the **text** — an item has no value of its own, and what is typed need
not be an item. To show a name and store an id, use `Ui.Select.Combobox`.

```csharp
Ui.Autocomplete.Bind(() => model.State).Label("State of residence")[
    states.Select(state => Ui.AutocompleteItem[state])
]
```

It takes what Flux documents for it, which is the input's own list: `Type`, `Label`, `Description`,
`Placeholder`, `Size` (`Ui.AutocompleteSize`: `.Sm`, `.Xs`), `Variant` (`Ui.AutocompleteVariant`: `.Filled`),
`Disabled()`, `ReadOnly()`, `Invalid()`, `Multiple()`, `Mask`, `Icon`, `IconTrailing`, `Kbd`, `Clearable()`,
`Copyable()`, `Viewable()`, `As` (`Ui.AutocompleteAs`), `InputClass`, and `ContainerClass` for the open list (a height, such
as `max-h-80`). `Ui.AutocompleteItem` takes `Disabled()`. The text reaches the page — the bound member, or
`OnChange` — when an item is picked, when Escape empties the input, and when what was typed is left.

No script: the list is a native `popover` placed by CSS anchor positioning, and the cursor, the filter and the
picking are C#. As recorded on Flux's page:

| | |
| --- | --- |
| A click, ArrowDown, ArrowUp | Open the list; the cursor starts on the item last picked, else the first. |
| Typing | Opens the list on the items that hold the text anywhere, whatever the case or the accents. Nothing matches: nothing is drawn. |
| ArrowDown, ArrowUp | Move the cursor and stop at either end. Home and End are the text's own. |
| Enter | Writes the active item into the input and closes. With no active item it does nothing. |
| Escape | Closes the list **and empties the input**, open or not. |
| Tab, a click elsewhere | Close, and keep whatever was typed. |
| The pointer | Moves the cursor to the row under it; a click picks it and focus stays in the input. |

The input is `role="combobox"` with `aria-autocomplete="list"`, `aria-haspopup="listbox"`, `aria-expanded`,
`aria-controls` and, while open, `aria-activedescendant`. The list is `role="listbox"` and — as Flux writes it,
though it holds one answer — `aria-multiselectable="true"`; the item last picked stays `aria-selected="true"`
whatever is typed afterwards. Markers: `data-ui-autocomplete`, `data-ui-autocomplete-items`,
`data-ui-autocomplete-item`. `Copyable()` is the input's copy button, and the page behind the open list is
locked (`data-rask-lock`).

## Pillbox

`Ui.Pillbox` is [Flux UI's pillbox](https://fluxui.dev/components/pillbox): several answers out of a list,
each shown as a pill that can be taken off again. It holds a **collection** — `Bind` over a `List<T>`, a
`T[]`, a `HashSet<T>`, or `Values(…)` with `OnChange` — and its options are children.

```csharp
Ui.Pillbox.Bind(() => model.Tags).Label("Tags").Placeholder("Choose tags...")[
    tags.Select(tag => Ui.PillboxOption.Value(tag.Id)[tag.Name])
]

Ui.Pillbox.Bind(() => model.Skills).Searchable().SearchPlaceholder("Filter skills...")[…]   // a search field over the list
Ui.Pillbox.Bind(() => model.Skills).Combobox.Placeholder("Choose skills...")[…]             // an input among the pills
```

| | |
| --- | --- |
| `Ui.Pillbox` | `Placeholder`, `Label`, `Description`, `Size` (`Ui.PillboxSize`: `.Sm`), `Variant` (`Ui.PillboxVariant`: `.Combobox`), `Searchable()`, `SearchPlaceholder`, `Filter(false)`, `Disabled()`, `Invalid()`. |
| `Ui.PillboxOption` | `Value`, `Label`, `SelectedLabel` (what its pill says), `Disabled()`, `Filterable(false)` (a search never hides it), or children of your own — an icon beside the words. The pill shows the words alone. |
| `Ui.PillboxOptionCreate` | `MinLength`, `OnClick` handed the text as typed. Offered once that text is long enough and names no option; with nothing to type into it is always there, and opens a form of the page's own. Written before the options, it is drawn before them. |
| `Ui.PillboxOptionEmpty` | What the list says when nothing matches; `WhenLoading` while the page answers. |
| `Ui.PillboxSearch` `Ui.PillboxTrigger` `Ui.PillboxInput` | Flux's `search`, `trigger` and `input` slots, as children: the search field's `Placeholder` / `Icon` / `Clearable(false)` / `Value` / `OnInput`; the trigger's `Placeholder` / `Invalid()` / `Size` / `Clearable()`; the input's `Placeholder` / `Value` / `OnInput` / `Invalid()`. |

Creating the option that is not there, with the page holding what was typed:

```csharp
Ui.Pillbox.Bind(() => model.TagIds).Combobox[
    Ui.PillboxInput.Value(_search).OnInput(text => _search = text).Placeholder("Choose tags..."),
    _tags.Select(tag => Ui.PillboxOption.Key(tag.Id).Value(tag.Id)[tag.Name]),
    Ui.PillboxOptionCreate.MinLength(2).OnClick(CreateTag)[$"Create new \"{_search}\""]
]
```

It is the list `Ui.Select` draws, under another trigger, so it ships no script either. As recorded on Flux's
page, where it differs from the select's listbox:

| | |
| --- | --- |
| A click, Space, ArrowDown, ArrowUp | Open the list. Enter on the closed trigger does nothing. |
| Enter, a click on a row | Switch the row; the list stays open. Pills stand in the order they were picked. |
| A letter on the closed trigger | Switches the next option that starts with it, without opening. |
| The cross on a pill | Takes that option off; the list stays as it was. |
| The pointer leaving the list | No row is lit any more. |
| Searchable | The search field takes focus; a pick empties it and sends the cursor back to the top. |
| Combobox | Typing opens and narrows the list; a pick empties the input; **Backspace in the empty input takes the last pill off**; Escape and Tab close and empty it. |

The trigger is `role="combobox"` (with `aria-controls`, `aria-autocomplete="none"`) while there is nothing to
type into, and `role="button"` over a search field or an input, which is then the combobox; the list is
`role="listbox"` with `aria-multiselectable="true"`. Markers: `data-ui-pillbox`, `data-ui-pillbox-trigger`,
`data-ui-pillbox-placeholder`, `data-ui-pillbox-input`, `data-ui-pillbox-search`, `data-ui-listbox-options`,
`data-ui-listbox-option`, `data-ui-option-create`.

The page behind an open list is locked (`data-rask-lock`), as on Flux, and the trigger keeps the keys Flux's
keeps (`data-rask-contain-keys`): Enter, Space and the vertical arrows as a combobox, Space and the arrows as the
button over a search field, so opening it from the keyboard does not scroll the page. The input among the pills
keeps the list's keys the same way while the list is open.
`Ui.PillboxOptionCreate` has no `modal`: its `OnClick` is the page's to answer.

## Form controls

**All twelve** of the kit's data-input controls implement `IFormControl<T>`, so each works in the two
shapes every Rask input does:

```csharp
Form.Model(_order)[
    Ui.Select.Bind(() => _order.Country).Label("Country")[
        countries.Select(c => Ui.SelectOption.Key(c.Code).Value(c.Code)[c.Name])
    ],
    Ui.Select.Value(_country).Label("Country").OnChange(v => _country = v)[ … ],
    Ui.Select.Bind(() => _order.Tags).Label("Tags").Listbox.Multiple()[ … ]
]
```

**Toasts are raised, not placed** — this is [Flux's toast](https://fluxui.dev/components/toast), with its
group. `Toast.Success("Saved")` from any handler raises one ([Toast messages](composition-lists.md#toast-messages));
`Ui.Toast` in the layout is where they appear, and an app that places none gets the host's.

| Flux | Rask |
|---|---|
| `<flux:toast />` in the layout | `Ui.Toast` in the layout — or nothing: the host places one |
| `<flux:toast position="top end" invert />` | `Ui.Toast.TopEnd.Invert()` (`Ui.ToastPosition`: `BottomEnd` `BottomCenter` `BottomStart` `TopEnd` `TopCenter` `TopStart`) |
| `<flux:toast class="pt-24" />` | `Ui.Toast.Class("pt-24")` |
| `<flux:toast.group>` … `</flux:toast.group>` | `Ui.ToastGroup[Ui.Toast]` |
| `<flux:toast.group expanded position="top end">` | `Ui.ToastGroup.Expanded().TopEnd[Ui.Toast]` |
| `Flux::toast('Saved.')` / `$flux.toast('Saved.')` | `Toast.Info("Saved.")` |
| `Flux::toast(heading: 'Changes saved', text: '…')` | `Toast.Info("…").Heading("Changes saved")` |
| `variant: 'success'` / `'warning'` / `'danger'` | `Toast.Success(…)` / `Toast.Warning(…)` / `Toast.Error(…)` |
| `duration: 1000` / `duration: 0` | `.For(1.Second)` / `.UntilDismissed()` |
| `action: ['label' => 'Undo', 'event' => 'undo-changes']` | `.Action("Undo", UndoChanges)` |
| `action: ['label' => 'View', 'href' => …]` | `.Action("View", Routes.InvoicePage(id))` |
| `link: ['label' => 'View invoice', 'href' => …]` | `.Link("View invoice", Routes.InvoicePage(id))` |

On its own, `Ui.Toast` shows one toast at a time: a new one takes the place of the one showing. Inside a
`Ui.ToastGroup` they stack — three show, the newest in front and each older one a step back and a little
narrower — and the pointer over the stack lays them all out, as `Expanded()` does for good.

A toast goes after five seconds unless it says otherwise, by its close button, or — on its own — by Escape.
The kit ships no script, so all of that is the **runtime's** generic hooks, written as attributes: the toast is a
native `popover` the runtime shows (`data-rask-popover-open`), so it is in the top layer, over an open dialog;
`data-rask-dismiss-after="<ms>"` has the runtime press the toast's own `[data-rask-dismiss]` button when the
time is up, waiting while the pointer is over the toast — and only the pointer
(`data-rask-dismiss-hold="pointer"`): as on Flux, a toast with focus on its close button still goes on time. A
`Ui.ToastGroup` is one `data-rask-dismiss-scope`, so the pointer anywhere over the stack holds every toast in
it, and when it leaves each runs on from where it stopped rather than starting again; and
`data-rask-shortcut="escape"` is Escape. Pressing
the button rather than hiding the element is the point: the outlet takes the toast off its list, so the next
render agrees with the screen. An action's button shows a spinner while its handler runs — the runtime's
`data-loading` — and then takes the toast down.

Like Flux's, the host is `role="status"` and each toast `aria-atomic="true"`, so a toast is announced politely
and whole; the variant is carried by its icon's shape as well as its colour.

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

**`Ui.Chart` is Flux's chart, part by part, drawn in C#.** No script and no chart library: the scales, the ticks
and every path are computed during the render, to the numbers Flux's own layout arrives at
(`scripts/flux/parity-chart.mjs` compares the two drawings' geometry).

```csharp
Ui.Chart.Value(visits).Class("aspect-3/1")[
    Ui.ChartSvg[
        Ui.ChartLine.Field((Visit v) => v.Visitors).Class("text-pink-500 dark:text-pink-400"),
        Ui.ChartAxis.X.Field((Visit v) => v.Date)[Ui.ChartAxisLine, Ui.ChartAxisTick],
        Ui.ChartAxis.Y[Ui.ChartAxisGrid, Ui.ChartAxisTick],
        Ui.ChartCursor],
    Ui.ChartTooltip[
        Ui.ChartTooltipHeading.Field((Visit v) => v.Date),
        Ui.ChartTooltipValue.Field((Visit v) => v.Visitors).Label("Visitors")]]
```

- **Data.** Flux's chart takes an array of rows and its parts name a column with a string. `Ui.Chart.Value(rows)`
  takes the typed list, and each part takes a selector whose lambda states the row type once —
  `.Field((Visit v) => v.Visitors)` — because a part is built before the chart it goes into. A number of any
  numeric type plots without a cast; a `DateTime`, `DateOnly` or `DateTimeOffset` on the index axis makes it a
  time axis; anything else is a name. A chart of bare numbers needs no field at all:
  `Ui.Chart.Value([15, 18, 16])[Ui.ChartSvg.Gutter("0")[Ui.ChartLine]]`.
- **Parts.** In the drawing: `Ui.ChartLine` and `Ui.ChartArea` (`Curve`: smooth or `None`), `Ui.ChartPoint`,
  `Ui.ChartBar` (alone, or inside `Ui.ChartGroup` / `Ui.ChartStack`), `Ui.ChartPie` (`InnerRadius("60%")` makes a
  donut, `Radius` rounds the slices, `ColorField` lets a row choose its hue), `Ui.ChartAxis` holding
  `Ui.ChartAxisTick`, `Ui.ChartAxisGrid`, `Ui.ChartAxisLine` and `Ui.ChartAxisMark`, `Ui.ChartZeroLine` and
  `Ui.ChartCursor`. Beside it: `Ui.ChartViewport` (the drawing's box, when a legend or a summary shares the
  chart), `Ui.ChartTooltip` with `Ui.ChartTooltipHeading` / `Ui.ChartTooltipValue` / `Ui.ChartTooltipIndicator`,
  `Ui.ChartSummary` with `Ui.ChartSummaryValue`, and `Ui.ChartLegend` with `Ui.ChartLegendIndicator`.
  `Ui.Chart.Horizontal()` lays the rows down the Y axis.
- **Axes.** The value axis starts at zero (or the lowest value below it) and steps by a round quarter of its
  span; `TickCount`, `TickStart`, `TickEnd`, `TickValues`, `TickPrefix`, `TickSuffix` and
  `Position(Ui.Position.Right)` are Flux's. A time axis ticks at the pace of its rows and writes itself for its
  reach (`9:17 AM`, `9 AM`, `Tue 9 AM`, `Mar 10`, `Mar`, `2026`). Labels that would touch give way as Flux's do:
  dates drop every other one, names turn 45°.
- **Size.** Flux measures its element in the browser and draws again when it changes. A chart drawn in C# is told
  its box — `Ui.ChartSvg.Width(606).Height(202)`, 600 × 200 unless stated — and the SVG then scales as a whole.
  State the size it is usually shown at, so its 12px labels are 12px there.
- **Format.** Flux's `:format` is the options of `Intl.NumberFormat` / `Intl.DateTimeFormat`; `UiChartFormat`
  carries them under Intl's names, written with .NET formatting in the reader's culture:
  `new() { Style = Ui.ChartFormatStyle.Currency, Currency = "USD" }`,
  `new() { Notation = Ui.ChartFormatNotation.Compact, MaximumFractionDigits = 1 }`,
  `new() { Month = Ui.ChartFormatPart.Short, Day = Ui.ChartFormatPart.Numeric }`. Carried: `style`
  (decimal, currency, percent, unit), `currency`, `unit`, `notation` (standard, compact, scientific),
  `minimumFractionDigits`, `maximumFractionDigits`, `useGrouping`, `dateStyle`, `timeStyle`, `weekday`, `year`,
  `month`, `day`, `hour`, `minute`, `second`, `hour12`. Not carried: `compactDisplay: long`, `currencyDisplay`,
  `currencySign`, `signDisplay`, `unitDisplay`, significant-digit and integer-digit limits, `roundingMode`,
  `timeZone`, `timeZoneName`, `era`, `fractionalSecondDigits`, `dayPeriod`, `hourCycle`, `calendar`,
  `numberingSystem`. A date's parts are written in the order English writes them.
- **Under the pointer.** Flux follows the pointer in script. Here every row has a strip over the drawing,
  reaching halfway to its neighbours, that carries the row's own cursor and tooltip and shows them on `:hover`
  — so the nearest row answers, as in Flux, with no script. A summary shows the latest row and does not follow
  the pointer; a pie has no tooltip. Both need a pointer hook in the runtime.
- **Labels are measured in Inter.** Flux sizes a chart's gutters by measuring its tick labels in the browser.
  The kit carries Inter's advance widths and kerning (`UiChartInter`, generated by
  `scripts/flux/inter-metrics.mjs`) and adds them up as Chromium does. In another face the labels still fit;
  the gutters are those Inter would have needed.

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
Ui.Input.Value(key).Icon(Ui.IconName.Key).ReadOnly().Copyable()                    // copy button, a tick for 2 s
Ui.Input.Of<string>().Type(InputType.File).Multiple().OnFiles(Save)                // "Choose files" + the chosen name
Ui.Input.Of<string>().As(Ui.InputAs.Button).Placeholder("Search...").OnClick(Open) // a button drawn as the input
Ui.InputGroup[Ui.InputGroupPrefix["https://"], Ui.Input.Bind(() => m.Site)]        // fused borders
Ui.Textarea.Bind(() => m.Notes).Label("Notes").Rows(UiTextareaRows.Auto).None      // grows by CSS; resize="none"
```

- **Props.** `Label`, `Description`, `DescriptionTrailing` (and `Badge` on a textarea) wrap the control in a
  [`Ui.Field`](#fields-label-description-error) with its `Ui.Error`; without them it is the control alone. `Size`
  (`Sm`, `Xs`), `Variant` (`Filled`), `Disabled`, `ReadOnly`, `Invalid`, `Icon` / `IconTrailing` (a `Ui.IconName`, or
  content of your own such as a button), `Kbd`, `Clearable`, `Copyable`, `Viewable`, `Mask`, `As`, `Multiple`, and
  `Class` for the wrapper with `InputClass` for the `<input>`; `Attributes(("aria-label", "Search keys"))` forwards
  an attribute to the `<input>` as Flux does — the way to name an input that has no label. A textarea takes `Rows` (4 unless set) and `Resize`
  (`Vertical`, `Horizontal`, `Both`, `None`).
- **A bound control is invalid on its own** while its form holds a message for the member: `aria-invalid`,
  `data-invalid` and the red border, with `aria-describedby` naming the field's error and description.
  `ShowValidation(false)` leaves the message to a `Ui.Error` you place yourself. A control with no label draws no
  field, so its message is yours to place too: `Ui.Field[Ui.Input.Bind(…), Ui.Error]`.
- **No script of the kit's.** What Flux does in Alpine is the runtime's, by attribute: the clear button
  (`data-rask-clear`, hidden by CSS while the input is empty) empties the field in the click and leaves the focus
  in it; the copy button (`data-rask-copy`) writes the clipboard in the click and shows a tick for two seconds;
  `Mask` holds each keystroke to its pattern (`data-rask-mask`) and is applied to the value drawn and committed
  as well. The reveal button is a handler; `Rows(UiTextareaRows.Auto)` is `field-sizing: content`; a file input
  is a `<label>` around the real input. Flux's `mask:dynamic` (an Alpine expression) is not built.
- **In a group, label the group.** `Ui.Field[Ui.Label["Website"], Ui.InputGroup[…], Ui.Error]` — the group stands
  for its input, so the field's label and error reach it. A neighbour that is not an input (a button, a select)
  joins the outline by carrying `data-ui-group-target`.
- **Gone with daisyUI's input:** floating labels (`Floating`), `Hint` (now `Description`), `Tone`, `Error("…")`
  (now `Invalid()` beside a `Ui.Error.Message("…")`), `AccessibleLabel`, `ShowValidating`, `AutoSize` (now
  `Rows(UiTextareaRows.Auto)`), `Ui.Resize` (now `Ui.TextareaResize`) and `Ui.Search` (now
  `Ui.Input.Icon(Ui.IconName.MagnifyingGlass)`).

### Checkbox, radio and switch

**`Ui.Checkbox`, `Ui.Radio` and `Ui.Switch` are Flux UI's.** Same parts, props, look and markers
([checkbox](https://fluxui.dev/components/checkbox), [radio](https://fluxui.dev/components/radio),
[switch](https://fluxui.dev/components/switch)), over Rask's binding.

```csharp
Ui.Checkbox.Bind(() => m.Agreed).Label("I agree to the terms")                  // a bool, or a bool?
Ui.Checkbox.Checked(on).OnChange(v => on = v).Label("Remember me")              // the parent owns the state
Ui.Switch.Bind(() => m.Alerts).Label("Email alerts").Description("At most once a day.")
Ui.Switch.Value(on).OnChange(v => on = v).Label("Compact rows").Left            // align="left"

Ui.CheckboxGroup.Bind(() => m.Topics).Label("Email me about")[                  // a List<T>, a HashSet<T>, an array
    Ui.CheckboxAll.Label("Everything"),
    Ui.Checkbox.Value("news").Label("News").Description("Once a month."),
    Ui.Checkbox.Value("jobs").Label("Jobs")
]

Ui.RadioGroup.Bind(() => m.Plan).Label("Plan").Cards[                           // one value, of any type
    Ui.Radio.Value(Plan.Free).Label("Free").Description("For trying it out."),
    Ui.Radio.Value(Plan.Pro).Label("Pro").Icon(Ui.IconName.Bolt)
]
Ui.RadioGroup.Value(role).OnChange(v => role = v).Segmented.Sm[ … ]             // variant="segmented" size="sm"
```

- **The group is the field; the choices are its children.** A `Ui.Radio` or a `Ui.Checkbox` inside a group binds
  nothing itself: its `Value` is what the group's member becomes (radio) or what its collection holds while the
  box is ticked (checkbox). A checkbox group writes the collection in the order the checkboxes are written, and
  its controlled opening is `Values`. `Ui.Radio.Checked()` chooses a radio only while the group holds no value.
- **Variants draw the same inputs.** `Variant` on a checkbox group is `Cards`, `Pills` or `Buttons`; a radio group
  adds `Segmented`, with `Size` (`Sm`) and, for cards, `Indicator(false)`. A choice's `Label`, `Description` and
  `Icon` are drawn on the card, the pill, the segment or the button. A card's children replace them:
  `Ui.Radio.Value(x)[Ui.RadioIndicator, Div[…]]` (and `Ui.CheckboxIndicator`). `Class("flex-col")` on a cards
  group stacks it; `max-sm:flex-col` only on a phone.
- **`Ui.CheckboxAll`** sits among a group's checkboxes, anywhere in its markup: ticked when all are, a dash
  (`data-indeterminate`, as Flux marks it) while some are, and pressing it ticks them all unless they all are already. Disabled
  checkboxes keep what they have. `Ui.Checkbox.Indeterminate()` draws the same dash on a checkbox of your own, and
  a bound `bool?` that is `null` draws it too.
- **No script, and real inputs.** Each root (`data-ui-checkbox`, `data-ui-radio`, `data-ui-switch`, and
  `…-cards` / `-pills` / `-buttons` / `-segmented`) is a `<label>` around an `<input>` that is out of sight, never
  `hidden`: the tick, the dot and the thumb's 150ms travel read the input's own `:checked`. So the space bar,
  a click on the label, a radio group's arrow keys (they move AND choose, wrapping at the ends; Tab enters at the
  chosen radio) and the form post (`name` + `value`) are the browser's. A switch is
  `<input type="checkbox" role="switch">`, which the runtime flips on Enter as well as Space, as Flux's does.
- **What Flux forwards to the control** goes through `Attributes`, onto the `<input>`:
  `Ui.Radio.Value("editor").Label("Editor").Attributes(("name", "role"))`,
  `Ui.Checkbox.Attributes(("aria-label", "Select row"))`. A radio group names its radios after its own id.
- **Validation.** A bound checkbox, switch or group is invalid on its own while its form holds a message for
  the member: `aria-invalid`, `data-invalid`, `aria-describedby` naming the field's `Ui.Error`, which `Label`
  draws with it. `Invalid()` says so by hand. Flux's pages show no invalid checkbox to measure; the unticked box
  takes the red-500 border Flux's input has.
- **Gone with daisyUI's controls:** `Ui.Toggle` (now `Ui.Switch`), `Tone` and `Size` on all three, children as
  the label (now `Label`), `Ui.Radio.Text` / `.Group` and a radio bound to its own `bool`, and the groups'
  `Options`, `OptionDescription`, `OptionDisabled`, `Layout` (`Ui.ChoiceLayout`), `CheckAll` / `CheckAllLabel`,
  `Hint`, `Error`, `Badge` and `AccessibleLabel`.

**The opening step fixes the type argument and the mode together.** `Bind` opens a bound control and
`Value` a controlled one; they are mutually exclusive because a control with both would have two
sources of truth for one field, and the compiler enforces it — both live on the control's entry, so
taking one leaves the other unreachable. `Label`, `Placeholder` and the rest follow in any order, since none
of them says anything about `T`. Bound mode drives the surrounding `Form`'s validation — per-field
`Validate`, `AfterBind`, and the `aria-invalid`/`aria-describedby` display — and controlled mode leaves
the value with the parent. See [building form controls](building-form-controls.md).

**The controls still on daisyUI share one field shape.** `Ui.Otp`
and the date pickers all take the same members from
`UiFormField<T>`, until each is rebuilt on Flux as `Ui.Input`, `Ui.Textarea`, `Ui.Select`, the checkbox, the radio, the switch and the file upload have been: a visible `Label` (a `<label for>` over the control, with an optional `Badge` beside it) or,
without one, an invisible `AccessibleLabel`; a `Hint` and a controlled `Error` under it; an `Id`, derived from the
bound member or the label when you give none; and `aria-describedby`, `aria-invalid` and `aria-required` worked out
from those and from the bound member's `[Required]` and messages. `Label` is never a required step, so write it
anywhere after the opening — `Ui.Otp.Value(code).Length(6).Label("Verification code").Hint("Sent to your phone")`.

**Generic where the value type varies, concrete where it does not.** `UiInput<T>`, `UiTextarea<T>`,
`UiSelect<T>` and `UiFilter<T>` are generic — the model decides what they hold, and `Ui.Input` even
takes its `type` attribute from `T`, so a bound `int` is a number field with nothing said at the call
site. `UiRadioGroup<T>` and `UiCheckboxGroup<T>` are generic over what a choice holds. The rest are closed over
the one type they can have: `Ui.Checkbox` and `Ui.Switch` over `bool`, `Ui.Range` over `double`, `Ui.Rating` over `int`, `Ui.Otp` over `string`,
`Ui.Calendar` over `DateOnly`. A checkbox's value is a `bool` and nothing else; a type parameter there
would have exactly one legal argument.

| | Binds |
|---|---|
| `UiInput<T>` `UiTextarea<T>` `UiSelect<T>` | what the field holds |
| `UiSelectMultiple<T>` — `Ui.Select` opened on a collection | the ELEMENT type — it binds an `ICollection<T>` |
| `UiFilter<T>` | the chosen option of a whole radio group |
| `UiRadioGroup<T>` | the value of the chosen `Ui.Radio` |
| `UiCheckboxGroup<T>` | the ELEMENT type — it binds an `ICollection<T>` of the ticked checkboxes' values |
| `Ui.Checkbox` `Ui.Switch` | on or off |
| `Ui.Range` `Ui.Rating` `Ui.Calendar` | the position, the star count, the day |
| `Ui.Otp` | the code — `OnComplete` fires on the transition into a full one, in both modes |


**File upload is Flux UI's** ([fluxui.dev/components/file-upload](https://fluxui.dev/components/file-upload)):
`Ui.FileUpload` around a `Ui.FileUploadDropzone`, and a `Ui.FileItem` per file with a `Ui.FileItemRemove` in its
`Actions`. Where Flux binds a Livewire property, the files come to the page through `OnFiles` — the same upload
a plain file input uses — and the page draws the list from its own state:

```csharp
Ui.FileUpload.Label("Upload files").Multiple().OnFiles(Keep)[
    Ui.FileUploadDropzone.Heading("Drop files here or click to browse").Text("JPG, PNG, GIF up to 10MB")
],
Div.Class("mt-4 flex flex-col gap-2")[
    _photos.Select((photo, index) =>
        Ui.FileItem.Key(photo.Name).Heading(photo.Name).Size(photo.Size).Image(photo.Url)
            .Actions(Ui.FileItemRemove.AriaLabel("Remove file: " + photo.Name).OnClick(() => _photos.RemoveAt(index))))
]
```

Write the remove handler where the list is, as above: a handler re-renders the component it closes over, and
one built in a static helper that closed over the list alone would remove the file and redraw nothing.

| | Takes |
|---|---|
| `Ui.FileUpload` | `Name`, `Multiple()`, `Label`, `Description`, `Error` (a message, which also marks the input invalid), `Disabled()`, and Rask's `OnFiles`; `Accept` is the input's own attribute. Its children are the dropzone, or any markup of your own (an avatar to click). |
| `Ui.FileUploadDropzone` | `Heading`, `Text`, `Icon` (`CloudArrowUp` by default), `Inline()` for the compact row, `WithProgress()` for a bar in place of `Text` while files upload. |
| `Ui.FileItem` | `Heading`, `Text` (written from `Size` when unset: `162400` → `159 KB`), `Image` (a preview's address), `Size` in bytes, `Icon` (`Document` by default), `Invalid()`, and the `Actions` slot. |
| `Ui.FileItemRemove` | `OnClick`, and `AriaLabel` ("Remove file" by default — name the file in a list of several). It removes nothing itself. |

`Ui.FileUpload` is a `<label>` around a real `<input type="file">`, so a click anywhere opens the picker and the
input keeps the keyboard (Space and Enter open it) with no script; the focus ring is drawn on the dropzone. While
files are dragged over it the runtime writes `data-dragging` on it (the existing `data-rask-dropzone` hook) and the
input is laid over the whole area, so the drop is the browser's own. From the moment files are chosen until
`OnFiles` has rendered, the runtime writes `data-loading` on the upload (its `data-rask-loading` hook, which the
upload asks for) and the dropzone shows a spinner — or, with `WithProgress()`, a bar as wide as
`--ui-file-upload-progress` with `--ui-file-upload-progress-as-string` beside it: Flux's attribute, and its two
variables under the kit's prefix, filled from the runtime's `--rask-progress` pair and `0%` at rest. On the
Server host that is the upload request's own progress. In a WebAssembly app nothing is sent, so it is how much
of the files your handler has read through `OpenReadStream`, and stays at `0%` for one that never opens them.
In markup of your own, `in-data-dragging:` and `in-data-loading:` style the two states.

The field's label is a `<label for>` like every other field's, so a click on it opens the picker — as a click
on Flux's label does — and the input is named by it through `aria-labelledby`, as Flux's is, so the words of
the dropzone are not read out as part of its name.

`Ui.FileInput` and its `Dropzone()` mode are gone. A plain file field is the input, as on Flux's page —
`Ui.Input.Of<string>().Type(InputType.File).Label("Logo").OnFiles(…)`.

**A field with no value yet opens on its type alone**: `Ui.Input.Of<string>().Label("Search")`. A form
control's openings are its mode pins, so a required step like `Label` never gets to pin `T` — without
`Of` a controlled field with nothing in it would have to invent a value to compile. `Of` is the
controlled mode: the parent still owns whatever the field ends up with.

`Ui.Calendar` is the one to read twice. `Month` and `OnMonth` are the **view**, not the value — paging
through months changes nothing a form would submit, which is why they sit outside the binding. Leave `Month`
unset and the calendar pages by itself.

**Several days and a range are the same entry, told apart by the model** — the way `Ui.Select` holds several
answers when it is opened on a collection:

```csharp
Ui.Calendar.Bind(() => model.Delivery).Label("Delivery")   // DateOnly: one day
Ui.Calendar.Bind(() => model.DaysOff).Label("Days off")    // List<DateOnly>, HashSet<DateOnly>, …: several
Ui.Calendar.Bind(() => model.Stay).Label("Stay")           // UiDateRange: a range
Ui.Calendar.Values([monday, friday]).Label("Days off")     // several, controlled
Ui.Calendar.Value(new UiDateRange(from, to)).Label("Stay") // a range, controlled
```

`UiDateRange(Start, End)` is always whole: the reader's first click is held by the control and drawn as the
start, and the model changes only when the second click gives the range an end — in date order, whichever end
was clicked first. So a bound model never holds half a range. `default(UiDateRange)` is nothing chosen, as
`default(DateOnly)` is for one day; bind the nullable where the two must differ.

**`Ui.DatePicker` is the field.** A field-shaped button showing the choice in the reader's short date format, with
the grid in a popover — the browser's, so the top layer, Escape, a click outside and focus back on the button
come with it. It is a form field like `Ui.Otp` (`Label`, `Hint`, `Error`, `Badge`, validation), and it takes the
same three openings: one day closes the popover on the pick, several days keep it open while they are added, and
a range closes on the click that gives it its end.

```csharp
Ui.DatePicker.Bind(() => booking.Stay).Label("Stay").Min(DateOnly.FromDateTime(DateTime.Today))
```

Nothing in either is typed. Where a date may be months away, a `Ui.Input` of type date is faster than paging, and
it is the only route for somebody who cannot use a pointer comfortably. There is no drawn time picker:
`Ui.Input.Type(InputType.Time)` is the platform's own.

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

If you write your own `ui-*` classes, there is nothing to copy: the kit's `@theme` reaches your Tailwind
build through `@import "./vendor/rask-ui.css"`, so `bg-ui-brand/10` or `text-ui-warn-ink` in your own
markup compiles against the same tokens the kit's components use.

## Two rules it holds itself to

**Mobile-first, which is a different claim from responsive.** Every control takes a 44px touch target
below `sm`. A dialog is a bottom sheet on a phone and a centred card above it, because a centred
dialog at 360px either overflows or shrinks its content past reading. The tab bar scrolls sideways
rather than wrapping, so the header is exactly one row tall however many tabs there are.

**Every control has a name.** A label is required, not optional, and it becomes the accessible name
rather than a placeholder — a placeholder disappears the moment typing starts, so the one thing saying
what a field is for vanishes exactly when a reader might check it. An icon-only button names itself with
`AccessibleLabel`, written as `aria-label`; a spinner is `aria-hidden` with its words beside it; a toast's
variant changes its **icon** and not only its colour.

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
