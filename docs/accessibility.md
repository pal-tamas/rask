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

A bound form control wires validation state to assistive tech automatically — you don't add
anything. When a bound field has validation messages the control renders:

- `aria-invalid="true"` on the input/select/textarea, so the failed state is exposed programmatically
  (not just the visual `.is-invalid` red border);
- `aria-describedby` pointing at the error message's `id` (and the help-text `id` when `HelpText:` is
  set), so a screen reader reads the error together with the field; and
- the `.invalid-feedback` message as a `role="alert"` live region, so the error is announced the moment
  validation fails on submit/blur.

```csharp
BsInput.Bind(() => model.Email).Label("Email").HelpText("We never share it.")
// valid   → <input id="Email" aria-describedby="Email-help" …>
// invalid → <input id="Email" class="form-control is-invalid"
//                  aria-invalid="true" aria-describedby="Email-help Email-error" …>
//           <div id="Email-error" class="invalid-feedback d-block" role="alert">Enter a valid email</div>
```

The help/error element ids (and the `aria-describedby` that points at them) derive from the control id —
`Id:` if you set one, otherwise the bound property name or `Name:`. That id also anchors the `<label for>`
association, so the same rule has always applied: **if you render the same bound field more than once on a
page** (a repeated form, a list of rows), give each control an explicit unique `Id:` so the ids stay
document-unique and every `aria-describedby`/`for` resolves to the right field.

Building your own control from the core `Input`/`ValidationMessage` primitives? Mirror the same three
attributes: `Aria: new() { ["invalid"] = "true", ["describedby"] = errorId }` on the control, and render
the message in a `Div.Id(errorId).Role("alert")`. See [forms-validation.md](forms-validation.md).

## Focus trapping (overlays)

Any element that carries `data-rask-focus-trap` gets accessible-overlay focus management from the
runtime — no component library's JavaScript, no per-component wiring. While the element is in the DOM, focus moves into
it on open (its `[autofocus]` element, else the element itself), `Tab`/`Shift+Tab` cycle **within** it
(focus can't reach the inert page behind), and focus returns to the previously-focused element when it
closes. If the trap (or a descendant) carries `data-rask-dismiss`, `Escape` closes it by triggering that
element's click handler — no per-keystroke server round-trip.

A dialog should opt in deliberately: an open modal traps focus, is labelled (`aria-labelledby`
its title, or `aria-label` from the title text), and dismisses on `Escape` (except with a static backdrop,
which keeps `Escape` inert). Build your own overlay the same way — add `data-rask-focus-trap`
(via the `Data` dictionary) and mark your close control with `data-rask-dismiss`.

A sibling runtime helper keys off `data-rask-popover` to place the Bs dropdown-family menus (the
date/time pickers, `BsDropdown`, `BsMultiSelect`, `BsSelect`) with `position: fixed` while open, so the menu escapes
any `overflow: hidden/auto` ancestor instead of being clipped. It is placement only — the components'
keyboard navigation, ARIA roles, and focus behavior are unchanged.

## Navigation

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
keyboard widgets (roving tabindex for `BsTabs`/`BsDropdown`), and automated axe-core scans in the sample
E2E suite — are tracked as follow-up work. Today you build those from the `Aria`/`Role`/`TabIndex`
primitives above (plus the focus trap) and standard semantic HTML (`Nav`, `Main`, `Aside`, `Label(For:)`,
`Th(Scope:)`, …).
