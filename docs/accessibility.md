# Accessibility: ARIA, roles & focus

How to make Rask components accessible — setting ARIA attributes, roles, and keyboard focus on any
element, plus the analyzer that catches missing image alt text.

- [The `Aria` dictionary](#the-aria-dictionary)
- [`Role` and `TabIndex`](#role-and-tabindex)
- [Language of parts (`Lang` and `Dir`)](#language-of-parts-lang-and-dir)
- [Attribute order](#attribute-order)
- [Images and alt text (RASK023)](#images-and-alt-text-rask023)
- [What's not covered yet](#whats-not-covered-yet)

---

## The `Aria` dictionary

Every element exposes an `Aria` parameter — a `string → string?` dictionary modelled exactly on the
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

Because it's a dictionary, the full [WAI-ARIA](https://www.w3.org/TR/wai-aria-1.2/) vocabulary is
reachable without a typed property per attribute — `aria-live`, `aria-labelledby`, `aria-describedby`,
`aria-current`, `aria-modal`, and the rest are all just keys.

A common live-region pattern:

```csharp
Div.Role("status").Aria(new() { ["live"] = "polite" })[_statusMessage]
```

## `Role` and `TabIndex`

`role` and `tabindex` are not `aria-*` attributes, so they have their own typed parameters on every
element:

```csharp
Div.Role("dialog").TabIndex(-1).Aria(new() { ["modal"] = "true", ["labelledby"] = "title" })[
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
data-*, role, tabindex, aria-*, Attributes, then tag-specific
```

Tests assert this order; it is stable across releases.

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

A Rask UI field wires its label, hint, error and validation state to assistive tech on its own — you add
nothing. `UiInput`, `UiTextarea` and `UiSelect` (both modes) render:

- `aria-invalid="true"` when the field is invalid — a bound field whose form holds a message for it, or a
  controlled field given `Tone(UiTone.Error)` — so the failed state is exposed programmatically, not only
  as a red border;
- `aria-describedby` naming what is **visible** under the control, error first: the bound field's own
  validation message, then a controlled `Error` (only while the tone reveals it — a hidden message named
  by `aria-describedby` would still be read aloud), then the `Hint`. Omitted when there is nothing;
- `aria-required="true"` when the field is bound to a member carrying `[Required]`. It is the one required
  rule a field can see; a FluentValidation rule or a `Validate` delegate is invisible to it, so nothing is
  guessed; and
- a `Badge` ("Required", "Optional") inside the label, `aria-hidden` so the accessible name stays the
  label's text.

```csharp
UiInput.Bind(() => model.Email).Label("Email").Badge("Required").Hint("We never share it.")
// valid   → <input id="f-email" aria-required="true" aria-describedby="f-email-hint" …>
// invalid → <input id="f-email" aria-required="true" aria-invalid="true"
//                  aria-describedby="f-email-validation f-email-hint" …>
//           <p id="f-email-validation" class="label text-ui-danger-ink">Enter a valid email</p>
//           <p id="f-email-hint" class="label">We never share it.</p>
```

The hint, error and message ids derive from the field id — `Id` if you set one, otherwise the bound
member's name or the label text. That id also anchors the `<label for>` association, so **if you render
the same bound field more than once on a page** (a repeated form, a list of rows), give each field an
explicit unique `Id` so every `for` and `aria-describedby` resolves to the right element.

Building your own control from the core `Input`/`ValidationMessage` primitives? Mirror the same
attributes: `.Aria(new Dictionary<string, string?> { ["invalid"] = "true", ["describedby"] = errorId })`
on the control, and give the message element that id. See [forms-validation.md](forms-validation.md).

## Focus trapping (overlays)

Any element that carries `data-rask-focus-trap` gets accessible-overlay focus management from the
runtime — no component library's JavaScript, no per-component wiring. While the element is in the DOM, focus moves into
it on open (its `[autofocus]` element, else the element itself), `Tab`/`Shift+Tab` cycle **within** it
(focus can't reach the inert page behind), and focus returns to the previously-focused element when it
closes. If the trap (or a descendant) carries `data-rask-dismiss`, `Escape` closes it by triggering that
element's click handler — no per-keystroke server round-trip. The trap follows the attribute as well as the
element: adding or removing `data-rask-focus-trap` on an element that stays mounted engages or releases it,
which is how Rask UI's state-driven `UiModal` hands focus back when `Open(false)` closes it in place.

Rask UI's declarative `UiModal` needs none of this: it opens with `command="show-modal"`, so the browser's own
modal dialog makes the page inert, closes on Escape and returns focus to the trigger. While any kit dialog is
open, the kit's stylesheet also stops the page behind it from scrolling.

A dialog should opt in deliberately: an open modal traps focus, is labelled (`aria-labelledby`
its title, or `aria-label` from the title text), and dismisses on `Escape` (except with a static backdrop,
which keeps `Escape` inert). Build your own overlay the same way — add `data-rask-focus-trap`
(via the `Data` dictionary) and mark your close control with `data-rask-dismiss`.

A menu that must escape an `overflow: hidden/auto` ancestor uses the platform's own answer instead:
a `[popover]`, which the browser lifts into the top layer, dismisses on Escape and on a click outside,
and gives a real `::backdrop`. `UiSelect` with `Native: false`, `UiMegamenu` and `UiModal` are all
built that way. C# hears the browser's own dismissal through `OnToggle`, which is what lets a control
keep `aria-expanded` truthful rather than drifting the moment Escape is pressed.

A second runtime helper contains the navigation keys while such a list is open — the live client never
calls `preventDefault`, so without it ArrowDown would scroll the document behind the list and Enter
would submit the surrounding form. It keys off `aria-expanded` on the closest `[role=combobox]`, and
deliberately leaves Escape alone, since Escape's default *is* the dismissal.

`UiTree` has the same shape and its own rule: one focusable `[role=tree]` with the cursor named by
`aria-activedescendant`, the arrows, Home/End, Page keys and Space contained while it has focus (Enter
left alone — a focused non-form element has no default for it). Because the cursor is an attribute
rather than the focus, the runtime also scrolls the named row back into view when it moves out of sight,
and in a virtualized tree — where that row is not rendered at all — it scrolls to where the row will be,
which is what loads it.

## Menus

An element with `role="menu"` is a focused list with a cursor inside it (`aria-activedescendant`), and the
runtime treats it like one: the arrows, Home/End, Page keys and Space are contained so they move the cursor
rather than scrolling the page, and Enter or Space press the row the cursor names — its own click handler, link
or checkbox runs exactly as a pointer would run it. ArrowDown or ArrowUp on a closed `aria-haspopup="menu"`
button opens it; a pick closes the popover the menu sits in unless the row or the menu carries
`data-rask-keep-open`; Tab out of an open menu closes it. Escape is the browser's, and hands focus back to the
trigger. Rask UI's `UiDropdown` builds on this, with `menuitem`, `menuitemcheckbox` and `menuitemradio` rows.

A `[popover]` carrying `data-rask-popover-open="true"|"false"` is shown or hidden to match whenever the attribute
changes — how a controlled menu opens from C#, which cannot call `showPopover()`.

## Controls that are waiting

A `<button>` (or `<input type=button|submit>`) whose own handler is still running after 200 ms is marked
by the runtime with `aria-busy="true"` and `data-loading`, on both hosts, and a second activation is dropped
until the first handler's render has landed. It is deliberately **not** `disabled`: disabling the control
the reader just pressed moves keyboard focus to the document body, so the next Tab starts from the top of
the page. The mark comes off when the dispatch finishes, when the connection drops, or after a 30 s
backstop. Opt a control — or a container of them — out with `data-rask-loading="off"`
(`UiButton.Loading(false)`), and opt a non-button element in with `data-rask-loading`. See
[ui-kit.md](ui-kit.md#buttons-that-wait).

## Navigation

A `NavLink` to the page being shown writes `aria-current="page"` beside its active class — what a screen reader
announces as "current page", where a class says nothing. An empty `ActiveClass` opts out of both, and an
`aria-current` the call site sets wins. Rask UI's `UiNavItem` is built on it.

Client-side (SPA) route changes on the Server live runtime are handled accessibly without any wiring:

- **Progress.** A slow server-side route render surfaces the top progress bar (the same one a slow
  handler round-trip uses), after a ~300 ms grace so a fast navigation never flashes it.
- **Focus.** A forward, whole-page navigation moves focus into the new page's `<main>` (or its first
  `<h1>`), so a keyboard user continues from the new page instead of the now-removed nav link at the top
  of the document. Give your layout a `<main>` (`Main(...)`) to anchor this.
- **Announcement.** The new page's `<title>` is announced through a polite `aria-live` region, so a
  screen-reader user hears the route changed.

Back/Forward (popstate) navigation leaves focus and scroll to the browser's native restoration. (Server
host today; the WASM navigation path is a follow-up.)

## What's not covered yet

This is the framework primitive layer. Higher-level affordances — skip links, ARIA `tablist`/`tab`
keyboard widgets (the roving cursor in `UiSelect`'s drawn listbox), and automated axe-core scans in the sample
E2E suite — are tracked as follow-up work. Today you build those from the `Aria`/`Role`/`TabIndex`
primitives above (plus the focus trap) and standard semantic HTML (`Nav`, `Main`, `Aside`, `Label(For:)`,
`Th(Scope:)`, …).
