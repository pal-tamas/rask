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
[Toggle](https://rask.sh/ui/toggle/), the kit's own [Confirm](https://rask.sh/ui/confirm/),
[Confirm leave](https://rask.sh/ui/confirm-leave/), [Filters](https://rask.sh/ui/filters/),
[Form buttons](https://rask.sh/ui/form-buttons/), [Form tabs](https://rask.sh/ui/form-tabs/), [Row actions](https://rask.sh/ui/row-actions/),
[Editor dialog](https://rask.sh/ui/editor-dialog/) and [Day and time](https://rask.sh/ui/day-and-time/), with
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
- **The hand over what can be pressed.** Every pressable kit part shows `cursor: pointer` — buttons, menu rows,
  options, tabs, calendar days, a pager's pages, a checkbox, radio or switch and the label that toggles it — and
  the arrow while it cannot be pressed: disabled (`disabled`, `aria-disabled="true"`, `data-disabled`), read-only,
  inside an `inert` subtree, or busy (a button waiting on its handler, a form's submit while it is sent). Flux keeps
  the arrow; the kit departs from it on purpose. It is one zero-specificity rule in the kit's sheet (`@layer rask`),
  keyed on what an element *is* — `button`, `[role=button]` and the other pressable roles, `summary`, `select`,
  `label[for]`, a label round a box or a radio, and the clickable `input` types — so your own `<button>`s in a page
  that loads the kit get it too. A `Div.OnClick(...)` does not: add `cursor-pointer` to it yourself. To opt an
  element out, give it a cursor utility (`cursor-default`, `cursor-text`): utilities sit above `rask` and win.
- **Its own words are translatable.** The few texts the kit writes itself — a pager's "Showing 1 to 10 of 13
  results", a select's "No results found", a date picker's "Select a date", the name of a close button — are
  `RaskString` keys. The kit speaks English and Hungarian out of the box, in the languages your app lists in
  `SupportedCultures`; a `Resources/RaskStrings.{culture}.json` in your app adds a language or changes a word
  ([every key and its English](localization.md#translating-the-frameworks-own-text)).
- **Simple first, composable after.** `Ui.Input.Bind(() => m.Email).Label("Email").Description(…)` is one line; `Ui.Navlist` with
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

The kit also compiles a sheet of its own (`UiStylesheet.Css`, served at `UiStylesheet.Href()`). **That
sheet is for a surface with no Tailwind build of its own** — `Rask.DevTools` inlines it into a panel
drawn inside somebody else's page; an app that draws only with `Ui.*` components and writes no
utilities can link it and run no Tailwind at all.

`RaskApp` and the WASM host link it by themselves whenever the kit is on and the app's own sheet does not
import it, and the sheet is always there to answer: a Rask.Server host serves `/css/rask-ui.css` out of the
kit's assembly (compressed, cached for a year under its `?v=`), and a browser app's build adds it to the
app's static web assets from the package, so `dotnet publish` ships it. Nothing to switch on, nothing written
into your tree. `<RaskUiWriteStylesheet>true</RaskUiWriteStylesheet>` still copies the file into
`wwwroot/css/rask-ui.css` for a host that serves only what is on disk.

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

The set is generated by `flux icons` from the latest `heroicons` npm release (MIT), which the
daily upstream run follows (`scripts/upstream/followers.json`), and compiled into
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
arrows select as they go, Home and End go to the first and the last tab, and a tab is never a link. A `Ui.Tabs` on
its own above a list that keeps its page in the address (`Ui.Table.PageIn("page", …)`) switches the list: a tab pressed
takes the page key out of the address the tab's `OnChange` (or bound value) moved to, so the new tab starts at page one,
its filters and sort kept — the page writes no `Page: null` of its own. A tab of a `Ui.TabGroup`, or in an editor
dialog's form, leaves the list's page alone. Every example, the
keyboard, Flux's attributes as steps and the
reference: **[rask.sh/ui/tabs](https://rask.sh/ui/tabs/)**.

### A long form's tabs

`Ui.FormTabs` is the kit's own (owner-approved, not Flux's): a long form split into Flux's tabs, each tab saying in a
red `Ui.Badge` how many of the fields it holds are invalid, and every refused save opening the first tab that holds
one — and, once that tab is shown, focusing its first invalid field (the runtime's, on every form; see
[accessibility](accessibility.md#a-refused-submit-takes-the-reader-to-the-first-mistake)). It stands inside the `Form` and reads that form's messages itself, so the page hands it nothing — no
`EditContext`, no refusal counter, no `OnInvalidSubmit`. A tab names what it holds with the member expressions a control
binds; a member that is an object or a collection holds every field it reaches, so an editor's rows are held by their
list. A field no tab holds is counted by none, and a tab that holds nothing (a document, a history) never shows a
count. The row scrolls sideways on a phone; which tab is open is the browser's, as a `Ui.Tabs`' is, and every panel
stays in the form. A correction counts once it reaches the form — with the next action, or as it is typed in a
`.Live()` field. A screen reader hears the count as "Fields to correct: 2" (`FormTabsInvalid` in the `RaskStrings`
catalog; „Javítandó mezők: 2” in Hungarian).

```csharp
// Before (Eszaf): a FormTabs of its own, handed the form's context, field names as strings and a refusal counter.
Form.Model(model).Context(form).OnSubmit(Save).OnInvalidSubmit(() => { _refused++; })[
    FormTabs.Form(form).Refused(_refused).Tabs([
        new("trip", "Törzs", [nameof(model.Number), nameof(model.VehicleId), nameof(model.DriverId), …], Trip(model)),
        new("transactions", "Tranzakciók", [], Transactions(model)),
    ]),
    FormButtons.For(…)]

// After: the kit's. No EditContext, no counter, no OnInvalidSubmit.
Form.Model(model).OnSubmit(Save)[
    Ui.FormTabs[
        Ui.FormTab.Name("trip").Label("Törzs").Holds(() => model.Number, () => model.VehicleId, () => model.DriverId, …)[Trip(model)],
        Ui.FormTab.Name("transactions").Label("Tranzakciók")[Transactions(model)]
    ],
    Ui.FormButtons.Back(…)]
// A tab shown only for a saved record:  Id is null ? null : Ui.FormTab.Name("document").Label("Dokumentum")[Document()]
// An editor's rows:                    .Holds(() => model.Pistols) — every field of every pistol
```

Live: **[rask.sh/ui/form-tabs](https://rask.sh/ui/form-tabs/)**.

## Tables

`Ui.Table` is [Flux's table](https://fluxui.dev/components/table), part for part: `Ui.TableColumns` of
`Ui.TableColumn`s and `Ui.TableRows` of `Ui.TableRow`s of `Ui.TableCell`s. **It sorts nothing and pages nothing**: the
page keeps the sorted column, its direction and the page number (`Sortable()`, `Sorted`, `Direction`, `OnSort`, and
`Paginate(Ui.Pagination…)` under the rows) — or, on a list page, keeps them in the address with
[`SortIn` and `PageIn`](#a-list-page). A slow page change keeps the rows and dims them with nothing to write;
`w-full` on the table and a width on every column but one keep the columns still from page to page
(`ContainerClass("min-w-0")` inside a grid or a flex row). A cell's and a heading's look is written once, in the kit's
sheet, so a cell writes only what differs and a list of thousands of rows stays small; your class on a cell wins as it
is (`ps-10`, `text-xs`). The heading the rows are sorted by says so to a screen reader (`aria-sort`), and a sticky
column casts its shadow once columns scroll under it, from marks the runtime writes on the scroll area
([accessibility.md](accessibility.md#tables-and-charts)). Every example, the dimming marks, the steady-columns recipe,
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

### Columns from the row's members

`Ui.TableColumn.For<TRow>(r => r.X)` is a heading bound to a member of the row type, and `Ui.TableCell.For(() => row.X)`
a cell bound to the same member of one row. They are Flux's heading and cell with the member read for them — not
Flux's, an owner-approved addition (2026-11-24):

```csharp
Ui.Table.Of(Listed)[
    Ui.TableColumns[
        Ui.TableColumn.For<SupplierRead>(s => s.Name).Class(ListTable.Wide),     // "Megnevezés" — its [Display(Name)]
        Ui.TableColumn.For<SupplierRead>(s => s.VatNumber).Sortable().Sorted(bySort).OnSort(Sort),
        Ui.TableColumn.End[RowActions.Heading]                                   // a hand-written heading, as before
    ],
    Ui.TableRows[(Listed.Data?.Rows ?? []).Select(supplier => Ui.TableRow.Key(supplier.Id)[
        Ui.TableCell.For(() => supplier.Name).Variant(Ui.TableCellVariant.Strong),
        Ui.TableCell.For(() => supplier.VatNumber),
        Ui.TableCell.End.Class("py-0")[RowActions.For(…)]                       // a hand-written cell
    ])]
]
```

- **The heading** says the member's `[Display(Name)]`, else its name; words in its brackets win.
- **The cell** writes the value in the reader's culture: the member's `[DisplayFormat(DataFormatString)]` first, else by
  what it holds — a number with its value object's scale (`Format.Number`), a day (`Format.Day`), a moment on the wall
  clock (`Format.Moment`), an enum's `[Display(Name)]`, a `bool` as Yes or No (`TableCellYes`/`TableCellNo`), a value
  object as its value. No value is its `[DisplayFormat(NullDisplayText)]`, else "–". Content in its brackets wins.
- **Alignment**: a number or a whole number keeps to the end in the heading and the cells, as Flux aligns an amount;
  `.Align(…)` wins.
- **`[ScaffoldColumn(false)]`** draws neither the heading nor any cell of the member, so they never go out of line.
- **Sorting** stays the heading's own steps — `.Sortable().Sorted(…).Direction(…).OnSort(…)`.

A generated read face (`SupplierRead`) carries the aggregate property's `[Display]`, `[DisplayFormat]` and
`[ScaffoldColumn]`, and registers each value object's shape (its kind and scale) at compile time, so nothing is
reflected over; a row type of your own is read off its attributes.

### A list page

**`SortIn`, `PageIn` and `Empty` are the list page every app writes** (not Flux's: an addition the owner approved,
2026-11-04). `SortIn("sort", "dir")` keeps the sorted column (by its heading's `Key`) and its direction in the address:
a press on a heading writes them — another column ascending, the sorted one turned round, as Flux's — and takes the
list to its first page; `SortIn("dir")` keeps the direction alone, for a list sorted by one column. `PageIn("page",
total)` draws a pager of links under the rows — always, as Flux does, "Showing 1 to 3 of 3 results" on one page (`UiPaginator.DefaultPerPage`, 15, unless a third
argument says otherwise), and steps back to the last page there is when the address names one past it — which is what
deleting the last row of the last page leaves. The column the list sorts by unless asked, ascending, and the first page
are each named by leaving their key out. `Empty(nothingYet, noMatch)` is what the one row of an empty list says:
`noMatch` while the list is narrowed — the address holds a key that narrows it (a filter, a tab, a search), or a
[`Ui.Filters`](#filters) before the table has a filter in force — and `nothingYet` otherwise. Every key of the address
narrows it except the table's own sort, direction and page; the key of a `Ui.EditorDialog` on the page (an open
`?new=new` or `?edit=7` sits over the list); Rask's own `rask-*` keys; and the order a page keeps there with its own
`OnSort` — a key whose value is a sortable heading's `Key` (`?sort=code`) or a direction (`asc`, `desc`). The page reads the
same keys as its own query parameters and reads that page of its rows; nothing else is wired.

```csharp
// Before: the page wrote every heading's sort, the pager's links, the step back and the empty sentence itself.
[QueryParam("sort")] public string? Sort { get; set; }
[QueryParam("dir")] public string? Dir { get; set; }
[QueryParam("page")] public int? Page { get; set; }

Listed.Data is { Total: 0 }
    ? EmptyList.Of("No suppliers yet.", "No supplier matches.", filtered: Name is not null)
    : Ui.Table.Of(Listed).Paginate(Listed.Data?.Total > 15 ? Pager() : null)[
        Ui.TableColumns[Heading("Name", null), Heading("VAT number", "vat")],
        …
    ]
// …plus Heading(…) with its Sorted/Direction/OnSort, Pager() with its Href, and in Remove():
// if (Listed.Data?.Rows.Count == 1 && Page > 1) Go.To(Address(Page - 1)).Replacing();

// After: the same keys, read the same way; the table writes them.
Ui.Table.Of(Listed)
    .SortIn("sort", "dir")
    .PageIn("page", Listed.Data?.Total)
    .Empty("No suppliers yet.", "No supplier matches.")[
    Ui.TableColumns[
        Ui.TableColumn.Key("name").Sortable()["Name"],
        Ui.TableColumn.Key("vat").Sortable()["VAT number"]
    ],
    Ui.TableRows[(Listed.Data?.Rows ?? []).Select(s => Ui.TableRow.Key(s.Id)[Ui.TableCell[s.Name], Ui.TableCell[s.Vat]])]
]

// and the read: Dir == UiTable.Descending, Page ?? 1, UiPaginator.DefaultPerPage
```

A heading the page gives its own `OnSort` keeps it. A table that is not drawn from a query takes `Empty` too: its one
row shows when it has no rows.

A table is not windowed as a [long select](#select) is: `PageIn` already bounds what is on the page to one page of rows
(15 unless said), read from the database a page at a time, and a page's rows are the page's own markup, of any height.
For one long scroll of fixed-height rows, draw them with [`Virtualize.Items`](composition-lists.md#virtualize--windowed-lists).

### Column widths, row actions and a form's buttons

**Three pieces every list app copied, now the kit's** (not Flux's: additions the owner approved, 2026-11-04, #1298).

`Ui.TableColumn.Width(Ui.ColumnWidth.…)` — or the step itself, `Ui.TableColumn.Day` — says what a column holds, and
that alone sizes it: `Day` and `Number` 7rem, `Short` (a short code: a plate, a VAT number) 10, `Code` (an invoice
number, a plate, never spaced out) 8, `Moment` and
`Reference` (a town, a supplier) 10, `Name` 18, `Flag` 5, `Actions` (two icon buttons — a pencil and a bin) 6, `Buttons`
(three or four, or a worded one) 12, `RowMenu` (the row's ⋯) 4, and the `Rest` (or a heading with
no width) takes what the others leave. A table whose headings name widths fills its box and is never narrower than
they take together (40rem at least) — each counted as wide as it really is, so a heading the page sized itself
(`.Class("w-32")`) counts its 8rem, not a rest's 12 — so a phone scrolls it inside its box (`min-w-0` on the box is written for you);
a long label wraps, numbers and the row actions keep to the end, and on paper the widths give way. A `RowMenu`,
`Actions` or `Buttons` heading with no words says "Actions" to a screen reader. `Ui.TableCell.Cut(text)` puts a value on one line, cut short
with an ellipsis, the whole of it in the cell's tooltip. `.Whole()` keeps a cell on one line on
paper too, where every other cell wraps — a day a wrap would split: `Ui.TableCell.Cut(day).Whole()`, Eszaf's
`ListTable.Whole(day)`. `Ui.TableColumn.Actions` heads a pencil and a bin the page
draws itself, `Ui.Button.Ghost.Sm.Icon(…)` twice in a `Ui.TableCell.End.Class("py-0")`: 6rem is what two of Flux's
small icon buttons and the cell's padding need, measured, not guessed.

`Ui.RowActions.Edit(url).Remove(delete, heading, question)` is a row's last cell: its actions behind one ⋯ (below).
The delete asks in the layout's [`Ui.Confirm`](https://rask.sh/ui/confirm/) and runs only on yes; a null delete draws
none. `Ui.FormButtons.Back(listUrl).Remove(delete, heading, question)` is a form's footer as Flux draws
one: Delete at the start (asking first), then Cancel — a link back to the list, so `.ConfirmLeave()` asks before it
drops what was typed — and Save, the form's submit; `.Save("Create")` and `.Cancel("Back")` name them. Cancel returns to
the list **as the reader left it** — its filters, sort and page, never a `Ui.EditorDialog`'s key — once the session has
shown it (in a `Ui.AppFrame`, or by a `Ui.Table` keeping its sort or page in the address), as `Ui.AppFrame`'s trail does;
`Back(Routes.SuppliersPage())` names only the list. In a dialog the app draws itself over the list, whose key the kit
does not know, Cancel is the list as given: a way back never leads to where the reader stands. Their words come from the `RaskStrings` catalog (`FormButtons*`,
`RowActions*`, `RemoveQuestion`): Hungarian reads Törlés, Mégse, Mentés, Módosítás and "Biztos törölni szeretné?".

**The app's own way back to a list is `.AsLeft()`** on the list's route (`UiListRoutes`): Rask notes every address a
session moves to, so `Routes.SuppliersPage().AsLeft()` is the list as this reader last saw it — its filters, sort, page
and search, an editor dialog's key left out — and the route as given when the session has not shown it. A save on a
page of its own, a "Cancel" the page draws itself, and an old form address sending the reader on write it, with no
list-address service of the app's own:

```csharp
// Before: an app service noting every address, injected into every form.
Ui.Button.Ghost.Href(_lists.Back(Routes.LandParcelsPage()))["Mégse"]
_lists.Back(Routes.SuppliersPage()).Go().Replacing();

// After: Rask remembers it.
Ui.Button.Ghost.Href(Routes.LandParcelsPage().AsLeft())["Mégse"]
Routes.SuppliersPage().AsLeft().Go().Replacing();
Go.To(Routes.DriversPage().AsLeft().With("edit", id)).Replacing();   // an old form address, on to the dialog
```

`Ui.FormButtons`' Cancel, `Ui.AppFrame`'s trail and a closing `Ui.EditorDialog` go there by themselves.

```csharp
// Before: each app kept a FormButtons, a RowActions and a ListTable of its own.
ListTable.Of([ColumnWidth.Rest, ColumnWidth.Day, ColumnWidth.Actions])[
    Ui.TableColumns[ListTable.Column(ColumnWidth.Rest, "Supplier"), ListTable.Column(ColumnWidth.Day, "Since"), ListTable.Actions()],
    Ui.TableRows[rows.Select(s => Ui.TableRow.Key(s.Id)[
        ListTable.Strong(s.Name), Ui.TableCell[Format.Day(s.Since)],
        Ui.TableCell.End[RowActions.For(Routes.EditSupplierPage(s.Id), () => Remove(s), "Szállító törlése", "Biztos törli?")]])]
]
FormButtons.For(Routes.SuppliersPage(), Remove, "Szállító törlése", "Biztos törli?")

// After: the kit's.
Ui.Table[
    Ui.TableColumns[
        Ui.TableColumn.Rest["Supplier"],
        Ui.TableColumn.Day["Since"],
        Ui.TableColumn.RowMenu
    ],
    Ui.TableRows[rows.Select(s => Ui.TableRow.Key(s.Id)[
        Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Cut(s.Name), Ui.TableCell[Format.Day(s.Since)],
        Ui.RowActions.Edit(Routes.EditSupplierPage(s.Id)).Remove(() => Remove(s), "Szállító törlése", "Biztos törli?")])]
]
Ui.FormButtons.Back(Routes.SuppliersPage()).Remove(Remove, "Szállító törlése", "Biztos törli?")
Ui.FormButtons.Back(Routes.SuppliersPage()).Save("Létrehozás")            // a new record
```

`Ui.DialogButtons.OnSave(Book)` is a dialog's footer as Flux draws a modal's: Cancel, a ghost button inside
`Ui.ModalClose`, so it closes the `Ui.Modal` it stands in (by the browser's own `command="close"`, `OnClose` runs, no
round trip) — then the primary action, a click handler, not a submit, so a form around the dialog is not sent. The
action is busy (`data-loading` and its spinner) while `OnSave` runs, as a form's Save is while it is sent; what you put
inside it stands before Cancel. `.Save("Book")` and `.Cancel("Back")` name them, and unnamed they read the catalog's
`FormButtonsSave` / `FormButtonsCancel` (Mentés, Mégse). `Ui.FormButtons` stays the form's footer.

```csharp
// Before: Ui.FormButtons' Cancel needs a route and its Save is a submit, so a dialog wrote its own row.
Div.Class("flex gap-2")[
    Ui.Spacer,
    Ui.ModalClose[Ui.Button.Ghost["Mégse"]],
    Ui.Button.Primary.OnClick(Book)["Foglalás"]
]

// After: the kit's.
Ui.DialogButtons.OnSave(Book).Save("Foglalás")
```

A dialog that IS a form takes `.Submit()` in place of `.OnSave(…)`: the action is then the form's `type="submit"`,
exactly as `Ui.FormButtons`' Save is — the form's validation runs, its `OnSubmit` runs only when every field passes, the
action is busy while it is sent, and Enter in a field submits. Cancel still closes the modal without sending anything.
The form's handler is the action, so `.Submit()` beside `.OnSave(…)` throws when the row is drawn, naming both.

```csharp
// Before: a validated form in a dialog could not use Ui.DialogButtons (its action is a click), so it kept its own row.
Form.Model(m).OnSubmit(Merge)[
    …fields…,
    Div.Class("flex gap-2")[Ui.Spacer, Ui.ModalClose[Ui.Button.Ghost["Mégse"]], Ui.Button.Primary.Submit["Egyesítés"]]
]

// After:
Form.Model(m).OnSubmit(Merge)[
    …fields…,
    Ui.DialogButtons.Submit().Save("Egyesítés")
]
```

`.Danger()` draws the action as Flux's danger button, as Flux's "Delete project?" modal does — with `.OnSave(…)` or
`.Submit()`: `Ui.DialogButtons.Danger().OnSave(Delete).Save("Delete project")`.

`Ui.Modal.Heading("…").Text("…")` writes the block every Flux modal example opens with — a large heading and the
sentence two steps under it, six steps above what follows — so a modal no longer hand-builds it. The heading is an
`h2`, clear of the close button in the corner (`pe-8`; none with `Closable(false)`), and names the dialog
(`aria-labelledby`). Not Flux's: an addition the owner approved (2026-11-18, #1367).

```csharp
// Before: every modal wrote its own heading block.
Ui.Modal.Name("book")[
    Div.Class("space-y-6")[
        Div[Ui.Heading.Level(2).Lg.Class("pe-8")["Book 12 pallets?"], Ui.Text.Class("mt-2")["They move in today."]],
        Ui.DialogButtons.OnSave(Book).Save("Book")
    ]
]

// After: the modal's own steps.
Ui.Modal.Name("book").Heading("Book 12 pallets?").Text("They move in today.")[
    Ui.DialogButtons.OnSave(Book).Save("Book")
]
```

`Ui.RowActions` puts all of a row's actions behind one ⋯, as Flux's table demo does: a ghost, small ⋯ (its
tooltip "Actions") opening a dropdown at the bottom end, holding `View(url)` (an eye), `Edit(url)` — or `OnEdit(…)` to
edit the row where it stands — what you put inside it, then the delete last, after a line, in red, asking first in
`Ui.Confirm` (its words are the heading's). None is required: a row that can only be deleted, or only chosen, says only
that, and a row nothing can be done with has no ⋯. The cell is Flux's sticky cell on `Ui.Main`'s surface (white,
zinc-800 in the dark; your own `bg-*` on it wins), so it stays at the table's right edge while the other columns
scroll under it on a phone; its column is `Ui.TableColumn.RowMenu` (4rem, sticky too, "Actions" to a screen reader). Each
row's dropdown, button and items are keyed by the component itself, so nothing in it needs a `"row-menu-" + id` key
of yours, and the `Ui.RowActions` itself needs none either: written inside a keyed row, an unkeyed component is that
row's (#1309), so a delete pressed just as a row above it went still deletes its own row.

```csharp
// Before: each app kept a "⋯" helper, keying every dropdown and item by the row.
Ui.TableColumn.Key("actions").End.Class(ListTable.Actions + " " + RowActions.AtTheEdge)[RowActions.Heading]
Ui.TableCell.Key("actions").End.Class(RowActions.AtTheEdge + " py-0")[
    RowActions.For(driver.Id, Editing(driver.Id), () => Remove(driver.Id), "Sofőr törlése", "Biztos törölni szeretné a sofőrt?")]

// After: the kit's.
Ui.TableColumn.RowMenu
Ui.RowActions.View(Routes.DriverPage(driver.Id)).Edit(Editing(driver.Id))
    .Remove(() => Remove(driver.Id), "Sofőr törlése", "Biztos törölni szeretné a sofőrt?")
Ui.RowActions[Ui.MenuItem.Icon(Ui.IconName.Check).OnClick(() => Choose(tenant))["Kiválasztás"]]
Ui.RowActions.OnEdit(() => EditInPlace(row))                                 // edited where it stands
```

Live: **[rask.sh/ui/row-actions](https://rask.sh/ui/row-actions/)**, **[rask.sh/ui/form-buttons](https://rask.sh/ui/form-buttons/)**
and the column widths on **[rask.sh/ui/table](https://rask.sh/ui/table/)**.

### Empty state, fold-out rows and a page's header

**Three more every list app wrote by hand** (#1283). `Ui.Empty["No suppliers yet."]` is what a list says when it has
nothing to show. Flux has no empty state of its own; this is the one its account overview demo draws when a search
matches nothing — a `<div role="status">`, centred, 24px above and below, with no ink or size of its own — measured
there and held to it by `flux parity empty-state --prepare empty-state`. Put it in a row's
only cell, `Ui.TableCell.ColSpan(columns)[Ui.Empty[…]]`, where it reads in the cell's 14px muted ink, or in place of the table, where
the page gives the ink (`.Class("text-sm text-zinc-500 dark:text-zinc-300")` reads as the cell does). A table drawn
from a query writes that row itself (`Ui.Table.Of(query).Empty(nothingYet, noMatch)`).

`Ui.TableCell.ColSpan(n)` spans a cell across columns, as Flux's cell forwards `colspan`: a row folded out beneath
another, or the empty row.

`Ui.PageHeader.Heading("Suppliers")` is the row a page opens with (not Flux's: an addition the owner approved,
2026-10-20): the page's `<h1>` in the extra large size, `Subheading` 8px under it, `Breadcrumbs` above, and its children
— the actions — at the end of the row, 8px apart. The row wraps; on a phone the title takes the whole line and the actions
go 16px under it. It is always the page's one `<h1>`, and keeps no room around itself: `.Class("mb-6")` is the page's. Each value is measured on
Flux's demos (the settings demo's title, the account overview's heading row). `.Beside(component)` puts something on
the title's line, after the `<h1>` and centred with it, 8px on — a status badge, as Flux's account overview sets
"Pending" beside a name — and the line wraps, so on a phone the badge goes 2px under a title that fills it:
`Ui.PageHeader.Heading("Gázolaj visszaigénylés").Beside(Ui.Badge.Green["Beadva"])` (before: a hand-built
`Div.Class("flex items-center gap-2")[Ui.Heading.Level(1).Xl[…], Ui.Badge…]` in place of the header).

```csharp
// Before: an EmptyList, a RowFold's colspan and a PageHeadline of each app's own.
Div.Class("mb-6 flex flex-wrap items-center gap-y-3")[
    PageHeadline.Of("Kutak"), Ui.Spacer, Div.Class("flex flex-wrap gap-2")[Ui.Button.Primary["Új kút"]]
],
pumps is [] ? EmptyList.Of("Még nincs kút rögzítve.") : Ui.Table[…],
Ui.TableRow.Key(pump.Id + "-detail")[Ui.TableCell.Attributes(("colspan", "3"))[Details(pump)]]

// After: the kit's.
Ui.PageHeader.Heading("Kutak").Class("mb-6")[Ui.Button.Primary["Új kút"]],
pumps is [] ? Ui.Empty["Még nincs kút rögzítve."] : Ui.Table[…],
Ui.TableRow.Key(pump.Id + "-detail")[Ui.TableCell.ColSpan(3)[Details(pump)]]
```

Live: **[rask.sh/ui/empty](https://rask.sh/ui/empty/)**, **[rask.sh/ui/page-header](https://rask.sh/ui/page-header/)** and the
fold-out row on **[rask.sh/ui/table](https://rask.sh/ui/table/)**.

### The editor over its list

**A list's record is added or changed in a dialog over the list, at the list's own address** (not Flux's: an addition
the owner approved, 2026-11-04, #1295, made of `Ui.Modal`). `Ui.EditorDialog.In("edit")` is open while the address
holds `?edit=` — `UiEditorDialog.New` for a record not yet saved, its id otherwise — and holds its content only then.
The link that opens it is the list's own address with the key set, `route.With("edit", id)`, so the search, sort and
page stay, a reload reopens it and Back closes it. The cross, Escape, a press outside and Cancel take the key out
(`Go.Without("edit")`, in the current history entry's place) and the list stands as the reader left it; a save ends
with `Go.Without("edit")`, and the list, rendering again, reads its rows again. `.Flyout()` (it was `.Wide()`) opens a form with rows of its
own as a sheet from the right, as wide as the screen allows (84rem at most); one list can hold both, each with its own
key. Open it from a link — a row's Edit, a New button — never a row's double-click. An editor's old address keeps working
when its page sends the reader on: `Go.To(Routes.DriversPage().With("edit", id)).Replacing()` in its `OnMount`.

```csharp
// Before: each app kept an EditorDialog of its own, given the list's address to close back to.
[QueryParam("edit")] public string? Edit { get; set; }
Ui.Button.Primary.Href(EditorDialog.Editing(Here, EditorDialog.New))["New driver"]
EditorDialog.For(Edit, DriverEditing.Key(Edit ?? "closed").Editing(Edit ?? EditorDialog.New).Back(Here), Here, wide: true)
EditorDialog.Heading("Edit driver")   // inside the editor

// After: the dialog reads and clears its own key.
[QueryParam("edit")] public string? Edit { get; set; }
Ui.Button.Primary.Href(route.With("edit", UiEditorDialog.New))["New driver"]
Ui.RowActions.Edit(route.With("edit", driver.Id.ToString(CultureInfo.InvariantCulture)))
Ui.EditorDialog.In("edit").Heading(Edit == UiEditorDialog.New ? "New driver" : "Edit driver").Flyout()[
    DriverEditing.Key(Edit).Editing(Edit)
]

// With the aggregate's own operations: nothing of the editor's to write.
Ui.EditorDialog.In("edit").Heading("Driver")[
    Edit == UiEditorDialog.New
        ? Form.For(Driver.Register)[f => [Ui.Field.Bind(() => f.Name), Ui.Button.Primary.Submit["Save"]]]
        : Form.For(Driver.Change, DriverId.Parse(Edit!))[f => [Ui.Field.Bind(() => f.Name), Ui.Button.Primary.Submit["Save"]]]
].Delete(Driver.Delete)
```

A [form over an operation](forms.md#a-form-over-an-operation) inside it reports to the dialog: once it saves, the
dialog says "Saved" in a toast (`RaskString.EditorSaved`, "Mentve") and closes, and an id no row has shows "It is not
there any more." (`EditorNotFound`) instead of the form. `.Delete(Driver.Delete)` — generated for every `partial`
aggregate that can be deleted — adds a Delete button for a stored record whose roles admit the reader
(`Driver.MayDelete`; roles by `b.Operation(nameof(Delete), roles: …)`). Pressed, it first tries the delete
(`Driver.DryRunDelete`, its `ValidateDelete()`) and shows what that refuses in place of asking; otherwise it asks
in the page's `Ui.Confirm` ("Delete it? This cannot be undone."), deletes through `Driver.Delete(id, version)`, says
"Deleted" and closes.

Live: **[rask.sh/ui/editor-dialog](https://rask.sh/ui/editor-dialog/)**.

## A day and a time

**A moment a form asks for, as a day and a time of that day in one row** (#1297) — Flux's typed date picker and time
picker side by side, the way both TA and Eszaf asked for one (the one-button `Ui.DatetimePicker` was not wanted for
it). Not one of Flux's components; the owner's, 2026-11-04.

```csharp
// before: two model properties taking the moment apart, and a helper drawing the two pickers
public DateOnly? FromDay { get => Moments.Day(From); set => From = Moments.OnDay(From, value); }
public TimeOnly? FromTime { get => Moments.Time(From); set => From = Moments.AtTime(From, value); }
Moments.Pickers(() => _form.FromDay, () => _form.FromTime, rule: ChipRules.NotBefore, live: true)

// after: one DateTime? bound whole
Ui.DayAndTime.Bind(() => _form.From).Validate(ChipRules.NotBefore).Live()
```

- **One row, wrapping**: the day takes the room (`min-w-48 flex-1`), the time a third of it (`w-36`, `w-44` where the clock has AM/PM); where the row has
  no room for both, as on a phone, the time goes beneath the day. Nothing scrolls sideways.
- **The day** is `Ui.DatePicker…Typed()`: typed in the culture's own order and separators (hu `2026. 11. 06.`, en-US
  `11/06/2026`) or picked in the calendar, whose header chooses the month and the year. **The time** is
  `Ui.TimePicker` typed, 24-hour or 12-hour as the culture writes it. Both follow `Locale`, the current culture unless set.
- **No day is no moment**: the time is disabled until a day is given, and a day taken away takes its time with it. A
  first day is its first minute.
- **Seconds** — the rule: a new day keeps the moment's time to the tick. The time field writes hours and minutes, so a
  time left at the hour and the minute the value already shows keeps the value whole, seconds and all (a save never
  loses seconds nobody changed); any other time has none. A moment kept in UTC or local time keeps its `Kind`.
- **Required-ness is the member's**, as for `.Choices`: a `DateTime?` the model does not require has the day's × and
  empties to `null`; a required one has none and, emptied, says "The … field is required." at the save; a `DateTime`
  cannot be empty, and an emptied day says "This field is required." at once and keeps its moment.
- **Bounds**: `.Min(DateTime)` and `.Max(DateTime)` are the earliest and the latest moment it takes — the day picker's
  `Min`/`Max` days, and the time picker's `Min`/`Max` on the first and the last day only. A day or a time typed past
  them is refused with the pickers' own message; a day picked onto the first day with an earlier time takes the
  earliest moment (and so for the last). Both forms of `Bind` take them.
- **Messages** stand under the pair, in the member's name: a day or a time half typed ("Enter a valid date." /
  "Enter a valid time." — Hungarian from the kit's catalog), the model's attributes and `Validate`; the save is refused
  while one shows.

**A day and a time the model keeps apart** bind both (#1362, owner-approved 2026-11-18) — the day a `DateOnly?`, the
time a `TimeOnly?`, the same two pickers in the same row:

```csharp
// before: a helper drawing two pickers, the time's disabled state and both messages by hand
Moments.Pickers(() => _note.Day, () => _note.Time, rule: DeliveryRules.Day)

// after: each part bound to its own member, each saying its own member's message under itself
Ui.DayAndTime.Bind(() => _note.Day, () => _note.Time).Label("Delivered")
```

- **Messages per part**: the day's member's under the day (`#{id}-error`), the time's under the time
  (`#{id}-time-error`) — its attributes, a FluentValidation rule or a rule of the day on either member, a part half
  typed — so a save can be refused with two. The label and description stay the pair's.
- **The time waits for a day**: disabled until a day is given. **A day taken away empties the time** (both members
  `null`, written together) and disables it again; a day alone leaves the time `null` — no first minute is invented.
- The day's × is the day member's, as above: a `DateOnly?` the model does not require has one. `.Live()`, `.Locale`,
  `.Disabled` and `.ShowValidation(false)` apply to both parts; `AfterBind` hears the moment the two make (the day at
  its time, its first minute without one). `.Validate` and `.Value`/`.OnChange` are the one-member binding's.

`Ui.DatePicker.Bind(() => m.Day).Typed()` is the same typed day on its own (the input trigger with the selectable
header), and `Ui.DatePicker.Range.Bind(() => m.Days).Typed()` two fields side by side, the start and the end, named
from the catalog ("Kezdet" / "Vége" in Hungarian). Live: **[rask.sh/ui/day-and-time](https://rask.sh/ui/day-and-time/)**,
**[rask.sh/ui/date-picker](https://rask.sh/ui/date-picker/)**.

## Printing

The kit prints what a page says and leaves off what it does — a **departure from Flux**, which prints its screen.
Inside `@media print` only, so nothing changes on screen, and the same for [Rask.Pdf](pdf.md) and a reader's own
Ctrl+P:

- **Left off:** every `Ui.Button`, a badge's ×, `Ui.Pagination`, `Ui.Sidebar` with its toggle and backdrop,
  `Ui.Header`, toasts, tooltips, open dropdown and popover panels, and a modal's backdrop. `Ui.Main` drops its screen
  padding, border and inner scroll.
- **`Ui.Table` prints whole:** cells wrap (nothing truncated or held to one line — but a `Ui.TableCell.Whole()`, which
  keeps its line and widens its column), the scroll frame lets the table run on, the heading repeats at the top of
  every page, and no row is split across two.
- **`Ui.Accordion` prints open,** closed items included, with no chevron.

```csharp
Div.Class("print:hidden")[Ui.Callout["Only on screen."]]   // your own parts: Tailwind's print: variants
Ui.Button.Class("print:inline-flex!")["Kept on paper"]      // a kit control you want printed: an important utility
```

## Native feel

On a phone the kit's controls behave like an app's, and its content stays a web page's — a **departure from
Flux**, which leaves all of this to the browser:

- **Controls select no text.** A long press, a double tap or a drag across a row of buttons selects nothing on
  buttons, tabs, nav items, menu rows, triggers, checkboxes, radios, switches, sortable headings, calendar days,
  a toast's buttons, the brand and the profile. Table cells, inputs, editors, code, `Ui.Text`, `Ui.Heading`,
  badges, callouts, a toast's message and a modal's body stay selectable. Clickables still show the pointer.
- **A tap is a tap** (`touch-action: manipulation`): no 300 ms wait for a double-tap zoom; pinch zoom stays.
- **No long-press callout on a control or a picture that is chrome** (an avatar, an icon, the brand's logo), and
  those pictures do not drag (`draggable="false"`). A link keeps its preview and "open in new tab".
- **An overlay keeps its scroll:** a modal, a flyout, the command palette, a menu and a popover do not carry the
  scroll on into the page behind.
- **The notch:** with `viewport-fit=cover` in the viewport (a new app has it), the header, the sidebar, a flyout and
  the toasts keep clear of it. Without it nothing moves.
- **No field zooms the page on iOS:** every default-size field is 16px below `sm` and 14px from it, as Flux draws
  them; a `.Sm` field is Flux's 14px at every width.

All of it weighs nothing (`:where()`, under `utilities`), so a class of yours wins:

```csharp
Ui.Button.Class("select-text")["Copy my label"]   // selectable again
Ui.Button.Class("touch-auto")["Zoomable"]          // the browser's touch handling back
```

A new app's shell also writes `viewport-fit=cover`, a `theme-color` for each scheme, and in `Styles/app.css` smoothed
text and no bounce of an installed app at its ends — all yours to change.

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

### Not found and forbidden

**What an address no page answers and a page the reader may not open say** (owner-approved, not Flux): one sentence
in a Flux callout, inside the app's frame. They are the built-in 404 and 403 of an app on the kit — the not-found page
lands in the root layout by itself ([routing](routing.md#not-found-and-auth-gating)), and a page whose `[Authorize]`
refuses a signed-in reader shows `Ui.Forbidden` in its outlet ([authentication](authentication.md#a-page-the-user-may-not-open)).
An app draws them itself where it decides on its own:

```csharp
Ui.NotFound                                   // warning callout: „A kért oldal nem található.” + Vissza a kezdőlapra
Ui.NotFound.Home(Routes.DashboardPage())      // the button leads there instead of /
!Allowed ? Ui.Forbidden : Ui.Table[…]         // danger callout, role="alert": „Nincs jogosultsága az oldal megtekintéséhez.”
```

The words are the catalog's `PageNotFound`, `NotFoundBackHome` and `PageForbidden` ([localization](localization.md)).

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
is the reader's HTML, cut to the editor's own schema both ways: the first paint of a stored value and every change the
browser sends keep only the tags and attributes the bundled extensions write (no script, event handler or
`javascript:` link). An extension of the app's own registers its markup once, at startup —
`UiEditorSchema.Allow("img", "src", "alt")`, or with a rule for a value:
`UiEditorSchema.Allow("span", "data-mention", id => Guid.TryParse(id, out _))` — and it is kept too. A registration
cannot let script back in: a tag that runs, embeds or submits something, an `on…` handler, `style`, `srcdoc` and
`srcset` are refused, a URL attribute is still held to http, https, mailto, tel or a path (`src` also to an inline
PNG, GIF, JPEG or WebP), and an attribute the kit already rules keeps the kit's rule. Sanitize the value before you
render it as markup anywhere else. Every example, the items and their keys,
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
typed by intl-tel-input (the release Flux runs, followed daily) and bound as E.164 once it is a possible number, the text as
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
Ui.Button.Primary.Href(Routes.NewProductPage())["New product"]    // stays in the app
Ui.Link.Href(Routes.ProductsPage())["Back to the list"]                   // stays in the app
Ui.Button.Href("https://github.com/pal-tamas/rask")["GitHub"]              // leaves it
```

A string that happens to name one of your own pages is still a string: it reloads the whole app to get
there. Use the route. As in Flux, a new tab is the anchor's own attribute — `.Attributes(("target", "_blank"), ("rel", "noopener noreferrer"))` —
and the runtime never intercepts one, because the reader asked for another tab.

## Heading, text and link

Flux UI's [heading](https://fluxui.dev/components/heading) and [text](https://fluxui.dev/components/text), with
Flux's names, props and look — measured against its docs in light and dark by `flux parity`.

```csharp
Ui.Heading["User profile"]                                    // <div>, 14px, medium
Ui.Heading.Level(3).Lg["Orders"]                              // <h3>, 16px
Ui.Subheading["Make changes to your personal details."]       // 14px zinc-500, 8px under the heading
Ui.Text.Class("mt-2")["This information will be displayed publicly."]

Ui.Text.Variant(Ui.TextVariant.Strong)["Total"]               // or .Subtle, or .Color(Ui.Color.Blue)
Ui.Text["Visit our ", Ui.Link.Href(Routes.ProductsPage())["documentation"], " for more information."]
Ui.Link.Href("https://example.com").External()["The spec"]    // new tab, rel="noopener noreferrer"
Ui.Link.As(Ui.LinkAs.Button).OnClick(Save)["Create account →"] // a <button type="button"> drawn as a link
```

| Component | Props |
| --- | --- |
| `Ui.Heading` | `Size` — `Base` (14px), `Lg` (16px), `Xl` (24px), `Xxl` (36px, Flux's `2xl`); `Level` 1–4 as in Flux, a `<div>` without one; `Accent()` |
| `Ui.Subheading` | none — Flux's `flux:subheading`, which its modal page uses and documents no prop of: a `<div>` of supporting text; the heading beside it keeps the 8px between them |
| `Ui.Text` | `Size` — `Sm`, `Default`, `Lg`, `Xl`; `Variant` — `Default`, `Strong`, `Subtle`; `Color` — a `Ui.Color` (Tailwind's hues), which wins over the variant; `Inline()` for a `<span>` |
| `Ui.Link` | `Href` — a generated route navigates inside the app, a string is an ordinary link; `Variant` — `Default` (underlined), `Ghost` (underlined under the pointer), `Subtle`; `External()`; `As` — `A`, `Button`; `Accent(false)` to draw it in the page's ink |

Each value is also a step — `Ui.Heading.Xl`, `Ui.Text.Subtle`, `Ui.Link.Ghost` — except the three that are also HTML
tags: write `Variant(Ui.TextVariant.Strong)` and `As(Ui.LinkAs.Button)`, because `.Strong`, `.Button` and `.A` on a
component are the inherited tag entries. Each is one HTML element, so
`Id`, `Class`, `Style`, `Data`, `Aria` and the events are the element's own. A heading is zinc-800 (white in dark), text zinc-500 (white at 70%),
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
    Ui.Main[Outlet]
];
```

`Ui.Main` is the page's `<main>` landmark (`<main data-ui-main>`), where a screen reader's "skip to main" lands and
where Rask puts focus after a navigation. Flux writes a `<div>` there; the kit departs (#1373), and the two draw alike.
It is the page's one `<main>`: put the outlet straight in it, never a `Main` of your own inside.

A WebAssembly app adds `Ui.SidebarScript` to its `HeadAssets` beside `Ui.AppearanceScript`, so a remembered rail is
drawn narrow from the first paint. One sidebar a page: its controls find it by fixed ids. Inside a sidebar the list
is the sidebar's own (`Ui.SidebarNav`); anywhere else it is **`Ui.Navlist`**
([rask.sh/ui/navlist](https://rask.sh/ui/navlist/)) or, in a row, **`Ui.Navbar`**
([rask.sh/ui/navbar](https://rask.sh/ui/navbar/)). **`Ui.Brand`**, **`Ui.Breadcrumbs`**, **`Ui.Profile`**,
**`Ui.Separator`** and **`Ui.Heading`** have pages of their own too; **`Ui.Spacer`** is `flex: 1`, pushing what
follows it to the far end of a row or a column.

### An app's whole frame

**`Ui.AppFrame`** (not Flux's: the owner approved it, #1296) is the layout an app on the kit writes anyway, in one line:
Flux's sidebar layout with the menu in the sidebar, breadcrumbs written from that menu in the header, who is signed in
at the sidebar's foot with a sign-out that asks first, and the page in the main.

```csharp
[Authorize]
[Route("/")]
public sealed partial class Layout : Component
{
    protected override Component? Render() =>
        Ui.AppFrame.Brand("PPTA").Tenant(Current.PartnerName).User(Current.Name).SignOut(SignOut.Path)
            .Notices(Ui.WorkNotices)[
            Ui.SidebarNav[
                Ui.SidebarItem.Icon(Ui.IconName.Home).Href(Routes.HomePage())["Home"],
                Ui.SidebarItem.Icon(Ui.IconName.Fire).Href(Routes.PumpsPage())["Pumps"],
                Ui.SidebarGroup.Expandable(true).Icon(Ui.IconName.CircleStack).Heading("Records")[
                    Ui.SidebarItem.Icon(Ui.IconName.Identification).Href(Routes.DriversPage())["Drivers"]
                ]
            ],
            Outlet
        ];
}
```

- **Its grid is the `<body>`:** the app's `Shell` gives it `min-h-screen bg-white dark:bg-zinc-800`, as for any Flux
  layout, so the sidebar runs the page's full height.
- **Its children:** each `Ui.SidebarNav` is the menu (pinned between the brand and the profile while it scrolls);
  everything else — the `Outlet`, and with it the built-in 404 and 403 — is the main.
- **Lit by the address:** the row of the page shown, or of the list it is beneath (`/drivers/7` lights Drivers), and
  the group holding it is opened. A row's own `.Current(…)` wins; a string link (an old front end) is never lit.
  Home is the row every other row is beneath (`/`) — or, where `/` is not home (an administrator's tenant list),
  the page the frame names: `.Home(Routes.HomePage())`, where the brand and the trail's first step then lead.
- **The trail:** home › group › list › the page's `PageTitle`. The list's step leads back to it **as it was left** —
  its filters, sort and page, never a `Ui.EditorDialog`'s key — and a phone keeps the last two steps. A form's
  `Ui.FormButtons` Cancel goes back the same way.
- **Mounted once:** `Ui.Toast`, `Ui.ConfirmLeave` and `Ui.Confirm`, in the catalog's words; the sign-out row is
  `Ui.MenuItem.Href(url).SignOut()`. The unsaved-changes dialog is titled "Unsaved changes" (the catalog's
  `ConfirmLeaveHeading`; "Mentetlen változások" in Hungarian) over the form's question; `.LeaveHeading("…")` words it,
  and `.LeaveWords(stay: "Nem", leave: "Igen")` its two buttons (the catalog's `ConfirmLeaveStay` / `ConfirmLeaveLeave`
  otherwise: Stay and Leave, Maradok and Elhagyom).
- **The tenant line** has the id `ui-app-frame-tenant`, for a test or the app's stylesheet.
- **The look is shared:** the zinc ground and edges are the kit's; an app's own is its accent (`--color-accent` in its
  stylesheet, [Re-skinning](#re-skinning)) and its `Logo(url)`. It works the same on Server and WebAssembly; a
  WebAssembly app needs no `Ui.SidebarScript` for it, as its sidebar has no rail.

The live demo and its reference: **[rask.sh/ui/app-frame](https://rask.sh/ui/app-frame/)**.

### Background work: the bell and the list

**`Ui.WorkNotices`** (not Flux's — Flux has no notifications; the owner approved it, #1293) is the bell at the header's
end, `.Notices(Ui.WorkNotices)`: Flux's `bell` on a ghost button with a red count, opening a flyout of the signed-in
user's background work — each running job with Flux's progress bar and what it last said, each one lately done, warned,
failed or stopped with a badge and the words its handler said, a batch as one group. **`Ui.WorkList`** is the same list
in the page. Both take `.Lane(id)` for one lane's work; the bell takes `.Actions(…)`, a footer under its list.

```csharp
await Jobs.Enqueue(new ImportPrices(file.Id)).Noticed($"Import of {file.Name}");   // what it shows
Jobs.Report(kept, total);                                                           // its bar, from the handler
```

- **The user's own work, in their tenant** — and their tenant's work enqueued `.ByTenant()` that they may run themselves —
  read from the job rows every poll and moved live in between by `WorkReported`; see
  [Rask.Jobs](jobs.md#show-a-job-to-the-person-who-started-it).
- **How it ended, in colour.** `Jobs.Warn(…)` draws an amber *Warning* badge, `Jobs.Refuse(…)` a red *Failed* one, and
  the words the handler said (`Detail`) stand under the title in the badge's colour — grey for `Jobs.Note(…)` on a job
  that is done. The error a failure threw is never drawn.
- **A batch is one group.** The jobs of one `Jobs.Enqueue(jobs)` stand under one heading — when it was started and how
  many it holds (*18/11/2026 09:30 · 3 items*), the worst way one ended, one bar for all of them — and count once among
  the ten lately ended. When the last job of a batch the reader started ends, the bell raises **one toast** in the tone
  of its worst end (*3 items are done, some with a warning.*); a colleague's shared batch is never toasted, and the list
  in the page raises none.
- **A footer:** `.Actions(…)` stands under the list in the flyout — the link to the page of all the work.

Before — a bell of the app's own beside the kit's, because the kit's could not group a press, say amber, or link out:

```csharp
Ui.AppFrame.Notices(LoadNotices.For(partner))[ … ]       // the app's own bell, its own rows, its own toasts
```

After — the kit's bell, with the app's link under it:

```csharp
Ui.AppFrame.Notices(Ui.WorkNotices.Actions(Ui.Link.Href(Routes.LoadsPage().Url())["All loads"]))[ … ]
```
- **On either host.** On a Server page the rows are read in-process; in the browser half of a `wasm-hosted` app they are
  asked of the server, and the live watch opens there — the same rows, the same rules, nothing to wire.
- **The count** is what runs, and what ended since the flyout was last opened; the badge is hidden from a screen reader,
  which hears it in the bell's name (*Background work, 2 new*).
- **A job seen running that ends is announced** in a polite live region: *Import of prices.csv is done.* A failure says
  *Failed*, never why.
- **A job with no count** draws a bar without an end, and *Running…* until it says something.
- Words from the catalog, English and Hungarian (`WorkNotices`, `WorkDone`, `WorkWarned`, `WorkBatch`, … in
  [localization](localization.md)).

The live demo is in the app frame's: **[rask.sh/ui/app-frame](https://rask.sh/ui/app-frame/)** — the bell in its header, the list on its home page.

### Card

`Ui.Card` is [Flux UI's card](https://fluxui.dev/components/card), part for part: `Ui.CardHeader` (`Ui.CardHeading`,
`Ui.CardSubheading`, `Ui.CardActions`), `Ui.CardBody`, `Ui.CardFooter` and `Ui.CardBleed`, and it handles the spacing,
dividers and corners between them. `Body`, `Variant` and `Size` set how the parts are apart, the surface and the room.
Every example, what each step decides, headers outside a card, the bleed variables and the reference of its steps:
**[rask.sh/ui/card](https://rask.sh/ui/card/)**.

`Ui.Facts` is what a summary card says of a record: a description list (`<dl>`), each `Ui.Fact("Label", value)` a
`<dt>` over its `<dd>`, grouped in a `<div>` as HTML allows. Flux has no description list (the kit's own, owner-approved
2026-11-18, #1363), so it is drawn in Flux's typography: the label in its subtle small text, the value in its strong
text, as `Ui.Text.Strong` draws it. A value is any markup — words, a number, a badge — and none shows a dash. The facts lie
in as many columns of 12rem at least as the card has room for, one on a phone; `.Class("sm:grid-cols-3")` on
`Ui.Facts` wins, and `Ui.Fact(…).Class("col-span-full")` gives a long one the row.

```csharp
// Before: each app kept a ViewField of its own, in a grid it set up on the card.
Ui.Card.Class("grid gap-4 sm:grid-cols-3")[ViewField.Of("Company", _company), ViewField.Of("Driver card", card)]

// After: the kit's.
Ui.Card[Ui.Facts[Ui.Fact("Company", _company), Ui.Fact("Driver card", card)]]
```

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
throw keyboard focus off the control mid-press.

A **field** is marked the same way once the event that sent its value is unanswered — `data-loading` alone, with no
`aria-busy` — so your own CSS can say `data-loading:opacity-70` on an input that searches as it is typed. The kit
draws nothing for it: Flux's input draws no loading state of its own (its `icon:trailing="loading"` is an icon you
put there), so `Ui.Input` shows no spinner by itself; style one yourself with `in-data-loading:` if you want one.
A field or control that says `data-rask-loading-now` is marked the moment its event leaves, with no 200 ms delay: the
kit writes it on the search field of a select, pillbox and autocomplete that asks the page (the reader has waited out
the pause already, and Flux's search spins at once) and on a create row.

**The wait ends when the page is whole, not when the handler returns.** A handler's own intermediate state is
not painted while it runs, and the page it answers with waits 150 ms for the reads it started
([routing](routing.md#a-handlers-work-reaches-the-screen-once)); when those are slower, the button stays busy
until they are back. And `data-rask-loading="within"` on a container marks it — `data-loading` only — for a
press anywhere inside: that is how a table knows its pager is waiting.

## What is in it

Grouped by what each piece is for.

| | |
| --- | --- |
| **Actions** | `Ui.Button` `Ui.ButtonGroup` `Ui.Dropdown` `Ui.Menu` `Ui.MenuItem` `Ui.MenuSubmenu` `Ui.MenuSeparator` `Ui.MenuGroup` `Ui.MenuCheckbox` `Ui.MenuCheckboxGroup` `Ui.MenuRadio` `Ui.MenuRadioGroup` `Ui.Navmenu` `Ui.NavmenuItem` `Ui.NavmenuSeparator` `Ui.Context` `Ui.Command` `Ui.CommandInput` `Ui.CommandItems` `Ui.CommandItem` `Ui.Popover` `Ui.Modal` `Ui.ModalTrigger` `Ui.ModalClose` `Ui.Confirm` `Ui.ConfirmLeave` `Ui.Filters` `Ui.Filter` |
| **Data display** | `Ui.Accordion` `Ui.AccordionItem` `Ui.AccordionHeading` `Ui.AccordionContent` `Ui.Avatar` `Ui.AvatarGroup` `Ui.Badge` `Ui.BadgeClose` `Ui.Card` `Ui.CardHeader` `Ui.CardHeading` `Ui.CardSubheading` `Ui.CardActions` `Ui.CardBody` `Ui.CardFooter` `Ui.CardBleed` `Ui.Carousel` `Ui.CarouselSlide` `Ui.CarouselControls` `Ui.Flag` `Ui.Kanban` `Ui.KanbanColumn` `Ui.KanbanColumnHeader` `Ui.KanbanColumnCards` `Ui.KanbanColumnFooter` `Ui.KanbanCard` `Ui.Table` `Ui.TableColumns` `Ui.TableColumn` `Ui.TableRows` `Ui.TableRow` `Ui.TableCell` `Ui.Timeline` `Ui.TimelineItem` `Ui.TimelineIndicator` `Ui.TimelineContent` `Ui.TimelineBlock` `Ui.TimelineSubgrid` `Ui.Chart` `Ui.ChartSvg` `Ui.ChartViewport` `Ui.ChartLine` `Ui.ChartArea` `Ui.ChartPoint` `Ui.ChartBar` `Ui.ChartGroup` `Ui.ChartStack` `Ui.ChartPie` `Ui.ChartAxis` `Ui.ChartAxisTick` `Ui.ChartAxisGrid` `Ui.ChartAxisLine` `Ui.ChartAxisMark` `Ui.ChartZeroLine` `Ui.ChartCursor` `Ui.ChartTooltip` `Ui.ChartTooltipHeading` `Ui.ChartTooltipValue` `Ui.ChartTooltipIndicator` `Ui.ChartSummary` `Ui.ChartSummaryValue` `Ui.ChartLegend` `Ui.ChartLegendIndicator` |
| **Navigation** | `Ui.Navbar` `Ui.NavbarItem` `Ui.Navlist` `Ui.NavlistItem` `Ui.NavlistGroup` `Ui.Brand` `Ui.Profile` `Ui.Breadcrumbs` `Ui.BreadcrumbsItem` `Ui.Link` `Ui.Pagination` `Ui.TabGroup` `Ui.Tabs` `Ui.Tab` `Ui.TabPanel` |
| **Feedback** | `Ui.Callout` `Ui.CalloutHeading` `Ui.CalloutText` `Ui.CalloutLink` `Ui.Progress` `Ui.Skeleton` `Ui.SkeletonLine` `Ui.SkeletonGroup` `Ui.Toast` `Ui.ToastGroup` `Ui.Tooltip` `Ui.TooltipContent` |
| **Data input** | `Ui.Input` `Ui.Textarea` `Ui.Composer` `Ui.Select` `Ui.SelectOption` `Ui.SelectGroup` `Ui.SelectOptionCreate` `Ui.SelectOptionEmpty` `Ui.SelectButton` `Ui.SelectInput` `Ui.SelectSearch` `Ui.ColorPicker` `Ui.ColorPickerArea` `Ui.ColorPickerSlider` `Ui.ColorPickerInput` `Ui.ColorPickerSwatches` `Ui.ColorPickerSwatch` `Ui.ColorPickerDropper` `Ui.Autocomplete` `Ui.AutocompleteItem` `Ui.Pillbox` `Ui.PillboxOption` `Ui.PillboxOptionCreate` `Ui.PillboxOptionEmpty` `Ui.PillboxSearch` `Ui.PillboxTrigger` `Ui.PillboxInput` `Ui.FileUpload` `Ui.FileUploadDropzone` `Ui.FileItem` `Ui.FileItemRemove` `Ui.FilePicker` `Ui.Checkbox` `Ui.CheckboxGroup` `Ui.CheckboxAll` `Ui.CheckboxIndicator` `Ui.RadioGroup` `Ui.Radio` `Ui.RadioIndicator` `Ui.Switch` `Ui.Toggle` `Ui.Slider` `Ui.SliderTick` `Ui.Field` `Ui.Field.Bind` `Ui.Label` `Ui.Description` `Ui.Error` `Ui.Fieldset` `Ui.Legend` `Ui.Otp` `Ui.OtpInput` `Ui.OtpSeparator` `Ui.OtpGroup` `Ui.Phone` `Ui.Calendar` `Ui.DatePicker` `Ui.DatetimePicker` `Ui.DatetimePickerButton` `Ui.TimePicker` `Ui.DayAndTime` |
| **Layout** | `Ui.Separator` |
| **Layout** | `Ui.Header` `Ui.Main` `Ui.Sidebar` `Ui.SidebarHeader` `Ui.SidebarBrand` `Ui.SidebarCollapse` `Ui.SidebarSearch` `Ui.SidebarNav` `Ui.SidebarItem` `Ui.SidebarGroup` `Ui.SidebarSpacer` `Ui.SidebarProfile` `Ui.SidebarToggle` |
| **Support** | `Ui.Icon` / `Ui.IconName` / `Ui.IconVariant` (all of Heroicons: outline, solid, mini, micro), `Ui.AppearanceScript` (dark mode), `UiPaginator`, `Ui.Breakpoint`, `UiStylesheet` |

## Who owns the state

The kit ships no JavaScript (the [editor](#rich-text-editor)'s and the [phone](#phone)'s engines apart), and that constraint decides the shape of every interactive component. It
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
the very press or submit go. Opening, typing and closing send nothing. Every example, live, with its steps:
**[rask.sh/ui/confirm](https://rask.sh/ui/confirm/)**.

```csharp
Ui.Confirm                                                // "Confirm", "No" and "Yes"
Ui.Confirm.Heading("Megerősítés").No("Nem").Yes("Igen")   // your own words
Ui.Button.Danger.OnClick(Delete).Confirm("Biztosan törli a céget?", heading: "Cég törlése")   // a title of its own
Ui.MenuItem.Danger.OnClick(Delete).Confirm("Biztosan törli a céget?", heading: "Cég törlése")   // a menu item too
Ui.MenuItem.Href("/logout").Confirm("Sign out of Acme?")["Sign out"]   // a link: a yes follows it
Ui.MenuItem.Href(SignOut.Path).SignOut()   // "Are you sure you want to sign out?" under "Sign out", from the catalog
Ui.Confirm.Primary()                                      // Yes as the primary button: questions that lose nothing
```

A handler asks from code, after a check only it can make, with `await Ui.Confirm.Ask("…")` — true for Yes, false
for No, the close button, Escape and a press outside — in the same dialog, on both hosts:

```csharp
private async Task Save()
{
    if (await Periods.Overlapping(_period) && !await Ui.Confirm.Ask("The period overlaps another. Save it anyway?"))
    {
        return;
    }

    await Periods.Save(_period);
}

if (await Ui.Confirm.Heading("Km").No("Nem").Yes("Igen").Primary().Ask("Rögzíti a km-t?")) …   // this question's words
```

The words and `.Primary()` set on that chain are that question's alone; what it leaves unset is the layout's
dialog's. Focus moves into the dialog and back to the control that was pressed. A second question asked while one
is open waits its turn and opens once the first is answered. Work that is cancelled while it asks — its component
left the page, the session ended, a configured handler timeout ran out — gets false, unanswered. On a page with no
`Ui.Confirm` it throws, naming what to place (`Ui.AppFrame` places one).

A menu item's link asks before it is followed, and a yes follows it — an in-app route in the app, a string as the
browser follows any link: a GET and a page load, which is how a sign-out endpoint is reached. `.SignOut()` is that
question in the catalog's words (`SignOut`, `SignOutQuestion`; in Hungarian "Biztos ki szeretne jelentkezni?" under
"Kijelentkezés"), and names the row "Sign out" with Flux's `arrow-right-start-on-rectangle` where it has no words or
icon of its own. It signs no one out itself: the link does, or the `OnClick` it runs.

`Ui.ConfirmLeave` is where a form's [`ConfirmLeave("…")`](forms.md#ask-before-leaving-unsaved-changes) asks, in
place of `confirm` — the form's message as the heading. Every example, live, with its steps:
**[rask.sh/ui/confirm-leave](https://rask.sh/ui/confirm-leave/)**.

```csharp
Ui.ConfirmLeave                              // "Stay" and "Leave"
Ui.ConfirmLeave.Stay("Nem").Leave("Igen")    // your own words
Ui.ConfirmLeave.Heading("Unsaved changes")   // a short title; the form's question is the sentence beneath it
```

Both are rendered closed and the runtime opens them, so the question appears without a round trip. The close
button, Escape and a press outside mean no. A leave dialog's heading may be a whole question of the form's own,
so it carries `pe-8` to keep clear of the close button; `Heading("…")` gives it the shape `Ui.Confirm` always
has, Flux's: a short title, the question under it. Both are held to Flux's open confirmation by
`flux parity-modal`; the answer field, and a Yes that is no submit button, are the two departures.

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
none), so the table's rows turn into skeleton rows at the press, as for its own pager. A filter change (a pick, a
badge's cross, Clear all, Clear filters) takes that table back to its first page, the sort staying: a pager with
`Href` has the key its first two pages' addresses disagree on (`page`) taken out — or set as page one writes it — in
the same move of the address, and a pager with only `OnPage` is told `OnPage(1)`; no code at the call site. Each control is bound to a `[QueryParam]` member and the set
binds it to the address instead, so the router stays the only writer of the member and the page hears `OnUpdated`:
a pick moves the query (replacing the history entry), a text field once typing pauses for 150 ms, and a call site's
own `Debounce` wins. A member without `[QueryParam]` fails as the page renders, naming the declaration to write.
`filters.Badges` is a badge for each filter in force in its control's own words — "Driver: Ada Lovelace", a range
as its picker writes it or the preset that made it, a checkbox's label alone — each with a cross that takes that key
out, and Clear all from two on, which takes every filter's key (and the page) out in one move and leaves the sort alone.
`Ui.Filters.OnOpen(…)` runs as the panel opens. A filter no control can say — a select of several answers, until a
collection `[QueryParam]` exists — goes in `Ui.Filter.Label("Labels").Shown(…).OnRemove(…)[control]`. Handed the names it holds, `.Shown(names)` says one
by its name ("Vehicles: ABC-123") and two or more by their count ("Vehicles: 40 selected", `FiltersSelectedCount`)
with every name in a tooltip on the badge that wraps (hover, keyboard focus, a tap); no names is no filter. A badge
never widens the page: words longer than its row end in an ellipsis, the whole of them in its `title`, and its cross
stays in reach. A range is one
query value because `UiDateRange` is `IParsable`: `2026-10-01..2026-10-09`, `~last7Days` after it when a preset made
it, and text that is no range leaves the member unset.

A range of numbers is two keys, a least and a most: `Ui.Filter.Range(() => WeightFrom, () => WeightTo).Label("Weight")`
over two `[QueryParam]` members of a nullable number (`int?`, `long?`, `decimal?`, `double?` — any `INumber<T>`). The
panel draws the two ends side by side under the label, each a number field written to the address once typing pauses,
either left empty at will (`FilterRangeFrom`/`FilterRangeTo` say which while empty: "From"/"To", "-tól"/"-ig"). ONE
badge says what is set, its numbers in the reader's grouping: "Weight: at least 5,000", "at most 7,000" or
"5,000 – 7,000" (`FilterRangeAtLeast`/`FilterRangeAtMost`/`FilterRangeBetween`; Hungarian "legalább 5 000",
"legfeljebb 7 000"); a fraction keeps its digits ("20.5"). Its cross, Clear all and Clear filters take both keys out in
one move. With no `.Label(…)` it is called by the first end's `[Display]` name. Live:
**[rask.sh/ui/filters](https://rask.sh/ui/filters/)**.

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
nothing else. `Avatar` takes an image's address (Flux's `avatar`), or an avatar of your own for Flux's `avatar:*`
props — `.Avatar(Ui.Avatar.ColorAuto())` is `avatar:color="auto"`: what it states wins, and the option fills the
rest as Flux does (extra small, round, named by the option's words).

The browser owns opening and dismissal there — a press, Escape, a click elsewhere — and the runtime writes
`aria-expanded` and `data-open` in the task the list opens or shuts, so they never trail the list by a round
trip and the page hears no toggle.

## Fields: label, description, error

`Ui.Field`, `Ui.Label`, `Ui.Description`, `Ui.Error`, `Ui.Fieldset` and `Ui.Legend` are
[Flux UI's field](https://fluxui.dev/components/field), part for part: a field stacks a label, a control, help text
and the message the form holds for the control, and tells each part which control it belongs to. The label is a real
`<label for>`, and `Ui.Error` reads the form (bare inside a field, `For(() => m.Email)` anywhere, `Message("…")` for
your own words) from a live region that is always in the page. Every control's `Label` and `Description` draw the same
field around it. A field that must be filled (the answer `aria-required` comes from: `[Required]`, or a non-nullable
member of a generated model) says so beside its label with Flux's badge — *Required*, from the strings catalog
(`FieldRequired`, *Kötelező* in Hungarian), `aria-hidden` because `aria-required` already says it; a control's own
`.Badge(…)` wins and `.Badge("")` says nothing. Every example, the error's rules and the reference: **[rask.sh/ui/field](https://rask.sh/ui/field/)**.

The lengths are the member's too: `Ui.Input` and `Ui.Textarea` bound to a field whose value object declares them
(`Text(Value, MaxLength: 100, MinLength: 2)`, told through the model's [field shapes](forms-validation.md)) write
`maxlength` and `minlength` on the control, so the browser stops at the most and refuses the fewest on submit.
`.MaxLength(n)` (input) and `.MinLength(n)` (input and textarea) — the HTML attributes Flux forwards — win over them.

**The auto field.** `Ui.Field.Bind(() => m.Name)` draws the whole field from the member — Rask's own, approved by the
owner (2026-11-23), made of Flux's parts: the label, help text and placeholder are its `[Display(Name, Description,
Prompt)]`, the badge the automatic *Required*, and the control what the member's [field shape](forms-validation.md)
(else its type) says it holds. Text is a `Ui.Input` (`email`, `tel`, `url`, `password` from the member's attributes;
`.Multiline()` a `Ui.Textarea`); a number or whole number an input with its `min`, `max` and a step of 10^-scale; a day
a `Ui.DatePicker` (ending today for `NotAfterToday`); a moment a `Ui.DayAndTime` (ending now for a `Moment` value object's `NotAfterNow`); a `bool` a `Ui.Checkbox`; an enum a
`Ui.Select` over its options in their order, with an empty one for a nullable member; another row's id a `Ui.Select`
over the `.Choices(…)` you give (the shapes `Ui.Select.Choices` takes — without them it throws, naming the member); a
range two controls of its ends' kind under one label, `Km.Start` and `Km.End` each with its own message.
`[Editable(false)]` makes an input or textarea read-only and every other control disabled. The steps `Label`,
`Description`, `Placeholder` and `Badge` win over the attributes, `Disabled`, `ShowValidation` and `Live` reach every
control, and `Blur` and `Debounce` the text ones. The first step says which of the two the chain is.

To pick and tune the control yourself, as Flux does it — the binding and the tuning on the control, the field a frame
around it — put the bound control in `Ui.Field[…]` with no `Ui.Label`: the field labels it from the member, with the same
`[Display(Name)]`, the same *Required* badge the control's own label would carry (its `.Badge(…)` wins), and its
`[Display(Description)]` where the field holds no `Ui.Description`, wired by the field's ids (`label for`,
`aria-describedby`). The label goes where the control's own goes: before it, or after a checkbox in an inline field. A
placeholder is the control's own step. A field with a `Ui.Label` of its own, or with a control bound to nothing, is
drawn exactly as written. A bare bound control — no field, no `.Label(…)` — draws neither a label nor a message, as in
Flux: that is the shape for a table cell, whose column header names it, with its message beside it —
`Ui.TableCell[Ui.Input.Bind(() => row.Size), Ui.Error.For(() => row.Size)]`, which shows that row's messages, as
Flux's `flux:error name` does.

```csharp
Ui.Field.Bind(() => model.Name)                              // the field picks the control: [Display] label, description, placeholder
Ui.Field.Bind(() => model.Km)                                // a range: two controls, errors Km.Start / Km.End
Ui.Field.Bind(() => model.Vehicle).Choices(Vehicle.Find)     // a typed id
Ui.Field.Bind(() => model.Note).Multiline()                  // a textarea only when asked
Ui.Field[Ui.Input.Bind(() => model.Plate).InputClass("uppercase"), Ui.Error]   // you pick and tune it; the member labels it
Ui.Field[Ui.Label["Rendszám"], Ui.Input.Bind(() => model.Plate), Ui.Error]     // fully by hand
```

A control you give no `Id` takes one from the member it binds, else its label: `Ui.Input.Bind(() => m.Name)` is
`f-name`, its error `f-name-error`, its help text `f-name-description`. Ids are unique on the page: a second field that
would be `f-name` — another form's `Name`, a row of a list, a dialog opened later — is `f-name-2`, then `-3`, and its
label, error and description follow it. A field keeps its id for as long as it is on the page, so a live update never
re-ids the one being typed in; a new one takes the first id free. An `Id` you give is never changed. A field that
comes and goes between others of its type keeps its own id when you `Key` it, as any component keeps its state.

## Select

`Ui.Select` is [Flux UI's select](https://fluxui.dev/components/select), part for part, over Rask's binding:
`Bind` or `Value` where Flux says `wire:model`, and the options as children typed by the select's own value. The
default variant is the browser's `<select>`; `.Listbox` draws the list (icons, descriptions, avatars, content of your
own, a prefix, `Clearable()`), `Searchable()` filters it in the browser by text and `Keywords`, `.Listbox.Multiple()`
over a collection picks several, and `.Combobox` is an input that filters. `Filter(false)` hands the search to the
page, with `Debounce` on `Ui.SelectSearch` or `Ui.SelectInput` for one query a word, and `Ui.SelectOptionCreate`
offers to make what was typed. For a list too long to send whole, `Search` hands the select the question itself — it
asks for `""` when it mounts, again once typing pauses (150 ms, Livewire's `wire:model.live`), and again when a render
hands it another search (one over rows the page has read since), waits as Flux's backend search does from the moment
the question leaves (the rows stay, the field's clear button fades or a combobox spins, a create row greys and takes no
press, an empty list says "Loading..."), says "No results found" when there is none (both from the catalog, Hungarian
too), and keeps every picked option in the list while a search does not find it. Answer with the best twenty, as
Flux's backend search does (`.Take(20)`): the reader narrows a long list by typing, not by scrolling.

```csharp
Ui.Select.Bind(() => model.PartnerId).Placeholder("Choose a partner...").Search(async typed =>
    (await Partner.Where(p => p.Name.Contains(typed)).Take(20).ToList())
        .Select(p => Ui.SelectOption.Key(p.Id).Value(p.Id)[p.Name]))[
    // The partner the field holds, named before anything is found.
    Ui.SelectOption.Key(model.PartnerId).Value(model.PartnerId)[model.PartnerName]
]
```

**Rows of another table** — a company, a driver, a vehicle — are `.Choices(…)` rather than options (the owner's
addition, not Flux's). Hand it `Ui.Choice<T>` rows in any order and the select is the picker both apps used to write:
a listbox with a search field, the rows in the **reader's** alphabet (`TextOrder.Current` — in Hungarian `Á` stands
with `A`, `Cs` after `Cu`, `Dzs` after `Dz`), a row's detail as the muted line under its words that a search finds
too, and "Choose..." from the catalog ("Válasszon…" in Hungarian). Whether it can be cleared is the bound member's to
say: `int?` or `string?` that the model does not require gets the cross, `int` and a required member never do, and a
required member's message is the model's own rule's. For a list too long to send whole, hand it the search instead;
the row the value holds is named before anything is found and stays first while a search misses it. It is asked as
`Search` is — once typing pauses 150 ms — and answers with the best twenty too:

```csharp
Ui.Select.Bind(() => model.CompanyId).Choices(companies)        // Ui.Choice<int>[]: new(c.Id, c.Name, c.City)
Ui.Select.Bind(() => model.DriverId).Choices(Drivers, held: driver)

// The best twenty for what was typed.
static async Task<IReadOnlyList<Ui.Choice<int>>> Drivers(string typed) =>
    [.. (await Driver.Where(d => d.Name.Contains(typed)).Take(20).ToList()).Select(d => new Ui.Choice<int>(d.Id, d.Name))];
```

**A searched list the reader still scrolls to its end** — every product, not the first twenty — takes the search with
a `Ui.ChoicePage` as well (`.Choices((typed, page) => …)`, on `Ui.Select` and `Ui.Pillbox`, Server and WASM). The list
opens on the first page for what is typed; as the reader scrolls — the wheel, the scrollbar, End — to within ten rows of
the last one here, it asks for the next page and puts it after them, so the box stays where it is and grows as pages
arrive. An answer of fewer rows than `page.Take` is the end. Typing starts again from the first page, and a page asked
for older words is dropped when it lands. **A source that says how many rows it has** — the page answered as
`new Ui.ChoiceRows<T>(rows, total)`, which is still the `IReadOnlyList` the finder returns — makes the list that long
from its first page on (the owner's, 2026-11-20): the scrollbar is the whole list's, as a native list's is, and stays
still as pages arrive; the rows not here yet are skeleton rows, and a drag of the scrollbar far down is answered with one
question for every row up to there (at most a thousand at a time), not a page at a time behind the reader. A plain list
says nothing of the rest, and a box of skeleton rows past the last row found shows the next page on its way. The rows are drawn in the order the store gives them, page after page, so
order the query; the picked rows stay in the list as they do above. A page is fifty rows: five boxes of the ten a box
shows, asked for a box's worth of scrolling before the reader gets there. Flux draws no "loading more" row, and nor does
this. The one-list `.Choices(find, held:)` above is unchanged.

```csharp
// Before: the first twenty, and the list cannot be scrolled past them.
Ui.Pillbox.Bind(() => draft.ProductIds).Choices(typed => Products(typed), held: heldProducts)

static async Task<IReadOnlyList<Ui.Choice<int>>> Products(string typed) =>
    [.. (await Product.Where(p => p.Name.Contains(typed)).OrderBy(p => p.Name).Take(20).ToList())
        .Select(p => new Ui.Choice<int>(p.Id, p.Name))];

// After: a page at a time, as the list scrolls.
Ui.Pillbox.Bind(() => draft.ProductIds).Choices((typed, page) => Products(typed, page), held: heldProducts)

static async Task<IReadOnlyList<Ui.Choice<int>>> Products(string typed, Ui.ChoicePage page) =>
    [.. (await Product.Where(p => p.Name.Contains(typed)).OrderBy(p => p.Name).Skip(page.Skip).Take(page.Take).ToList())
        .Select(p => new Ui.Choice<int>(p.Id, p.Name))];

// Best: the scrollbar the whole list's from the first page — the page, and how many rows there are.
static async Task<IReadOnlyList<Ui.Choice<int>>> Products(string typed, Ui.ChoicePage page)
{
    var found = Product.Where(p => p.Name.Contains(typed));
    var rows = await found.OrderBy(p => p.Name).Skip(page.Skip).Take(page.Take).Select(p => new Ui.Choice<int>(p.Id, p.Name)).ToListAsync();
    return new Ui.ChoiceRows<int>(rows, await found.CountAsync());
}
```

**Rows pinned first** — "No company" above the companies — are `.Leading(…)` after `.Choices(…)`, on `Ui.Select` (a
select of `int?` takes `Ui.Choice<int>` rows, as `.Choices` does) and `Ui.Pillbox`, over any of the three sources (the
owner's addition, 2026-11-18). They stand in the order given, never sorted among the choices, and **every search keeps
them**, since a "none" row is what a reader looks for while typing a name: a list held whole never hides them for what
is typed, and a searched or paged source has them put before every answer's first rows, whatever was typed. They are
never asked of the source: a paged source's first page is asked for that many fewer rows (`page.Take` is 49 with one
leading row), so the list's first screen holds as many, and the pages after it skip the rows found, the leading ones not
counted. A picked row a search left out still comes first. Give them values no choice holds.

```csharp
// Before: the "none" row sorted in among the companies, and lost to a search.
Ui.Select.Bind(() => filter.CompanyId).Choices([new Ui.Choice<int>(0, "Nincs cég"), .. companies])

// After: first, whatever is typed.
Ui.Select.Bind(() => filter.CompanyId).Choices(companies).Leading(new Ui.Choice<int>(0, "Nincs cég"))
Ui.Select.Bind(() => filter.CompanyId).Choices((typed, page) => Company.Named(typed, page)).Leading(new Ui.Choice<int>(0, "Nincs cég"))
```

**A create row waits for the search, as Flux's does.** While a search's question is out, `Ui.SelectOptionCreate`
greys, says `aria-disabled`, is stepped over by the arrows and Enter and refuses a click that reaches it anyway, so
nothing is made of text the answer is about to change; pressed, it spins at once, refuses a second press and holds the list open until its handler is done.
And one departure the owner chose: while a search is out an empty list says "Loading..." (`WhenLoading`), where
Flux goes on saying "No results found" until the answer comes.

Everything it fills in is a default: `.Placeholder(…)`, `.Clearable(…)`, `.Searchable(false)` or `.Combobox` say
otherwise, `.AfterBind(…)` runs after a pick, and options written as children come after the rows.

**A long list draws only the rows in view** (not Flux's, which draws every option). Up to a hundred options a drawn
list — listbox, combobox, pillbox, `.Choices(…)`, the rows a `Search` answers — is drawn whole, as Flux draws it; past
a hundred it is windowed on [Core's virtualization](composition-lists.md#virtualize--windowed-lists): the rows its
scroll shows and ten either side, between two spacers that keep the box as tall as the whole list, so the reader
scrolls through all 1098 vehicles without typing. Every row of a windowed list is one height (the taller one for all
of them once any option has a description) and one line: a name or a description too long for it is cut short with an
ellipsis and said whole in the row's `title`, so no row grows past what the spacers count. There is nothing to write —
the same options, the same steps:

```csharp
Ui.Select.Bind(() => model.VehicleId).Listbox.Searchable()[
    vehicles.Select(v => Ui.SelectOption.Value(v.Id)[v.Plate])      // 1098 of them: about thirty are on the page
]
```

The search narrows the **whole** list (the select does it, by the browser's rules: case, accents and `Keywords`; on a
Server host once typing has paused 100 ms, unless `Ui.SelectSearch.Debounce(…)` says otherwise, and at every key in
WASM), the
arrow keys go on past the rows drawn and the list scrolls to the row they land on, `Home` and `End` go to either end,
type-ahead finds any row, the list opens on the picked row, and every row says `aria-setsize` and `aria-posinset` so
a screen reader counts all of them. A pick is the press on a row, as ever, and is sent at once: a windowed list holds
no pick in the browser for the next action. Every row is as tall as the next (32px, 60 with a description), and a list
with groups or children of the page's own between its options is drawn whole. The browser's own `<select>` (the
default variant) always holds every option: use `.Listbox` for a long one.

An option's `Key` names it inside its own select, so two selects on one page may key theirs by the same ids. An open list sends the page nothing until a row is picked — and a bound select
sends nothing at the pick either, one answer or several: the browser draws it (the words, the count, the ticked rows)
and the value travels with the next action, as Flux's `wire:model` (`.Live()` sends it at once). Every example, the
keyboard, searching on the server and the reference: **[rask.sh/ui/select](https://rask.sh/ui/select/)**.

## Autocomplete

`Ui.Autocomplete` is [Flux UI's autocomplete](https://fluxui.dev/components/autocomplete): an input over a list of
suggestions that holds the **text**. To show a name and store an id, use `Ui.Select.Combobox`. `Debounce` hands the
page what was typed once typing has paused, for suggestions from a database; `Money()` hands the input Flux's
`mask:dynamic="$money($input)"`, as on `Ui.Input`; the list, its keys and the pick are the runtime's (`data-rask-list`). Every example, the keyboard and the reference:
**[rask.sh/ui/autocomplete](https://rask.sh/ui/autocomplete/)**.

## Pillbox

`Ui.Pillbox` is [Flux UI's pillbox](https://fluxui.dev/components/pillbox): several answers out of a list, each a
pill that can be taken off again, bound to a collection. `Searchable()`, `.Combobox`, `Ui.PillboxOptionCreate` and
`Filter(false)` with `Debounce` for options found on the server — or `Search(async typed => …)`, as on the select, which
asks the page itself; the pills stay when a search's rows no longer hold them, and so do their rows. Rows of another
table are `.Choices(vehicles)` (or `.Choices(find, held: picked)`, or `.Choices((typed, page) => …, held: picked)` for
a searched list that [pages as it scrolls](#select)), as on the select: searched, in the reader's
alphabet, the detail as muted words after the name in the list and the name alone on the pill. It is the select's list under another trigger, so its keys are the runtime's, and past a hundred options it draws only the rows in view, as the select does ([a long list](#select)). Bound, a pick, a pill's cross and
Backspace in the empty input send nothing: the browser draws the pills and the answers travel with the next action,
as Flux's `wire:model` (`.Live()` sends each at once). Every example, the keyboard and the reference: **[rask.sh/ui/pillbox](https://rask.sh/ui/pillbox/)**.

## Form controls

**Every one** of the kit's data-input controls works in the two shapes every Rask input does — `Bind` or
`Value` — and those whose value is a `T` implement `IFormControl<T>`:

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
browser measured, and the cursor, tooltip, summary and a pie's slice following the pointer in the browser. A screen
reader hears the drawing as an image named by its kind, series and reach, described by its rows in a hidden table
([accessibility.md](accessibility.md#tables-and-charts)). Every example, the axes, formats, sizing and pointer rules,
and the reference of its steps:
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
and the code they spell bound as one string, Flux's keys key for key; `.Submit(Ui.OtpSubmit.Auto)` sends the
enclosing form once every cell is filled, as Flux's `submit="auto"` does, and `OnComplete` hears the whole code in a
form or out of one. Every example and the reference: **[rask.sh/ui/slider](https://rask.sh/ui/slider/)** and
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
| `Ui.Otp` | the code — `OnComplete` runs each time its last cell is filled, in both modes, and `.Submit(Ui.OtpSubmit.Auto)` sends the form then |


**File upload is Flux UI's**: `Ui.FileUpload` around a `Ui.FileUploadDropzone`, a `<label>` around a real file input
that takes files clicked, reached by keyboard or dropped, and a `Ui.FileItem` per file with a `Ui.FileItemRemove`.
The files reach the page through `OnFiles`, and the page draws the list from its own state. Every example, dragging,
progress, previews and the reference: **[rask.sh/ui/file-upload](https://rask.sh/ui/file-upload/)**.
**One file bound to a form** is `Ui.FilePicker.Bind(() => f.Scan)` over a `PickedFile?` member — the upload while it
is empty, the file and its remove button once picked, read as it is picked and refused under the field above
`MaxSize`; `Ui.Field.Bind` draws one for an operation's `Attachment` parameter ([file storage](file-storage.md#a-file-the-aggregate-holds-attachment)).

**A field with no value yet opens on its type alone**: `Ui.Input.Of<string>().Label("Search")`. A form
control's openings are its mode pins, so a step like `Label` never gets to pin `T` — without
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
A date typed outside `Min`/`Max`/`Unavailable`, or a range outside `MinRange`/`MaxRange`, is not taken, and the field
says why ("Choose a date on or after Mar 1, 2026.") — see [forms](forms.md#a-choice-takes-only-what-it-offered).
Every example and the reference: **[rask.sh/ui/date-picker](https://rask.sh/ui/date-picker/)**.

### Datetime picker

`Ui.DatetimePicker` is [Flux UI's datetime picker](https://fluxui.dev/components/datetime-picker): a date and a
time from one field — the calendar with a list of times beside it, or the time typed (`.Time(Ui.DatetimePickerTime.Input)`)
— over a `DateTime`, or a start and an end over a `UiDateTimeRange` (`Ui.DatetimePicker.Range`). Wall-clock times,
as a `datetime-local` field holds them: nothing is converted between time zones. `Min`/`Max`, `MinTime`/`MaxTime`,
`Unavailable` days and times (`UiDatetimeSlot`), `DefaultTime`, `WithConfirmation()`, `Clearable()`, a trigger of
your own and the date picker's calendar options; a sheet on a phone.
`Ui.DatetimePicker.Bind(() => m.StartsAt).MinTime(new(9, 0)).MaxTime(new(17, 0))`. A time outside those limits, or in
an unavailable slot, is not taken and the field says why ("Choose a time at or after 9:00 AM."). Every example, the keyboard and the
reference: **[rask.sh/ui/datetime-picker](https://rask.sh/ui/datetime-picker/)**.

### Time picker

`Ui.TimePicker` is Flux UI's time picker: a button over a list of times, or hour and minute typed in parts. A
`TimeOnly?` picks one or none and a collection of `TimeOnly` several. A time typed before `Min`, after `Max` or in an
`Unavailable` stretch is not taken and the field says why. Every example, the keyboard and the reference:
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

That is why every class the kit can write is spelled out as a complete literal in its source — in each
component's own file, `UiButtonClasses.cs` for the button — and why the axes above are closed enums rather
than strings.

If you write the accent yourself, there is nothing to copy: the kit's `@theme` reaches your Tailwind build
through `@import "./vendor/rask-ui.css"`, so `bg-accent/10` or `text-accent-content` in your own markup
compiles against the same tokens the kit's components use.

## Two rules it holds itself to

**Mobile-first, which is a different claim from responsive.** Every control takes a 44px touch target
below `sm`. A dialog is a bottom sheet on a phone and a centred card above it, because a centred
dialog at 360px either overflows or shrinks its content past reading. The tab bar scrolls sideways
rather than wrapping, so the header is exactly one row tall however many tabs there are.

**Every control has a name.** A field's `Label` becomes its accessible name, a real `<label for>`, rather
than a placeholder — a placeholder disappears the moment typing starts, so the one thing saying
what a field is for vanishes exactly when a reader might check it. An icon-only button is named with
`.AriaLabel("…")` or a `Tooltip`; a spinner is `aria-hidden` with its words beside it; a toast's
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
