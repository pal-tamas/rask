# Accessibility: ARIA, roles & focus

How to make Rask components accessible — setting ARIA attributes, roles, and keyboard focus on any
element, plus the analyzer that catches missing image alt text.

- [Typed ARIA: `AriaLabel`, `AriaExpanded`, `AriaLive`…](#typed-aria-arialabel-ariaexpanded-arialive)
- [The `Aria` dictionary](#the-aria-dictionary)
- [`Role` and `TabIndex`](#role-and-tabindex)
- [Language of parts (`Lang` and `Dir`)](#language-of-parts-lang-and-dir)
- [Attribute order](#attribute-order)
- [Images and alt text (RASK023)](#images-and-alt-text-rask023)
- [What's not covered yet](#whats-not-covered-yet)

---

## Typed ARIA: `AriaLabel`, `AriaExpanded`, `AriaLive`…

Every `aria-*` state and property the [WAI-ARIA spec](https://w3c.github.io/aria/#state_prop_def) defines is a
typed step on every element, generated at build time from the spec itself (pinned in
`src/Rask.Core/Dom/mdn.snapshot.json`, refreshed with the MDN data). The name is the DOM's own IDL name —
`ariaLabelledByElements` is written from markup as ids, so it is `AriaLabelledBy` — and the type is the
spec's value type:

| Spec value type | C# | Example |
|---|---|---|
| true/false, true/false/undefined | `bool?` | `.AriaExpanded(open)`, `.AriaHidden()` (no argument = `true`) |
| tristate, token | a generated enum | `.AriaChecked(AriaChecked.Mixed)`, `.AriaLive(AriaLive.Polite)`, `.AriaCurrent(AriaCurrent.Page)` |
| token list | a generated `[Flags]` enum | `.AriaRelevant(AriaRelevant.Additions \| AriaRelevant.Text)` |
| integer, number | `int?`, `double?` (invariant) | `.AriaLevel(2)`, `.AriaValueNow(0.5)` |
| ID reference(s), string | `string?` | `.AriaLabelledBy("title")`, `.AriaDescribedBy("hint err")`, `.AriaLabel("Close")` |

```csharp
Button.AriaExpanded(_open).AriaControls("menu").AriaHasPopup(AriaHasPopup.Menu)["Options"]
// <button aria-controls="menu" aria-expanded="false" aria-haspopup="menu">Options</button>

Div.Role(AriaRole.Status).AriaLive(AriaLive.Polite)[_statusMessage]
// <div role="status" aria-live="polite">…</div>
```

`null` leaves an attribute out, so `.AriaExpanded(null)` is "undefined" in the spec's sense. `AriaRole` holds
every concrete role as a constant (`AriaRole.Tablist`, `AriaRole.Menuitemcheckbox` — each name is the role's
token, PascalCased); `Role` stays a `string`, so a role the constants do not list yet still works.

The keyword enums have no per-keyword steps (`Div.Polite` would not say what is polite) — pass the value. A
boolean or keyword is stored and written as the interned literal it renders as, so the typed steps allocate
nothing per render; an element that names none of them pays nothing at all.

## The `Aria` dictionary

The dictionary stays for what the typed steps cannot say — an attribute newer than the snapshot, a value
built at run time from a key. Every element exposes an `Aria` parameter — a `string → string?` dictionary modelled exactly on the
[`Data` (data-*) bag](js-interop.md). Each entry renders as `aria-{key}="{value}"`: the key is used
verbatim (so you write `"label"`, not `"aria-label"`) and the value is HTML-encoded. A `null` value
emits a bare attribute.

```csharp
Button.Aria(new() { ["label"] = "Close", ["expanded"] = "false" })["✕"]
// <button aria-label="Close" aria-expanded="false">✕</button>

Span.Class("icon").Aria(new() { ["hidden"] = "true" })["\U0001F5D1"]
// <span class="icon" aria-hidden="true">🗑</span>  — decorative icon, skipped by screen readers.
// The glyph carries no meaning a reader needs; the BUTTON around it carries the accessible name.
```

A typed step and a bag entry for the same attribute never both render: the typed value wins and the bag's
entry is skipped, so a component can take a call site's bag and still set `.AriaExpanded(open)` itself.

## `Role` and `TabIndex`

`role` and `tabindex` are not `aria-*` attributes, so they have their own typed parameters on every
element:

```csharp
Div.Role(AriaRole.Dialog).TabIndex(-1).AriaModal(true).AriaLabelledBy("title")[
    H2.Id("title")["Edit product"],
    // ...
]
```

`Role` is a `string?`; `TabIndex` is an `int?` (`0` to make a non-interactive element focusable, `-1`
to take it out of the tab order but keep it programmatically focusable).

## Language of parts (`Lang` and `Dir`)

`Lang` marks the language of an element's content as a BCP 47 tag, and it belongs on any run of text in
a different language from the page — not only on `<html>`. A screen reader switches pronunciation on it,
and without it a French quotation inside an English page is read with English phonetics. That is
[WCAG 3.1.2 *Language of Parts*](https://www.w3.org/WAI/WCAG22/Understanding/language-of-parts), a
Level AA criterion:

```csharp
P["The exhibition is called ", Span.Lang("fr")["Les Demoiselles"], "."]
```

`Dir` is the same idea for direction — `"ltr"`, `"rtl"` or `"auto"`. Reach for `"auto"` on text you did
not author and whose language you do not know at render time (a display name, a comment, a search
query): the browser takes the direction from the first strongly-typed character, which is the only
correct answer when the content is arbitrary.

`Hidden` and `Inert` belong to the same family. `Hidden` removes an element from every presentation
*including* the accessibility tree — prefer it to a display-none class, which hides an element visually
while leaving a screen reader announcing it. `Inert` makes a whole subtree unfocusable and unreachable,
which is the correct primitive behind a modal (see [Focus trapping](#focus-trapping-overlays)).

## Attribute order

Accessibility attributes render in the universal attribute block, after `data-*` and before any
tag-specific attributes. The full documented order is:

```
id, class, style, title,
lang, dir, hidden, inert, popover, contenteditable, spellcheck, translate,
data-*, role, tabindex, aria-* (typed, then the Aria bag), Attributes, then tag-specific
```

The typed `aria-*` attributes render in the spec's (alphabetical) order, then the bag's entries in its own
order, less any a typed step already wrote. Tests assert this order; it is stable across releases.

## Images and alt text (RASK023)

`Img` requires an `Alt` for accessibility. The [RASK023](diagnostics.md#rask023) analyzer warns when
an `Img` chain omits it:

```csharp
Img.Src("/logo.png").Alt("Rask logo")   // informative image
Img.Src("/divider.png").Alt("")          // decorative: empty alt hides it from assistive tech
```

Pass `Alt: ""` for purely decorative images so screen readers skip them; pass a meaningful string
for everything else.

## Form validation

A Rask UI field wires its label, description, error and validation state to assistive tech on its own — you
add nothing. `Ui.Input` and `Ui.Textarea` render:

- `aria-invalid="true"` when the field is invalid — a bound field whose form holds a message for it, or a
  controlled field given `Invalid()` — so the failed state is exposed programmatically, not only as a red
  border;
- `aria-describedby` naming what is **visible** around the control, error first: the field's `Ui.Error` (only
  while it holds a message — a hidden message named by `aria-describedby` would still be read aloud), then the
  `Description`. Omitted when there is nothing; and
- a real `<label for>` from `Label`, which is the control's accessible name.

```csharp
Ui.Input.Bind(() => model.Email).Label("Email").Description("We never share it.")
// valid   → <input id="f-email" aria-describedby="f-email-description" …>
// invalid → <input id="f-email" aria-invalid="true" data-invalid
//                  aria-describedby="f-email-error f-email-description" …>
//           <div id="f-email-error" role="alert" aria-live="polite">Enter a valid email</div>
```

The hint, error and message ids derive from the field id — `Id` if you set one, otherwise the bound
member's name or the label text. That id also anchors the `<label for>` association, so **if you render
the same bound field more than once on a page** (a repeated form, a list of rows), give each field an
explicit unique `Id` so every `for` and `aria-describedby` resolves to the right element.

Building your own control from the core `Input`/`Validation.Message` primitives? Mirror the same
attributes: `.AriaInvalid(AriaInvalid.True).AriaDescribedBy(errorId)` on the control, and give the message
element that id. See [forms-validation.md](forms-validation.md).

## Focus trapping (overlays)

Any element that carries `data-rask-focus-trap` gets accessible-overlay focus management from the
runtime — no component library's JavaScript, no per-component wiring. While the element is in the DOM, focus moves into
it on open (its `[autofocus]` element, else the element itself), `Tab`/`Shift+Tab` cycle **within** it
(focus can't reach the inert page behind), and focus returns to the previously-focused element when it
closes. If the trap (or a descendant) carries `data-rask-dismiss`, `Escape` closes it by triggering that
element's click handler — no per-keystroke server round-trip. The trap follows the attribute as well as the
element: adding or removing `data-rask-focus-trap` on an element that stays mounted engages or releases it.

Rask UI's `Ui.Modal` needs none of this: it is a modal `<dialog>` — opened by a `Ui.ModalTrigger`'s
`command="show-modal"`, or by the runtime when the page's state says so (`Ui.Modal.Open(…)`,
`data-rask-modal-open`) — so the browser's own modal dialog makes the page inert, closes on Escape and returns
focus to what opened it. While one is open the runtime also stops the page behind it from scrolling
(`data-rask-lock="scroll"`).

As Flux UI's does, a `Ui.Modal` opens with focus on nothing: an empty `autofocus` placeholder inside the dialog
takes the focus the browser would hand the first field, then leaves, so no control is ringed before the reader
chose one and the first `Tab` lands on the first control. A modal has no title of its own — its content
carries the heading, exactly as Flux's does. Its close button is named "Close modal".

A dialog should opt in deliberately: an open modal traps focus, is labelled (`aria-labelledby`
its title, or `aria-label` from the title text), and dismisses on `Escape` (except with a static backdrop,
which keeps `Escape` inert). Build your own overlay the same way — add `data-rask-focus-trap`
(via the `Data` dictionary) and mark your close control with `data-rask-dismiss`.

A menu that must escape an `overflow: hidden/auto` ancestor uses the platform's own answer instead:
a `[popover]`, which the browser lifts into the top layer, dismisses on Escape and on a click outside,
and gives a real `::backdrop`. `Ui.Select`'s `.Listbox` and `.Combobox` variants, `Ui.Menu` and `Ui.Modal` are all
built that way. C# hears the browser's own dismissal through `OnToggle`, which is what lets a control
keep `aria-expanded` truthful rather than drifting the moment Escape is pressed.

A second runtime helper contains the navigation keys while such a list is open — the live client never
calls `preventDefault`, so without it ArrowDown would scroll the document behind the list and Enter
would submit the surrounding form. It keys off `aria-expanded` on the closest `[role=combobox]`, and
deliberately leaves Escape alone, since Escape's default *is* the dismissal.

`Ui.Tree` has the same shape and its own rule: one focusable `[role=tree]` with the cursor named by
`aria-activedescendant`, the arrows, Home/End, Page keys and Space contained while it has focus (Enter
left alone — a focused non-form element has no default for it). Because the cursor is an attribute
rather than the focus, the runtime also scrolls the named row back into view when it moves out of sight,
and in a virtualized tree — where that row is not rendered at all — it scrolls to where the row will be,
which is what loads it.

## Menus

An element with `role="menu"` is a list with a cursor inside it, and the cursor is where FOCUS is: the row the
cursor is on carries `data-active` and `tabindex="0"`, and the runtime moves focus to it whenever a render moves
the attribute — roving focus, as Flux UI's menus have it, so a screen reader follows the arrow keys row by row.
The runtime treats a menu like one: the arrows, Home/End, Page keys and Space are contained so they move the
cursor rather than scrolling the page, and Enter or Space press the row that has focus — its own click handler,
link or checkbox runs exactly as a pointer would run it. ArrowDown on the closed button that opens a menu (an invoker with `aria-haspopup` whose popover is, or
holds, a `role="menu"`) opens it onto its first row; a pick closes the popover the menu sits in unless the row, or something around it,
carries `data-rask-keep-open`; Tab out of an open menu closes it; a click outside closes it and focus is handed
back to the trigger. Escape is the browser's, and hands focus back too. Rask UI's `Ui.Menu` — what a
`Ui.Dropdown` opens — builds on this, with `menuitem`, `menuitemcheckbox` and `menuitemradio` rows and Flux's
keyboard: the arrows stop at the ends and step over disabled rows, a letter jumps to the row starting with it,
ArrowRight or Enter opens a submenu and ArrowLeft closes it. A `Ui.Navmenu` is not a menu: it is a `<nav>` of
links, reached with Tab. `Ui.Context` is the same menu opened by a right-click: the runtime shows its popover at
the pointer (`data-rask-contextmenu`), the ContextMenu key and Shift+F10 open it at the focused element, the
menu takes focus as it opens, and focus goes back where it was when it closes —
so give its area something focusable, or a keyboard user has no way in. `Ui.Command`, the command palette,
is the combobox pattern instead: focus stays in its search box, the commands are the `option`s of the `listbox` it
controls, the highlighted one is `aria-activedescendant` and says `aria-selected="true"`, and Enter presses it.

A `[popover]` carrying `data-rask-popover-open="true"|"false"` is shown or hidden to match whenever the attribute
changes — how a controlled menu opens from C#, which cannot call `showPopover()`.

## Tooltips

A tooltip that is only drawn is a tooltip a screen reader never says. Rask UI's `Ui.Tooltip` joins its trigger
to its content at render: a trigger with text of its own carries `aria-describedby`, so the tooltip is read
after its name, and a trigger without — an icon button — carries `aria-labelledby`, so the tooltip IS its name.
The content is `role="tooltip"` and `aria-hidden`, read through that reference and not a second time in the
reading order. It shows on keyboard focus (`:focus-visible`) as well as on hover, stays while that focus lasts
even if the pointer passes over and leaves, and Escape dismisses it — for any trigger, since the runtime shows
it (`data-rask-tooltip`). A `Ui.Button` with a `Tooltip` is wired the same way: an icon-only one is named by
`aria-labelledby`, not by an `aria-label` copied from the tooltip.

The trigger has to be one element for this — an HTML element or a kit component that is one; around a
composite nothing is wired, as Flux wires nothing but the trigger. And a tooltip is a hint: a touch
screen has no hover, so what matters there is `Toggleable()`, which a tap opens. See
[ui-kit.md](ui-kit.md#tooltips) for the little that is left undone.

## Controls that are waiting

A `<button>` (or `<input type=button|submit>`) whose own handler is still running after 200 ms is marked
by the runtime with `aria-busy="true"` and `data-loading`, on both hosts, and a second activation is dropped
until the first handler's render has landed. It is deliberately **not** `disabled`: disabling the control
the reader just pressed moves keyboard focus to the document body, so the next Tab starts from the top of
the page. The mark comes off when the dispatch finishes, when the connection drops, or after a 30 s
backstop. Opt a control — or a container of them — out with `data-rask-loading="off"`
(`Ui.Button.Loading(false)`), and opt a non-button element in with `data-rask-loading`. See
[ui-kit.md](ui-kit.md#buttons-that-wait).

## Toasts

`Ui.Toast` is Flux's toast, announced as Flux announces it: the host is `role="status"` and each toast is
`aria-atomic="true"`, so a toast is read politely and whole — heading and text together — whatever its
variant. A variant is its icon's **shape** as well as its colour. A timed toast waits while the pointer is over
it — or, in a stack, over any toast of the stack — and then runs out what it had left; focus inside it holds
nothing, as on Flux, so one that must not be missed is raised with `.UntilDismissed()`. A toast on its own closes on Escape; its
close button and any action or link are ordinary controls in the tab order. See
[ui-kit.md](ui-kit.md) and [Toast messages](composition-lists.md#toast-messages).

## Navigation

A `NavLink` to the page being shown writes `aria-current="page"` beside its active class — what a screen reader
announces as "current page", where a class says nothing. An empty `ActiveClass` opts out of both, and an
`aria-current` the call site sets wins. Rask UI's `Ui.NavbarItem` and `Ui.NavlistItem` are built on it.

Client-side (SPA) route changes on the Server live runtime are handled accessibly without any wiring:

- **Progress.** A slow server-side route render surfaces the top progress bar (the same one a slow
  handler round-trip uses), after a ~300 ms grace so a fast navigation never flashes it.
- **Focus.** A forward, whole-page navigation moves focus into the new page's `<main>` (or its first
  `<h1>`), so a keyboard user continues from the new page instead of the now-removed nav link at the top
  of the document. Give your layout a `<main>` (`Main[...]`) to anchor this.
- **Announcement.** The new page's `<title>` is announced through a polite `aria-live` region, so a
  screen-reader user hears the route changed.

Back/Forward (popstate) navigation leaves focus and scroll to the browser's native restoration. (Server
host today; the WASM navigation path is a follow-up.)

## What's not covered yet

This is the framework primitive layer. Higher-level affordances — skip links, ARIA `tablist`/`tab`
keyboard widgets (the `aria-activedescendant` cursor in `Ui.Select`'s drawn listbox), and automated axe-core scans in the sample
E2E suite — are tracked as follow-up work. Today you build those from the typed `Aria*`/`Role`/`TabIndex`
primitives above (plus the focus trap) and standard semantic HTML (`Nav`, `Main`, `Aside`, `Label.For(…)`,
`Th.Scope(…)`, …).
