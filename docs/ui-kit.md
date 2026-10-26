# The UI kit (`Rask.Ui`)

**[Flux UI](https://fluxui.dev)'s component catalogue, as typed Rask components.** Each is built from Flux's
public docs — measured in a browser in light and dark and held to them by a parity test — and its styles ship
inside the package, so a Rask app gets the whole catalogue without a single utility string, an npm install, or a
Tailwind configuration of its own.

It is **markup and nothing else**: no data access, no host dependency, and no JavaScript — with two exceptions, the
[rich text editor](#rich-text-editor) and the [phone](#phone) field, whose engines are scripts a page loads only when
it draws one. It runs on
the ASP.NET host and in browser-WebAssembly, which is the one place it differs from `Rask.Dashboard` —
the console is deliberately server-only because its panels read a `DbContext`.

```bash
dotnet add package Rask.Ui
```

```csharp
using Rask;

Ui.Button.Primary["Save"]
```

**Every component, live, with a reference of its steps: [the UI tab on rask.sh](https://rask.sh/ui/)** — the
[Header](https://rask.sh/ui/header/) and [Sidebar](https://rask.sh/ui/sidebar/) layouts, then
[Button](https://rask.sh/ui/button/), [Badge](https://rask.sh/ui/badge/), [Callout](https://rask.sh/ui/callout/),
[Card](https://rask.sh/ui/card/), [Heading](https://rask.sh/ui/heading/), [Text & link](https://rask.sh/ui/text/),
[Separator](https://rask.sh/ui/separator/), [Icon](https://rask.sh/ui/icon/), [Dropdown](https://rask.sh/ui/dropdown/),
[Context menu](https://rask.sh/ui/context/), [Command palette](https://rask.sh/ui/command/),
[Popover](https://rask.sh/ui/popover/), [Modal](https://rask.sh/ui/modal/), [Progress](https://rask.sh/ui/progress/),
[Skeleton](https://rask.sh/ui/skeleton/), [Toast](https://rask.sh/ui/toast/), [Tooltip](https://rask.sh/ui/tooltip/),
[Accordion](https://rask.sh/ui/accordion/), [Avatar](https://rask.sh/ui/avatar/), [Table](https://rask.sh/ui/table/),
[Chart](https://rask.sh/ui/chart/), [Kanban](https://rask.sh/ui/kanban/), [Timeline](https://rask.sh/ui/timeline/),
[Carousel](https://rask.sh/ui/carousel/), [Flag](https://rask.sh/ui/flag/), [Brand](https://rask.sh/ui/brand/),
[Breadcrumbs](https://rask.sh/ui/breadcrumbs/), [Navbar](https://rask.sh/ui/navbar/),
[Navlist](https://rask.sh/ui/navlist/), [Pagination](https://rask.sh/ui/pagination/),
[Profile](https://rask.sh/ui/profile/), [Tabs](https://rask.sh/ui/tabs/), [Field](https://rask.sh/ui/field/),
[Input](https://rask.sh/ui/input/), [Textarea](https://rask.sh/ui/textarea/), [Select](https://rask.sh/ui/select/),
[Checkbox](https://rask.sh/ui/checkbox/), [Radio](https://rask.sh/ui/radio/), [Switch](https://rask.sh/ui/switch/),
[Slider](https://rask.sh/ui/slider/), [OTP input](https://rask.sh/ui/otp-input/),
[Autocomplete](https://rask.sh/ui/autocomplete/), [Pillbox](https://rask.sh/ui/pillbox/),
[Calendar](https://rask.sh/ui/calendar/), [Date picker](https://rask.sh/ui/date-picker/),
[Datetime picker](https://rask.sh/ui/datetime-picker/),
[Time picker](https://rask.sh/ui/time-picker/), [Color picker](https://rask.sh/ui/color-picker/),
[File upload](https://rask.sh/ui/file-upload/), [Phone](https://rask.sh/ui/phone/),
[Rich text editor](https://rask.sh/ui/editor/), [Composer](https://rask.sh/ui/composer/) and
[Toggle](https://rask.sh/ui/toggle/), with
[theming](https://rask.sh/ui/theming/) and [dark mode](https://rask.sh/ui/dark-mode/).

## Principles

The kit's behaviour follows the practices [Flux UI](https://fluxui.dev) set out for Livewire, adapted to a
C# component framework that ships no script of its own:

- **Use the browser.** A dialog is a modal `<dialog>` opened by an invoker command; a menu and a listbox
  are `[popover]`s; a sidebar is a checkbox drawer. The top layer, Escape, light-dismiss and focus
  return are the platform's, and they work before any runtime has booted.
- **Use CSS.** A submenu's flyout is shown by a `:hover` rule, a button's spinner is a `[data-loading]` rule.
  Where something truly needs script — pressing a menu row, the triangle that follows the pointer to a flyout,
  holding the page still behind a popup, marking a button that is waiting on its handler — the framework runtime
  does it, generically, for every
  control, not the kit.
- **Accessible by default.** Every field describes itself (`aria-describedby`, `aria-invalid`, `aria-required`),
  menus carry a keyboard cursor, the current navigation item says `aria-current="page"`, a waiting button says
  `aria-busy` — none of it opt-in.
- **One vocabulary.** `Position` + `Align` place everything that floats, events are `On…`, `Kbd` shows a shortcut
  wherever one is shown, `Tone`/`Variant`/`Size` style everything.
- **We style, you space.** Components bring padding, borders and colour — never an outer margin.
- **Its own words are translatable.** The few texts the kit writes itself — a pager's "Showing 1 to 10 of 13
  results", a select's "No results found", a date picker's "Select a date", the name of a close button — are
  `RaskString` keys. The kit speaks English and Hungarian out of the box, in the languages your app lists in
  `SupportedCultures`; a `Resources/RaskStrings.{culture}.json` in your app adds a language or changes a word
  ([every key and its English](localization.md#translating-the-frameworks-own-text)).
- **Simple first, composable after.** `Ui.Input.Label("Email").Description(…)` is one line; `Ui.Navlist` with
  `Ui.NavlistGroup`s and `Ui.NavlistItem`s, or a `Ui.Menu` with `Ui.MenuSubmenu`s, is there when one line is not enough.

## Wiring it up

> **An app on `RaskApp` or the WASM host needs none of this in its code.** `rask new` writes the one
> import below into `Styles/app.css`; the host links the compiled `css/app.css` and writes the dark-mode script
> (`Ui.AppearanceScript`) at the top of every page's head, so `App.cs` is a title and a router. `app.Configure(c => c.Ui.Off())` leaves the kit out.
> This section is for a hand-wired `AddRask()`/`MapRask<App>()` host, or an App that overrides `Shell`.

Two things: forgetting the first renders structurally correct components with **no styling at all**, and
forgetting the second leaves the page light whatever the reader chose.

**1. Take the kit into your stylesheet.** One line in `Styles/app.css`, in place of
`@import "tailwindcss";`:

```css
@import "./vendor/rask-ui.css";
```

Your app then compiles **one stylesheet** — Tailwind, the kit's theme, its `dark` variant, and the classes the kit's components write, beside the classes you write — exactly as a
[Flux](https://fluxui.dev) app's one Tailwind build scans Flux's own views. The kit's class names are
C# string literals inside a compiled assembly, where no scan can find them, so the package ships them
as a list and the import reads it.

There is nothing to set in the `.csproj`. The build sees the import, writes the kit's Tailwind sources
into `Styles/vendor/` before Tailwind runs — `rask-ui.css`, `rask-ui.kit.css` and `rask-ui.classes.txt`;
generated, not committed, no npm — and records in the assembly that the kit is
already in the app's sheet. [The Tailwind guide](tailwind.md#what-a-new-project-starts-with) walks
through the four lines of `rask-ui.css`, and how to write them out yourself when you want Tailwind
without its preflight or a layer of your own.

**2. Link that sheet, with the dark-mode script before it** — the only code that runs ahead of the first
paint, so the only place a dark page can start dark:

```csharp
protected override Component? HeadAssets =>
[
    Ui.AppearanceScript,
    Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/css/app.css"),
];
```

**The layer order is one statement, and the import owns it.** `rask-ui.css` opens with

```css
@layer properties, theme, base, components, rask, utilities;
```

which puts your utilities above Tailwind's preflight and above the kit's own rules — a
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

## Dark mode

Dark mode is [Flux UI's](https://fluxui.dev/docs/dark-mode): a `dark` class on `<html>`, and `dark:`
utilities that follow it. There is no theme list and no theme picker.

`RaskApp` and the WASM host write `Ui.AppearanceScript` at the top of every page's head, as Flux's layout
writes `@fluxAppearance`. A hand-wired host puts it in the root component's head assets, **before the
stylesheets** (an App that still writes its own gets exactly one — its own, with its key):

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

The kit's import already points `dark:` at the class, as Flux does — `@custom-variant dark (&:where(.dark,
.dark *));` — and `.dark` also says `color-scheme: dark`, so scrollbars, native controls and `light-dark()` turn
with the page. Nothing else switches it: there is no theme attribute and no operating-system arm outside the
script.

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

The **accent** is three variables, Flux's own names — the fill of a primary action (`--color-accent`), the
same hue as readable text (`--color-accent-content`), and the text on the fill (`--color-accent-foreground`) —
with a second set under `.dark`. Written after the kit's import, yours win over the kit's zinc defaults:

```css
@theme {
  --color-accent: var(--color-red-500);
  --color-accent-content: var(--color-red-600);
  --color-accent-foreground: var(--color-white);
}

@layer theme {
  .dark {
    --color-accent: var(--color-red-500);
    --color-accent-content: var(--color-red-400);
    --color-accent-foreground: var(--color-white);
  }
}
```

The utilities follow: `bg-accent`, `text-accent-content`, `text-accent-foreground`, `border-accent`, and
`var(--color-accent)` in an arbitrary value.

### The typeface

The kit is drawn in [Inter](https://github.com/rsms/inter), as Flux is, and `Ui.Chart` lays its labels out
with Inter's metrics. Its theme names `"Inter"` first in `--font-sans` — the face Tailwind's preflight, the
kit's own document reset and the `font-sans` utility all read — with the system stack after it, and its
`@font-face` rules serve the variable face (every weight) from `/fonts/inter-latin.woff2` and, for text
outside latin such as Hungarian's ő and ű, `/fonts/inter-latin-ext.woff2`. **Every app's build copies the
two files into `wwwroot/fonts`**, with the OFL notice as `wwwroot/fonts/inter.LICENSE.txt`; in a
one-project WebAssembly app the browser half writes them, as it does the editor's engine. An app that
draws in another face re-points the token in its own `@theme`, and one that serves Inter itself turns the
copy off:

```xml
<PropertyGroup>
  <RaskUiWriteFonts>false</RaskUiWriteFonts>   <!-- true: a host that is not a Web SDK project -->
</PropertyGroup>
```

```css
@theme {
  --font-sans: "Geist", ui-sans-serif, system-ui, sans-serif;
}
```

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
Ui.Button.IconTrailing(Ui.Badge.Sm["3"])["Categories"]        // Flux's iconTrailing slot: content in the icon's place
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
| `Icon`, `IconTrailing` | a `Ui.IconName`; `IconVariant` picks another drawing of it. `IconTrailing` also takes a component — Flux's `iconTrailing` slot — placed as it is where the icon would stand, the button padded as around an icon. One step: naming an icon AND handing content in one chain is RASK044 |
| `Square` | as wide as tall. Automatic with an icon and no children; `Square(false)` turns that off |
| `Align` | `Ui.Align.Start` / `Center` / `End`, for a button wider than its content |
| `Inset` | `Ui.Inset` flags — `Top`, `Bottom`, `Left`, `Right`, `All` — for a ghost or subtle button |
| `Loading` | see [Buttons that wait](#buttons-that-wait) |
| `Tooltip`, `TooltipPosition`, `TooltipKbd` | Flux's shorthand: the button is wrapped in a [`Ui.Tooltip`](#tooltips) that says this, on that side (`Ui.TooltipPosition`), with that shortcut after it. It names a button that has only an icon (`aria-labelledby`), and still fuses inside a `Ui.ButtonGroup` |
| `Kbd` | a shortcut drawn inside the button, after its label: `Ui.Button.Kbd("esc")["Cancel"]` |
| `Href` | see [Buttons and links that go somewhere](#buttons-and-links-that-go-somewhere) |
| `As` | `Ui.ButtonAs.Div` for the look of a button on something that is not one |
| `Type`, `Disabled`, `Command`, `CommandFor` | the `<button>`'s own attributes |

A selected toggle is `.AriaPressed(AriaPressed.True)` on the variant that reads as selected — `Filled`.

## One vocabulary for placing things

Everything that floats against something else is placed with the same two words, the ones Flux UI uses:
**`Position`** picks the side (`Ui.Position` — Top, Right, Bottom, Left) and **`Align`** slides it along that
side (`Ui.Align` — Start, Center, End, following the reading direction). They are two properties because
they compose — a menu above its trigger, flush with the trigger's end edge, is both.

A component rebuilt on Flux has its own pair, with Flux's values and Flux's default first — a dropdown is
`Ui.DropdownPosition` (Bottom, Top, Right, Left) and `Ui.DropdownAlign` (Start, Center, End), each member a chain
step:

```csharp
Ui.Dropdown.Top.End[ trigger, Ui.Menu[ … ] ]
Ui.Tooltip.Content("Copy").Right[ … ]   // Flux's own Ui.TooltipPosition / Ui.TooltipAlign — see Tooltips
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
placement: `Ui.Brand`'s `me-4`, the gap Flux puts between a brand and the navbar after it, and
`Ui.Toast`'s 24px from the viewport's edges, which is where Flux puts a toast.

## Components that are one element

A button is a `<button>`, and a table cell is a `<td>`. `Ui.Button`, `Ui.Badge`, `Ui.BadgeClose` and
the parts of a `Ui.Table` do not wrap a raw element; they are the element. They derive from **`UiElement`**, which derives
from `Element`, so every step an element takes works on them unchanged, the events included. What they
show is their **children**, the same as a raw element's:

```csharp
Ui.Button.Primary.Icon(Ui.IconName.Check).Id("save").OnClick(Save)["Save"]

Ui.Badge.Color(Ui.Color.Green)["Live"]

Ui.TableCell.Id("total").Class("py-0")[Ui.Badge.Sm.Green["Paid"]]
```

A button and a badge take their icons as props and size them themselves.

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

`Ui.Accordion` is [Flux's accordion](https://fluxui.dev/components/accordion): a stack of `Ui.AccordionItem`s, each a
native `<details>` whose heading opens it with a click, Enter or Space and no handler. `Exclusive()` is the platform's
`<details name>` group, `Transition()` animates `::details-content`, and find-in-page opens the item holding a match.
Every example, what the browser owns and the reference of its steps:
**[rask.sh/ui/accordion](https://rask.sh/ui/accordion/)**.

On paper it prints open: every item's content, closed ones included, with no chevron ([printing](#printing)).

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
with its own tag inside, as `Ui.Button` does once it has a `Tooltip`. **`Ui.Table`** is the other place
this shows: it sits in a box that scrolls, and a heading wraps its label. Each renders its own tag with what
it adds inside or around it. The id, classes, data, ARIA and handlers you set stay on the `<table>` and the
`<th>`, so `#orders tbody tr` finds the rows.

## Tabs

`Ui.TabGroup`, `Ui.Tabs`, `Ui.Tab` and `Ui.TabPanel` are [Flux's tabs](https://fluxui.dev/components/tabs): a row, a
segmented control or pills, over panels or on their own. The selected tab is the row's value: `Bind(() => Tab)`,
`Value(_tab).OnChange(…)`, or neither and the row keeps track itself. Switching is drawn in the browser and sends
nothing; the panels not shown stay rendered and `hidden` (`hidden="until-found"` when `Findable()`), so a form in tabs
keeps what was typed on every panel, and a tab switch never counts as an unsaved change in a `ConfirmLeave` form. The
arrows select as they go, and a tab is never a link. Every example, the keyboard, Flux's attributes as steps and the
reference: **[rask.sh/ui/tabs](https://rask.sh/ui/tabs/)**.

## Tables

`Ui.Table` is [Flux's table](https://fluxui.dev/components/table), part for part: `Ui.TableColumns` of
`Ui.TableColumn`s and `Ui.TableRows` of `Ui.TableRow`s of `Ui.TableCell`s. **It sorts nothing and pages nothing**: the
page keeps the sorted column, its direction and the page number (`Sortable()`, `Sorted`, `Direction`, `OnSort`, and
`Paginate(Ui.Pagination…)` under the rows). A slow page change keeps the rows and dims them with nothing to write;
`w-full` on the table and a width on every column but one keep the columns still from page to page
(`ContainerClass("min-w-0")` inside a grid or a flex row). Every example, the dimming marks, the steady-columns recipe,
sticky parts, bleed, whose class wins, and the reference of its steps:
**[rask.sh/ui/table](https://rask.sh/ui/table/)**.

**`Ui.Table.Of(query)` draws it from a [Rask.Query](query.md) query, with skeleton rows while it loads** (not Flux's:
an addition the owner approved, 2026-10-21). The headings never leave. The first load shows a skeleton row per row
the pager says the page holds; every later one (a page, by button or by link, a sort, a filter, a tab) turns the rows
on screen into skeleton rows AT THE PRESS, in the browser, at their own count and heights, and the new rows replace
them in one step, so nothing under the table moves. A query that found nothing shows one row saying so (`TableEmpty`).

```csharp
var drivers = QueryClient.Query(QueryKey.Of("drivers"), () => (_page, _fuel), (asked, ct) => Drivers.Page(asked, ct));

Ui.Table.Id("drivers").Of(drivers).Paginate(Ui.Pagination.Paginator(pager).OnPage(page => _page = page))[
    Ui.TableColumns[Ui.TableColumn["Name"], Ui.TableColumn["Fuel"]],
    Ui.TableRows[(drivers.Data ?? []).Select(d => Ui.TableRow.Key(d.Id)[Ui.TableCell[d.Name], Ui.TableCell[d.Fuel]])]
]

Button.Aria("controls", "drivers").OnClick(Reload)["Reload"]   // a control outside the table names it
```

The pager, a sortable heading, a tab above the table and a [`Ui.Filters`](#filters) before it ask for the skeleton by
themselves — the filter set names the first `Ui.Table.Of(…)` after it, giving it an id if it has none. Any other control
elsewhere names the table with `aria-controls`. A button inside a row never does. A table without `Of` dims its rows
instead.

**A table that waits for more than its rows' query** names the others after it: `Ui.Table.Of(listed, columns)` (up
to three). The skeleton shows while any of them loads — a query that says which columns to draw, say, so the columns
never jump in after the rows — and the rows, their count and the empty row are the first query's.

**`Ui.Table.Loading(…)` does the same for rows the page loads into a field of its own**, with no query: skeleton rows
while they are on their way (a row per row the pager says the page holds, 15 with no pager), and the empty row when
there are none.

```csharp
Ui.Table.Loading(_rows is null)[
    Ui.TableColumns[Ui.TableColumn["Name"], Ui.TableColumn["Fuel"]],
    Ui.TableRows[(_rows ?? []).Select(d => Ui.TableRow.Key(d.Id)[Ui.TableCell[d.Name], Ui.TableCell[d.Fuel]])]
]
```

## Printing

The kit prints what a page says and leaves off what it does — a **departure from Flux**, which prints its screen.
Inside `@media print` only, so nothing changes on screen, and the same for [Rask.Pdf](pdf.md) and a reader's own
Ctrl+P:

- **Left off:** every `Ui.Button`, a badge's ×, `Ui.Pagination`, `Ui.Sidebar` with its toggle and backdrop,
  `Ui.Header`, toasts, tooltips, open dropdown and popover panels, and a modal's backdrop. `Ui.Main` drops its screen
  padding, border and inner scroll.
- **`Ui.Table` prints whole:** cells wrap (nothing truncated or held to one line), the scroll frame lets the table
  run on, the heading repeats at the top of every page, and no row is split across two.
- **`Ui.Accordion` prints open,** closed items included, with no chevron.

```csharp
Div.Class("print:hidden")[Ui.Callout["Only on screen."]]   // your own parts: Tailwind's print: variants
Ui.Button.Class("print:inline-flex!")["Kept on paper"]      // a kit control you want printed: an important utility
```

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

`Ui.Tooltip` is [Flux's tooltip](https://fluxui.dev/components/tooltip): a line of help beside its first child,
shown on hover and keyboard focus by the runtime (`data-rask-tooltip`), in the top layer, with no delay. It names an
icon button (`aria-labelledby`) and describes anything else (`aria-describedby`); `Toggleable()` opens it on a click,
the one tooltip a phone shows. `Ui.Button.Tooltip("…")` is the shorthand. Every example, what opens it, what a screen
reader hears and the reference of its steps: **[rask.sh/ui/tooltip](https://rask.sh/ui/tooltip/)**.

## Rich text editor

`Ui.Editor` is [Flux's editor](https://fluxui.dev/components/editor): a toolbar over an editable area, whose value
is HTML, on [Tiptap](https://tiptap.dev) as Flux's is. `Bind` writes the document with the next action (`.Live()`:
at every change), `Toolbar("heading | bold italic ~ undo redo")` names the items, and its parts compose a toolbar of
your own with `Ui.EditorButton`s that run C#. The engine is a static file every app's build writes and the browser
fetches the first time an editor mounts (`<RaskUiEditorEngine>false</RaskUiEditorEngine>` keeps it out). The value
is the reader's HTML: sanitize it before it is rendered as markup anywhere. Every example, the items and their keys,
the extensions and the reference: **[rask.sh/ui/editor](https://rask.sh/ui/editor/)**.

### Toolbar

Every item, its shortcut, Markdown while typing, and composing a toolbar from the parts:
**[rask.sh/ui/editor](https://rask.sh/ui/editor/)**.

### Extensions

The extensions that are on and off, and the `ui:editor` event that changes them (Flux's `flux:editor` under the
kit's name): **[rask.sh/ui/editor](https://rask.sh/ui/editor/)**.

## Composer

`Ui.Composer` is Flux UI's composer: a message box with buttons around it, for a chat or a prompt. It sends the form
it is in on Ctrl+Enter or Cmd+Enter (`.Enter`: on Enter, Shift+Enter for a new line) through the runtime's
`data-rask-submit-keys`, grows a line at a time to `MaxRows`, and takes a `Ui.Editor` in its `Input` slot for rich
text; typing sends nothing. Every example, its slots and states, and the reference:
**[rask.sh/ui/composer](https://rask.sh/ui/composer/)**.

## Pagination

`Ui.Pagination` is [Flux's pagination](https://fluxui.dev/components/pagination), handed a `UiPaginator` where Flux
takes a Laravel paginator: `Page`, `PerPage` (fifteen when unset) and `Total`, or `HasMore` for the simple paginator.
It pages nothing itself. With `OnPage` each page is a button; with `Href` each is a link, which is how the page goes in
the query string. `ScrollTo` brings the list back into view, `Ui.Table.Paginate(…)` puts it under a table's rows, the
runtime marks it `data-loading` while the next page is slow, and the page size is a `Ui.Select` of the page's own.
Every example, the numbering rules, paging on the server and the reference:
**[rask.sh/ui/pagination](https://rask.sh/ui/pagination/)**.

## Timeline

`Ui.Timeline` is Flux UI's `flux:timeline`: events or steps in order, down the page or across it
(`Horizontal()`), each `Ui.TimelineItem` a `Ui.TimelineIndicator` and the `Ui.TimelineContent` beside it, with status,
coloured or bare indicators, `Ui.TimelineBlock` and `Ui.TimelineSubgrid` for a card in the line, and two CSS variables
for spacing. Every example and the reference of its steps: **[rask.sh/ui/timeline](https://rask.sh/ui/timeline/)**.

## Carousel

`Ui.Carousel` is Flux UI's `flux:carousel`: `Ui.CarouselSlide`s that scroll sideways and snap, in the browser. The
runtime's carousel hook (`data-rask-carousel`) moves the arrows, the indicators, autoplay and a `Ui.CarouselControls`
of the same `Name` elsewhere on the page, with nothing sent to the page. Every example and the reference of its steps:
**[rask.sh/ui/carousel](https://rask.sh/ui/carousel/)**.

## Toggle

`Ui.Toggle` is Flux UI's `flux:toggle`: a setting turned on or off from a compact `<button role="switch">` whose
icon fills in when it is on, with a tooltip that names it, labels per state, and a spinner while a bound toggle's
`OnChange` saves. One that nobody listens to is turned in the browser and sends nothing (`data-rask-switch`). Every
example and the reference: **[rask.sh/ui/toggle](https://rask.sh/ui/toggle/)**.

## Flag

`Ui.Flag` is Flux UI's `flux:flag`: a country's flag from its two-letter region code (`Ui.Flag.Country("US")`), 3:2
or `Circle()`, in five widths, or an image of your own with `Src`. Each is one `<img loading="lazy">` of
`/flags/<CC>.svg`, a file the app's build writes into `wwwroot/flags` (`<RaskUiWriteFlags>true</RaskUiWriteFlags>`
where the build would not). Every example, where the drawings come from, and the reference of its steps:
**[rask.sh/ui/flag](https://rask.sh/ui/flag/)**.

## Phone

`Ui.Phone` is Flux UI's `flux:phone`: an international number with a searchable list of flags, formatted as it is
typed by intl-tel-input 29.2.3 (the release Flux runs) and bound as E.164 once it is a possible number, the text as
typed until then. Everything runs in the browser from two lazily fetched files
(`<RaskUiPhoneEngine>false</RaskUiPhoneEngine>` keeps them out). Every example and the reference:
**[rask.sh/ui/phone](https://rask.sh/ui/phone/)**.

## Buttons and links that go somewhere

Every kit component that goes somewhere takes a `RouteUrl`: `Ui.Button.Href`, `Ui.Link.Href`,
`Ui.NavbarItem.Href`, `Ui.NavlistItem.Href`, `Ui.SidebarItem.Href`, `Ui.SidebarBrand.Href`, `Ui.MenuItem.Href`, `Ui.BreadcrumbsItem.Href`, `Ui.Avatar.Href` and `Ui.Brand.Href`. All of them follow one rule. Hand one a
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

[Flux UI's two layouts](https://fluxui.dev/layouts/sidebar), part for part: `Ui.Header`, `Ui.Sidebar` and
`Ui.Main`, written as **siblings**. Whatever holds the `Ui.Main` (the `<body>`, or a wrapper) becomes the layout
grid: the header across the top, the sidebar down the side, the main in what is left. Both states of the sidebar,
slid over a phone and narrowed to a rail, are checkboxes, so they work before anything has loaded; the runtime puts
the sidebar away when the app navigates and remembers the rail under Flux's own key. Every demo on Flux's two
layout pages, framed live with its code and a Fullscreen page of its own, what the runtime keeps, the rail's
tooltips and menus, and the reference of every part: **[rask.sh/ui/header](https://rask.sh/ui/header/)** and
**[rask.sh/ui/sidebar](https://rask.sh/ui/sidebar/)**.

```csharp
protected override Component? Render() =>
[
    Ui.Sidebar.Sticky(true).Collapsible(Ui.SidebarCollapsible.Always)[
        Ui.SidebarHeader[Ui.SidebarBrand.Href(Routes.HomePage()).Logo("/logo.png").Name("Acme Inc."), Ui.SidebarCollapse],
        Ui.SidebarNav[Ui.SidebarItem.Icon(Ui.IconName.Home).Href(Routes.HomePage())["Home"]],
        Ui.SidebarSpacer,
        Ui.SidebarProfile.Avatar("/me.png").Name("Mara Lind")
    ],
    Ui.Header.Class("lg:hidden")[Ui.SidebarToggle.Inset(Ui.Position.Left), Ui.Spacer],
    Ui.Main[Main[Outlet]]
];
```

A WebAssembly app adds `Ui.SidebarScript` to its `HeadAssets` beside `Ui.AppearanceScript`, so a remembered rail is
drawn narrow from the first paint. One sidebar a page: its controls find it by fixed ids. Inside a sidebar the list
is the sidebar's own (`Ui.SidebarNav`); anywhere else it is **`Ui.Navlist`**
([rask.sh/ui/navlist](https://rask.sh/ui/navlist/)) or, in a row, **`Ui.Navbar`**
([rask.sh/ui/navbar](https://rask.sh/ui/navbar/)). **`Ui.Brand`**, **`Ui.Breadcrumbs`**, **`Ui.Profile`**,
**`Ui.Separator`** and **`Ui.Heading`** have pages of their own too; **`Ui.Spacer`** is `flex: 1`, pushing what
follows it to the far end of a row or a column.

### Card

`Ui.Card` is [Flux UI's card](https://fluxui.dev/components/card), part for part: `Ui.CardHeader` (`Ui.CardHeading`,
`Ui.CardSubheading`, `Ui.CardActions`), `Ui.CardBody`, `Ui.CardFooter` and `Ui.CardBleed`, and it handles the spacing,
dividers and corners between them. `Body`, `Variant` and `Size` set how the parts are apart, the surface and the room.
Every example, what each step decides, headers outside a card, the bleed variables and the reference of its steps:
**[rask.sh/ui/card](https://rask.sh/ui/card/)**.

### Kanban

`Ui.Kanban` is [Flux UI's kanban](https://fluxui.dev/components/kanban): `Ui.KanbanColumn`s of `Ui.KanbanCard`s, one
column per stage, with a header (heading, count, actions) and a footer. It draws the board and moves nothing; a card
is something to read until `.As(Ui.KanbanCardAs.Button)` makes it one to press. Every example and the reference of
its steps: **[rask.sh/ui/kanban](https://rask.sh/ui/kanban/)**.

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
attributes — `Ui.Button` is what turns them into a spinner, and your own CSS can style `[data-loading]` on any control. `data-rask-loading="off"` on an element, or on a toolbar around several,
opts them out; `data-rask-loading` on a non-button element opts it in. It is never `disabled`, which would
throw keyboard focus off the control mid-press. A Blazor island's buttons get it too — their handlers
dispatch over the same channel.

A **field** is marked the same way once the event that sent its value is unanswered — `data-loading` alone, with no
`aria-busy` — so your own CSS can say `data-loading:opacity-70` on an input that searches as it is typed. The kit
draws nothing for it: Flux's input draws no loading state of its own (its `icon:trailing="loading"` is an icon you
put there), so `Ui.Input` shows no spinner by itself; style one yourself with `in-data-loading:` if you want one.

**The wait ends when the page is whole, not when the handler returns.** A handler's own intermediate state is
not painted while it runs, and the page it answers with waits 150 ms for the reads it started
([routing](routing.md#a-handlers-work-reaches-the-screen-once)); when those are slower, the button stays busy
until they are back. And `data-rask-loading="within"` on a container marks it — `data-loading` only — for a
press anywhere inside: that is how a table knows its pager is waiting.

## What is in it

Grouped by what each piece is for.

| | |
| --- | --- |
| **Actions** | `Ui.Button` `Ui.ButtonGroup` `Ui.Dropdown` `Ui.Menu` `Ui.MenuItem` `Ui.MenuSubmenu` `Ui.MenuSeparator` `Ui.MenuGroup` `Ui.MenuCheckbox` `Ui.MenuCheckboxGroup` `Ui.MenuRadio` `Ui.MenuRadioGroup` `Ui.Navmenu` `Ui.NavmenuItem` `Ui.Context` `Ui.Command` `Ui.CommandInput` `Ui.CommandItems` `Ui.CommandItem` `Ui.Popover` `Ui.Modal` `Ui.ModalTrigger` `Ui.ModalClose` `Ui.Confirm` `Ui.ConfirmLeave` `Ui.Filters` `Ui.Filter` |
| **Data display** | `Ui.Accordion` `Ui.AccordionItem` `Ui.AccordionHeading` `Ui.AccordionContent` `Ui.Avatar` `Ui.AvatarGroup` `Ui.Badge` `Ui.BadgeClose` `Ui.Card` `Ui.CardHeader` `Ui.CardHeading` `Ui.CardSubheading` `Ui.CardActions` `Ui.CardBody` `Ui.CardFooter` `Ui.CardBleed` `Ui.Carousel` `Ui.CarouselSlide` `Ui.CarouselControls` `Ui.Flag` `Ui.Kanban` `Ui.KanbanColumn` `Ui.KanbanColumnHeader` `Ui.KanbanColumnCards` `Ui.KanbanColumnFooter` `Ui.KanbanCard` `Ui.Table` `Ui.TableColumns` `Ui.TableColumn` `Ui.TableRows` `Ui.TableRow` `Ui.TableCell` `Ui.Timeline` `Ui.TimelineItem` `Ui.TimelineIndicator` `Ui.TimelineContent` `Ui.TimelineBlock` `Ui.TimelineSubgrid` `Ui.Chart` `Ui.ChartSvg` `Ui.ChartViewport` `Ui.ChartLine` `Ui.ChartArea` `Ui.ChartPoint` `Ui.ChartBar` `Ui.ChartGroup` `Ui.ChartStack` `Ui.ChartPie` `Ui.ChartAxis` `Ui.ChartAxisTick` `Ui.ChartAxisGrid` `Ui.ChartAxisLine` `Ui.ChartAxisMark` `Ui.ChartZeroLine` `Ui.ChartCursor` `Ui.ChartTooltip` `Ui.ChartTooltipHeading` `Ui.ChartTooltipValue` `Ui.ChartTooltipIndicator` `Ui.ChartSummary` `Ui.ChartSummaryValue` `Ui.ChartLegend` `Ui.ChartLegendIndicator` |
| **Navigation** | `Ui.Navbar` `Ui.NavbarItem` `Ui.Navlist` `Ui.NavlistItem` `Ui.NavlistGroup` `Ui.Brand` `Ui.Profile` `Ui.Breadcrumbs` `Ui.BreadcrumbsItem` `Ui.Link` `Ui.Pagination` `Ui.TabGroup` `Ui.Tabs` `Ui.Tab` `Ui.TabPanel` |
| **Feedback** | `Ui.Callout` `Ui.CalloutHeading` `Ui.CalloutText` `Ui.CalloutLink` `Ui.Progress` `Ui.Skeleton` `Ui.SkeletonLine` `Ui.SkeletonGroup` `Ui.Toast` `Ui.ToastGroup` `Ui.Tooltip` `Ui.TooltipContent` |
| **Data input** | `Ui.Input` `Ui.Textarea` `Ui.Composer` `Ui.Select` `Ui.SelectOption` `Ui.SelectGroup` `Ui.SelectOptionCreate` `Ui.SelectOptionEmpty` `Ui.SelectButton` `Ui.SelectInput` `Ui.SelectSearch` `Ui.ColorPicker` `Ui.ColorPickerArea` `Ui.ColorPickerSlider` `Ui.ColorPickerInput` `Ui.ColorPickerSwatches` `Ui.ColorPickerSwatch` `Ui.ColorPickerDropper` `Ui.Autocomplete` `Ui.AutocompleteItem` `Ui.Pillbox` `Ui.PillboxOption` `Ui.PillboxOptionCreate` `Ui.PillboxOptionEmpty` `Ui.PillboxSearch` `Ui.PillboxTrigger` `Ui.PillboxInput` `Ui.FileUpload` `Ui.FileUploadDropzone` `Ui.FileItem` `Ui.FileItemRemove` `Ui.Checkbox` `Ui.CheckboxGroup` `Ui.CheckboxAll` `Ui.CheckboxIndicator` `Ui.RadioGroup` `Ui.Radio` `Ui.RadioIndicator` `Ui.Switch` `Ui.Toggle` `Ui.Slider` `Ui.SliderTick` `Ui.Field` `Ui.Label` `Ui.Description` `Ui.Error` `Ui.Fieldset` `Ui.Legend` `Ui.Otp` `Ui.OtpInput` `Ui.OtpSeparator` `Ui.OtpGroup` `Ui.Phone` `Ui.Calendar` `Ui.DatePicker` `Ui.DatetimePicker` `Ui.DatetimePickerButton` |
| **Layout** | `Ui.Separator` |
| **Layout** | `Ui.Header` `Ui.Main` `Ui.Sidebar` `Ui.SidebarHeader` `Ui.SidebarBrand` `Ui.SidebarCollapse` `Ui.SidebarSearch` `Ui.SidebarNav` `Ui.SidebarItem` `Ui.SidebarGroup` `Ui.SidebarSpacer` `Ui.SidebarProfile` `Ui.SidebarToggle` |
| **Support** | `Ui.Icon` / `Ui.IconName` / `Ui.IconVariant` (all of Heroicons: outline, solid, mini, micro), `Ui.AppearanceScript` (dark mode), `UiPaginator`, `Ui.Breakpoint`, `UiStylesheet` |

## Who owns the state

The kit ships no JavaScript (the [editor](#rich-text-editor)'s engine apart), and that constraint decides the shape of every interactive component. It
resolves three ways, and which one a component takes is a property of what the platform can do rather
than of anyone's preference.

**The browser owns it, declaratively.** `Ui.Modal` is [Flux's modal](https://fluxui.dev/components/modal): a real
modal `<dialog>` with a `Name`, opened by the button inside a `Ui.ModalTrigger` of that name with no handler — the
trigger makes its button an HTML invoker, so the browser supplies the top layer, an inert page behind it, Escape and
focus handed back, and the runtime adds a click outside and the page held still (`data-rask-modal`,
`data-rask-lock`). `Flyout()`, `Dismissible(false)`, `Escapable(false)`, `Scroll(Ui.ModalScroll.Body)`,
`Ui.ModalClose`, `OnClose` and `OnCancel` are Flux's switches; `Open(_field)` hands the state to the page in place of
`wire:model`. Every example, live, with the reference of its steps: **[rask.sh/ui/modal](https://rask.sh/ui/modal/)**.

`Ui.Confirm` and `Ui.ConfirmLeave` are the two modals here that are not Flux's. Each is the dialog the browser's
own would otherwise ask in, placed once in a layout and drawn as Flux's confirmation — `min-w-[22rem]`, a ghost
button that says no and a danger button that says yes.

`Ui.Confirm` is where an action's [`.Confirm("…")`](forms.md#asking-before-an-action) asks, in place of `confirm`
and `prompt`: a short heading, the call site's question beneath it, and with `expect:` a field that holds Yes back
until the answer is typed exactly. No, the close button, Escape and a press outside call the action off; Yes lets
the very press or submit go. Opening, typing and closing send nothing.

```csharp
Ui.Confirm                                                // "Confirm", "No" and "Yes"
Ui.Confirm.Heading("Megerősítés").No("Nem").Yes("Igen")   // your own words
Ui.Button.Danger.OnClick(Delete).Confirm("Biztosan törli a céget?", heading: "Cég törlése")   // a title of its own
```

`Ui.ConfirmLeave` is where a form's [`ConfirmLeave("…")`](forms.md#ask-before-leaving-unsaved-changes) asks, in
place of `confirm` — the form's message as the heading:

```csharp
Ui.ConfirmLeave                              // "Stay" and "Leave"
Ui.ConfirmLeave.Stay("Nem").Leave("Igen")    // your own words
Ui.ConfirmLeave.Heading("Unsaved changes")   // a short title; the form's question is the sentence beneath it
```

Both are rendered closed and the runtime opens them, so the question appears without a round trip. The close
button, Escape and a press outside mean no. A leave dialog's heading may be a whole question of the form's own,
so it carries `pe-8` to keep clear of the close button; `Heading("…")` gives it the shape `Ui.Confirm` always
has, Flux's: a short title, the question under it. Both are held to Flux's open confirmation by
`scripts/flux/parity-modal.mjs`; the answer field, and a Yes that is no submit button, are the two departures.

`Lazy()` is the one modal step that is not Flux's (the owner's, 2026-10-21): the modal sends what it holds only
while it is open. Closed, the page carries the dialog alone — its id, its close button, its focus placeholder — and
the trigger still opens it in the browser; the runtime tells the page it opened, and one frame brings the content.
Closing takes it away again. With `Open(_field)` it holds its content exactly while the field is true. It is off by
default, because a modal that must open with no server at all (`Ui.ConfirmLeave`, a ⌘K palette) has to hold its
content before anyone asks.

### Filters

`Ui.Filters` is the kit's own — not a Flux component, the owner's (2026-10-21) — and is built from Flux's select,
date picker, modal and badge: a list's filters, each kept in the page's address.

```csharp
[QueryParam("driver")] public string? Driver { get; set; }
[QueryParam("period")] public UiDateRange? Period { get; set; }

var filters = Ui.Filters[
    Ui.Select.Bind(() => Driver).Label("Driver").Listbox.Searchable().Clearable()[…],
    Ui.DatePicker.Range.Bind(() => Period).Label("Period").WithPresets().Clearable(),
];

Ui.Card[                                                   // Flux's full-bleed table, with the button on the right
    Div.Class("flex items-center justify-between gap-4")[
        Div[Ui.Heading["Recent orders"], Ui.Text.Class("mt-1")["What each driver delivered."]],
        filters
    ],
    Div.Class("mt-4")[filters.Badges],                     // filters.Active counts the filters in force
    Ui.Table.Bleed().ContainerClass("mt-6").Of(orders)[…]
]
```

Placed, it draws a small Filters button (a funnel, and the count once a filter is in force) that opens a lazy flyout
of the fields, with Clear filters and Close under them. It drives the first `Ui.Table.Of(…)` built after it: every
field, badge cross, Clear all and Clear filters names that table in `aria-controls` (giving it an id when it has
none), so the table's rows turn into skeleton rows at the press, as for its own pager. Each control is bound to a `[QueryParam]` member and the set
binds it to the address instead, so the router stays the only writer of the member and the page hears `OnUpdated`:
a pick moves the query (replacing the history entry), a text field once typing pauses for 150 ms, and a call site's
own `Debounce` wins. A member without `[QueryParam]` fails as the page renders, naming the declaration to write.
`filters.Badges` is a badge for each filter in force in its control's own words — "Driver: Ada Lovelace", a range
as its picker writes it or the preset that made it, a checkbox's label alone — each with a cross that takes that key
out, and Clear all from two on, which takes every filter's key out in one move and leaves the sort alone.
`Ui.Filters.OnOpen(…)` runs as the panel opens. A filter no control can say — a select of several answers, until a
collection `[QueryParam]` exists — goes in `Ui.Filter.Label("Labels").Shown(…).OnRemove(…)[control]`. A range is one
query value because `UiDateRange` is `IParsable`: `2026-10-01..2026-10-09`, `~last7Days` after it when a preset made
it, and text that is no range leaves the member unset. Live: **[rask.sh/ui/filters](https://rask.sh/ui/filters/)**.

`Ui.Tooltip` is Flux's: a popover too, which the runtime shows under the pointer. See [Tooltips](#tooltips).

**`Ui.Dropdown` and `Ui.Menu` are [Flux UI's dropdown](https://fluxui.dev/components/dropdown), exactly.** The
first child is the trigger, any button, and the second is what it opens: a `Ui.Menu` of actions (`Ui.MenuItem`,
`Ui.MenuSubmenu`, `Ui.MenuSeparator`, `Ui.MenuGroup`, `Ui.MenuCheckbox`, `Ui.MenuRadioGroup` of `Ui.MenuRadio`s), a
`Ui.Navmenu` of links, or a `Ui.Popover`. The menu is a `[popover]`, so the browser owns the open state, and the
runtime keeps the cursor and Flux's keyboard in the browser — a pointer sweeping the rows and the arrow keys send the
page nothing; it hears a pick, and with `OnToggle` the menu opening and closing. `Open(_open)` hands the state to
the page. Every panel the kit opens against a trigger — a menu, a popover, a tooltip, a list of options, a calendar —
stays inside the viewport as Flux's does (the runtime's `data-rask-place`). Every example, live, with the reference of
each part: **[rask.sh/ui/dropdown](https://rask.sh/ui/dropdown/)**.

**The page owns it, in C#.** A `Ui.Toggle` given `Checked` and `Ui.Modal`'s `Open` path hold their state in a field and
redraw through the live diff.

**The browser owns it, and tells the page.** A `Ui.AccordionItem` is a `<details>`: it opens with no handler at
all, and `Expanded` is only the state it starts in — see [Accordion](#accordion).

**And one whose state is a value.** `Ui.Tabs` selects in the browser, and tells the page the selected tab's
name when the page binds it or listens — see [Tabs](#tabs).

**And one that lets you choose.** `Ui.Select` is the browser's own `<select>` by default, and the browser
owns all of it: it works on a prerendered page and with scripting off, which is why it is the default.
`.Listbox` and `.Combobox` draw the list instead — a native `popover` under a `role="combobox"` trigger. The
browser owns where the reader is in it, as it does in Flux: the runtime keeps the lit row, the keys,
type-ahead, the search and whether the list is open (`data-rask-list`), and **none of that sends the page
anything** — a pointer swept over every row, the arrows, a search typed, Escape. The page renders the rows
and hears a pick, so the drawn list **needs the runtime**. Reach for it when an option must carry more than
words, or the list must be searched. The whole of it is under [Select](#select).

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

The browser owns opening and dismissal there — a press, Escape, a click elsewhere — and the runtime writes
`aria-expanded` and `data-open` in the task the list opens or shuts, so they never trail the list by a round
trip and the page hears no toggle.

## Fields: label, description, error

`Ui.Field`, `Ui.Label`, `Ui.Description`, `Ui.Error`, `Ui.Fieldset` and `Ui.Legend` are
[Flux UI's field](https://fluxui.dev/components/field), part for part: a field stacks a label, a control, help text
and the message the form holds for the control, and tells each part which control it belongs to. The label is a real
`<label for>`, and `Ui.Error` reads the form (bare inside a field, `For(() => m.Email)` anywhere, `Message("…")` for
your own words) from a live region that is always in the page. Every control's `Label` and `Description` draw the same
field around it. Every example, the error's rules and the reference: **[rask.sh/ui/field](https://rask.sh/ui/field/)**.

## Select

`Ui.Select` is [Flux UI's select](https://fluxui.dev/components/select), part for part, over Rask's binding:
`Bind` or `Value` where Flux says `wire:model`, and the options as children typed by the select's own value. The
default variant is the browser's `<select>`; `.Listbox` draws the list (icons, descriptions, avatars, content of your
own, a prefix, `Clearable()`), `Searchable()` filters it in the browser by text and `Keywords`, `.Listbox.Multiple()`
over a collection picks several, and `.Combobox` is an input that filters. `Filter(false)` hands the search to the
page, with `Debounce` on `Ui.SelectSearch` or `Ui.SelectInput` for one query a word, and `Ui.SelectOptionCreate`
offers to make what was typed. An open list sends the page nothing until a row is picked. Every example, the
keyboard, searching on the server and the reference: **[rask.sh/ui/select](https://rask.sh/ui/select/)**.

## Autocomplete

`Ui.Autocomplete` is [Flux UI's autocomplete](https://fluxui.dev/components/autocomplete): an input over a list of
suggestions that holds the **text**. To show a name and store an id, use `Ui.Select.Combobox`. `Debounce` hands the
page what was typed once typing has paused, for suggestions from a database; the list, its keys and the pick are the
runtime's (`data-rask-list`). Every example, the keyboard and the reference:
**[rask.sh/ui/autocomplete](https://rask.sh/ui/autocomplete/)**.

## Pillbox

`Ui.Pillbox` is [Flux UI's pillbox](https://fluxui.dev/components/pillbox): several answers out of a list, each a
pill that can be taken off again, bound to a collection. `Searchable()`, `.Combobox`, `Ui.PillboxOptionCreate` and
`Filter(false)` with `Debounce` for options found on the server; the pills stay when a search's rows no longer hold
them. It is the select's list under another trigger, so its keys are the runtime's. Every example, the keyboard and
the reference: **[rask.sh/ui/pillbox](https://rask.sh/ui/pillbox/)**.

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
`Ui.Toast` in the layout is where they appear (`Ui.ToastGroup[Ui.Toast]` stacks them), and an app that places none
gets the host's. Each goes after five seconds, held only by the pointer, through the runtime's generic hooks
(`data-rask-dismiss-after`, `data-rask-dismiss-hold`, `data-rask-dismiss-scope`). Every example, Flux's calls in C#,
the timing and the strings Rask raises one with: **[rask.sh/ui/toast](https://rask.sh/ui/toast/)**.

**`Ui.Context` is the same menu, opened by a right-click** — Flux's `flux:context`. The first child is the area
and the second the `Ui.Menu` a `Ui.Dropdown` would open, so the rows and the keyboard are identical. The runtime
opens it at the pointer at once, on either host, and the ContextMenu key or Shift+F10 at the focused element; iOS
never fires the event, so put the same actions somewhere visible too. Live, with its reference:
**[rask.sh/ui/context](https://rask.sh/ui/context/)**.

**`Ui.Command` is Flux's command palette: a searchable list of commands** — `Ui.Command` around a
`Ui.CommandInput` and the `Ui.CommandItems` list of `Ui.CommandItem`s, each with an `OnClick`. Narrowing, the lit
command and Enter are the browser's (`data-rask-command`), so nothing is sent until a command runs; as a palette it
is a bare `Ui.Modal` opened by `Ui.ModalTrigger.Name("search").Shortcut("mod+k")`. Live, with its reference:
**[rask.sh/ui/command](https://rask.sh/ui/command/)**.

**`Ui.Popover` is Flux's popover: extra content in a panel beside a button**, for what is not a menu — a form, a
set of filters, a card about a person. It is the second child of the `Ui.Dropdown` that holds its trigger, so where
it opens and what opens it (`Hover()`, `Open` with `OnToggle`) are the dropdown's. The browser opens it and closes
it on Escape and a press elsewhere; focus stays on the trigger and Tab walks into the panel. Live, with its
reference: **[rask.sh/ui/popover](https://rask.sh/ui/popover/)**.

**`Ui.Chart` is Flux's chart, part by part, drawn in C#.** No chart library: `Ui.Chart.Value(rows)` and a selector
per part (`.Field((Visit v) => v.Visitors)`), the scales, ticks and paths computed in the render for the box the
browser measured, and the cursor, tooltip, summary and a pie's slice following the pointer in the browser. Every
example, the axes, formats, sizing and pointer rules, and the reference of its steps:
**[rask.sh/ui/chart](https://rask.sh/ui/chart/)**.

**`Ui.Link.External()` opens in a new tab.** `target="_blank"` with `rel="noopener noreferrer"` (a new tab opened
without it can reach back through `window.opener`). A generated route is one of your own pages and is never
external, so it is ignored there.

**`Ui.Skeleton` is Flux UI's skeleton, part for part.** A block the call site sizes and rounds, `Ui.SkeletonLine`
for a line of text, and `Ui.SkeletonGroup` animating everything inside it with a shimmer or a pulse. Every example,
the sizes and the shimmer's colour: **[rask.sh/ui/skeleton](https://rask.sh/ui/skeleton/)**.

**`Ui.Progress` is Flux UI's progress bar.** A `<div role="progressbar">` with `Value`, `Max` and `Color`,
announcing the value and maximum as given and moving to a new one over 300ms. Every example and its reference:
**[rask.sh/ui/progress](https://rask.sh/ui/progress/)**.

**`Ui.Avatar` is Flux's `flux:avatar`, and draws initials when there is no picture.** Give it a `Name` and it works
out the initials, the picture's alternative text and, with `ColorAuto()`, a colour of its own; `Ui.AvatarGroup` stacks
them. Every example and the reference of its steps: **[rask.sh/ui/avatar](https://rask.sh/ui/avatar/)**.

**`Ui.Input` and `Ui.Textarea` are Flux UI's** ([input](https://fluxui.dev/components/input),
[textarea](https://fluxui.dev/components/textarea)), over Rask's binding: `Bind` writes with the next action as
`wire:model` does, `.Live()`, `.Debounce(300.Milliseconds)` and `.Blur()` sooner, and a bound `int` is a number field
with nothing said at the call site. The icons, shortcut and the clear, copy and reveal buttons are steps, `Mask` holds
each keystroke to a pattern, `Money()` groups an amount ([rask.sh/ui/input](https://rask.sh/ui/input/)), and the runtime does by attribute what Flux does in Alpine. `Ui.InputGroup` joins a
prefix, a suffix or a button to the input; in a field, label the group. Every example and the reference:
**[rask.sh/ui/input](https://rask.sh/ui/input/)** and **[rask.sh/ui/textarea](https://rask.sh/ui/textarea/)**.

### Checkbox, radio and switch

**`Ui.Checkbox`, `Ui.Radio` and `Ui.Switch` are Flux UI's**: a checkbox or a switch binds a `bool`, a
`Ui.CheckboxGroup` binds a collection of what is ticked (with `Ui.CheckboxAll`), and a `Ui.RadioGroup` binds one value
of any type. Cards, pills, buttons and a segmented row draw the same real inputs, so the space bar, the arrow keys and
the form post are the browser's. Every example and the reference: **[rask.sh/ui/checkbox](https://rask.sh/ui/checkbox/)**,
**[rask.sh/ui/radio](https://rask.sh/ui/radio/)** and **[rask.sh/ui/switch](https://rask.sh/ui/switch/)**.

**`Ui.Slider` is Flux UI's slider**: a number, or a range over an array of two, on real range inputs, with `BigStep`
for Shift and ticks as children; a label is the field's. **`Ui.Otp` is Flux UI's OTP input**: a cell per character
and the code they spell bound as one string, Flux's keys key for key, and `OnComplete` where Flux's `submit="auto"`
submits. Every example and the reference: **[rask.sh/ui/slider](https://rask.sh/ui/slider/)** and
**[rask.sh/ui/otp-input](https://rask.sh/ui/otp-input/)**.

**The opening step fixes the type argument and the mode together.** `Bind` opens a bound control and
`Value` a controlled one; they are mutually exclusive because a control with both would have two
sources of truth for one field, and the compiler enforces it — both live on the control's entry, so
taking one leaves the other unreachable. `Label`, `Placeholder` and the rest follow in any order, since none
of them says anything about `T`. Bound mode drives the surrounding `Form`'s validation — per-field
`Validate`, `AfterBind`, and the `aria-invalid`/`aria-describedby` display — and controlled mode leaves
the value with the parent. See [building form controls](building-form-controls.md).

**Generic where the value type varies, concrete where it does not.** `UiInput<T>`, `UiTextarea<T>`,
`UiSlider<T>` and `UiSelect<T>` are generic — the model decides what they hold, and `Ui.Input` even
takes its `type` attribute from `T`, so a bound `int` is a number field with nothing said at the call
site. `UiRadioGroup<T>` and `UiCheckboxGroup<T>` are generic over what a choice holds. The rest are closed over
the one type they can have: `Ui.Checkbox` and `Ui.Switch` over `bool`, `Ui.Otp` over `string`,
`Ui.Calendar` and `Ui.DatePicker` over `DateOnly`, a collection of days or a `UiDateRange`. A checkbox's value is a `bool` and nothing else; a type parameter there
would have exactly one legal argument.

| | Binds |
|---|---|
| `UiInput<T>` `UiTextarea<T>` `UiSelect<T>` | what the field holds |
| `UiSelectMultiple<T>` — `Ui.Select` opened on a collection | the ELEMENT type — it binds an `ICollection<T>` |
| `UiRadioGroup<T>` | the value of the chosen `Ui.Radio` |
| `UiCheckboxGroup<T>` | the ELEMENT type — it binds an `ICollection<T>` of the ticked checkboxes' values |
| `Ui.Checkbox` `Ui.Switch` | on or off |
| `UiSlider<T>` | the number under the thumb, or an array of the two under a range's thumbs |
| `Ui.Calendar` | the day |
| `Ui.Otp` | the code — `OnComplete` runs each time its last cell is filled, in both modes |


**File upload is Flux UI's**: `Ui.FileUpload` around a `Ui.FileUploadDropzone`, a `<label>` around a real file input
that takes files clicked, reached by keyboard or dropped, and a `Ui.FileItem` per file with a `Ui.FileItemRemove`.
The files reach the page through `OnFiles`, and the page draws the list from its own state. Every example, dragging,
progress, previews and the reference: **[rask.sh/ui/file-upload](https://rask.sh/ui/file-upload/)**.

**A field with no value yet opens on its type alone**: `Ui.Input.Of<string>().Label("Search")`. A form
control's openings are its mode pins, so a required step like `Label` never gets to pin `T` — without
`Of` a controlled field with nothing in it would have to invent a value to compile. `Of` is the
controlled mode: the parent still owns whatever the field ends up with.

### Calendar

`Ui.Calendar` is Flux UI's calendar. Its mode is the step that opens it: `Ui.Calendar` binds a `DateOnly`,
`Ui.Calendar.Multiple` a collection of days and `Ui.Calendar.Range` a `UiDateRange`, written only once it has both
ends. Min and max, unavailable days, week numbers, inputs over the months, the keyboard and the reference:
**[rask.sh/ui/calendar](https://rask.sh/ui/calendar/)**.

### Date picker

`Ui.DatePicker` is Flux UI's date picker: a field-shaped button over the calendar in a popover, a day or a range
(`Ui.DatePicker.Range`), typed in parts with `Type(Ui.DatePickerType.Input)`, `Ui.DatePickerInput` or
`WithInputs()` (the browser's own date fields with `.Native` and `WithInputs(Ui.DatePickerInputVariant.Native)`),
with presets, `Clearable()`, and Flux's sheet on a phone. A half-filled date is no date, by the owner's decision.
Every example and the reference: **[rask.sh/ui/date-picker](https://rask.sh/ui/date-picker/)**.

### Datetime picker

`Ui.DatetimePicker` is [Flux UI's datetime picker](https://fluxui.dev/components/datetime-picker): a date and a
time from one field — the calendar with a list of times beside it, or the time typed (`.Time(Ui.DatetimePickerTime.Input)`)
— over a `DateTime`, or a start and an end over a `UiDateTimeRange` (`Ui.DatetimePicker.Range`). Wall-clock times,
as a `datetime-local` field holds them: nothing is converted between time zones. `Min`/`Max`, `MinTime`/`MaxTime`,
`Unavailable` days and times (`UiDatetimeSlot`), `DefaultTime`, `WithConfirmation()`, `Clearable()`, a trigger of
your own and the date picker's calendar options; a sheet on a phone.
`Ui.DatetimePicker.Bind(() => m.StartsAt).MinTime(new(9, 0)).MaxTime(new(17, 0))`. Every example, the keyboard and the
reference: **[rask.sh/ui/datetime-picker](https://rask.sh/ui/datetime-picker/)**.

### Time picker

`Ui.TimePicker` is Flux UI's time picker: a button over a list of times, or hour and minute typed in parts. A
`TimeOnly?` picks one or none and a collection of `TimeOnly` several. Every example, the keyboard and the reference:
**[rask.sh/ui/time-picker](https://rask.sh/ui/time-picker/)**.

### Color picker

`Ui.ColorPicker` is [Flux UI's color picker](https://fluxui.dev/components/color-picker): a colour from a palette,
dragged out of a saturation and brightness area, or typed in any CSS form, written back in `Format`. Children
compose the popover from its parts; on a phone it is a sheet. A typed wide-gamut colour becomes what Flux makes of
it, digit for digit, and text that is no colour holds a form's save. Every example, the keys and the reference:
**[rask.sh/ui/color-picker](https://rask.sh/ui/color-picker/)**.

## The rule the whole kit rests on

Tailwind emits a rule only where it can **see** the class name in the scanned source. A name built by
concatenation — `"bg-" + colour + "-500"` — is a name no scanner ever reads, so the class is
absent from the compiled sheet and the component renders **with no styling whatsoever**. Not
misaligned, not the wrong colour: unstyled. The build stays green and the markup carries exactly the
class the call site asked for.

That is why every class the kit can write is spelled out as a complete literal in `UiClassNames.cs`,
and why the axes above are closed enums rather than strings.

If you write the accent yourself, there is nothing to copy: the kit's `@theme` reaches your Tailwind build
through `@import "./vendor/rask-ui.css"`, so `bg-accent/10` or `text-accent-content` in your own markup
compiles against the same tokens the kit's components use.

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
`<select>`. The options are nested in the same class — `Ui.Color`, `Ui.ButtonVariant`, `Ui.IconName`, `Ui.IconVariant`.

The component **types** keep the `Ui` prefix — `UiButton`, `UiSelect<T>` — because a type and a member of
one name cannot both sit on `Ui`. That is the name a field, a parameter, a hover or a compiler message uses;
the call site never needs it.

`Ui` lives in the `Rask` namespace, so `using Rask;` is all it takes — and a bare `Ui` means this class from
inside any `Rask.*` namespace as well, which a `Rask.Ui` namespace would have shadowed.

Each component lives in a file named after it, so the file list in `src/Rask.Ui` is the component list.

## See also

- [Dashboard](dashboard.md) — the operator console this kit was extracted from
- [Tailwind](tailwind.md) — how the compiler is wired into a Rask build
- [Building components](building-components.md) — the chain the kit is composed with
