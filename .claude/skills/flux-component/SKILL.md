---
name: flux-component
description: Build or change a Rask.Ui component so it looks and behaves EXACTLY like its Flux UI (fluxui.dev) counterpart. Use for every component of the Flux rebuild — a new `Ui.*` component, replacing a daisyUI-drawn one, or fixing a parity difference. Measures Flux's live docs, writes the C# component from the measurements, proves parity with scripts/flux/parity.mjs, converts call sites, and deletes the daisy component it replaces.
---

# flux-component — one Flux UI component, exactly

Rask.Ui mirrors Flux UI: Flux's catalogue (51 components, 199 parts), Flux's names, Flux's look and
behaviour **to the pixel and to the keypress**. daisyUI is being removed component by component.

## The line that is never crossed
`livewire/flux` on GitHub is **proprietary** and its terms forbid derivatives in open source. Rask is
MIT. **Never open, read or copy that repository's Blade, CSS or JS.** Everything comes from the
PUBLIC docs site: its markdown (`https://fluxui.dev/components/<slug>.md`, index at `/llms.txt`) and
its live rendered examples, which `scripts/flux/measure.mjs` measures. Class strings are written from
the measurements, never transcribed from a page's `class` attributes.

## 1. Read and measure
```bash
curl -sS https://fluxui.dev/components/<slug>.md          # examples + the Reference section
node scripts/flux/measure.mjs artifacts/flux-parity/flux components/<slug>
```
`measurements.json` holds, per example and per scheme (light, dark), every node's box and computed
styles, its `::before`/`::after`, and what `:hover`, `:active` and `:focus-visible` change. PNGs of
each example sit beside it — look at them. Flux is Tailwind v4 on the default palette, so a measured
colour IS a Tailwind colour: `oklch(0.274 0.006 286.033)` is `zinc-800`, `rgba(0,0,0,.05) 0 1px 2px` is
`shadow-xs`. Name every value before writing a class. Every detail counts: a bottom border one shade
darker, an inset highlight, 12px on the icon side and 16px on the other.

**A layout page** (`layouts/header`, `layouts/sidebar`) has no examples: it links to full-document demos
(`fluxui.dev/demo/<name>`). `parity.mjs layouts/<slug>` measures each demo whole — 1280 and 390 wide, light
and dark, as loaded, narrowed to its rail and slid over the page — against one document per demo, written
from a `FluxLayoutParity` (`tests/Rask.Ui.Tests/Flux/Parity/SidebarLayoutParity.cs`). What is not rendered
is not compared, and another component placed in the layout is compared as a box.

Behaviour is measured too: open the page, use the component with keyboard and pointer (Playwright via
`scripts/flux/lib.mjs`'s `chromium()`), and write down what each key does before implementing it.
A state no example shows (dragging, uploading, invalid) is read by writing Flux's attribute on the LIVE node
(`el.setAttribute('data-dragging', '')`) and diffing computed styles; when that changes nothing, the state is
decided on the server and cannot be measured — say so in the component, never guess silently.

## 2. Write the component
**Nothing beyond Flux** — the owner, 2026-10-07: *"ne csináljunk ilyen kiegészítéseket"*. A component carries
exactly Flux's props, values, attributes and behaviour: no extra prop, no extra ARIA, no convenience step, no
wider range, however reasonable. The only non-Flux surface is the Rask TRANSLATION of a Livewire / Alpine /
PHP mechanism — `Bind`/`Value` for `wire:model`, a `Callback` for an event or `wire:click`, `RouteUrl? Href`
with client-side navigation for a link, a `Toast`-style facade for a `Flux::…()` call — plus `Class`, which
every Flux component takes. Same spirit, 2026-10-06: Flux stops no animation under reduced motion, and
neither does the kit. `FluxConformanceTests` is the gate: a property a built component declares, or a member
of a prop's enum, that `flux.snapshot.json` does not list fails unless a `Translations` row (`part/Member` or
`part/Member=EnumMember`) names what of Flux's it stands for. Free by rule: `Class`, `Key`, `Id`, `Children`,
what `UiElement`/`Element`/`Component` hand down, a named slot's property, and an enum's zero member (the
unset prop).
Rendered attributes are not in that gate: compare them with Flux's live DOM (marker and ARIA names per
`[data-flux-*]` node) when a component changes.

- **File and type:** `src/Rask.Ui/Ui<Name>.cs`, `public sealed partial class Ui<Name>`; a part is
  `Ui<Parent><Part>` (`flux:button.group` → `UiButtonGroup`, reached as `Ui.ButtonGroup`).
- **Props are Flux's**, one property per documented prop: `icon:trailing` → `IconTrailing`,
  `tooltip:position` → `TooltipPosition`. Optional props are nullable; `tests/Rask.Ui.Tests/Flux/flux.snapshot.json`
  is the list.
- **One enum per component** for a prop's values, nested in `Ui` (`src/Rask.Ui/Ui.<Name>.cs`):
  `Ui.ButtonVariant { Outline, Primary, Filled, Danger, Ghost, Subtle }`, FIRST member = Flux's default.
  The chain generator turns each member into a step (`Ui.Button.Primary.Sm.Blue`) for an enum of up to
  24 members, so every `Ui.Color` prop has its hues as steps. Shared across components:
  `Ui.Color` (ONE file, `Ui.Color.cs`: Tailwind's 17 chromatic hues in its order, then Slate, Gray, Zinc,
  Neutral, Stone — a component whose Flux prop has no neutrals draws a neutral as its default, as `UiText`
  does), `Ui.IconName`, `Ui.IconVariant`, `Ui.Position`, `Ui.Align`, `Ui.Inset` (flags). A member named like
  an inherited HTML tag entry (`Strong`, `Desc`, `Button`, `A`, `Div`, `Text`, `Search`, `Time`) gets a step
  that cannot be called (CS0176): write `.Variant(Ui.TextVariant.Strong)`, `.As(Ui.ButtonAs.A)`,
  `.As(Ui.BadgeAs.Button)` there. Known generator limit, not fixed. A component with an `IconVariant` prop
  gets ITS members as steps too: `.Outline`, `.Mini`, `.Micro` on `Ui.Badge` set the icon's drawing, not the
  badge's look.
- **Rask's own idiom where Flux's is Livewire's:** `wire:model` → `Bind`/`Value` (see
  `docs/building-form-controls.md`); an event → a non-nullable `Callback`/`Callback<T>`; a link →
  `RouteUrl? Href`; a slot → children (the indexer) or a `Component?` property for a named slot.
- **Marker:** the root writes `data-ui-<part>` where Flux writes `data-flux-<part>` (`data-ui-button`,
  `data-ui-button-group`), bare, through the ONE helper: `private static readonly UiPartMarker Marker =
  new("ui-button");` and `ResolveData() => Marker.With(Data)` (`With(Data, "color", name)` for one more
  attribute; `new("slot", "text")` for a valued one). The parity tool pairs nodes by it. Write only the
  `data-ui-*` attributes Flux writes, and ALL of them: every one is a marker to the tool. A root Flux marks
  twice chains `Marker.And("ui-group-target")` into a second static marker (the button: every variant but
  ghost and subtle, and `data-ui-loading` beside the runtime's `data-loading`); a colon is kept as Flux
  writes it (`data-ui-badge-icon:trailing`). A host that carries SEVERAL markers
  (`UiToast`: `data-ui-toast`, the dialog's, position, variant) writes them through the `Data` bag instead —
  accepted there, not a pattern for a one-marker root.
- **Element:** where Flux renders a custom element (`<ui-field>`, `<ui-label>`, `<ui-progress>`,
  `<ui-table-scroll-area>`, `<ui-disclosure>`), write the NATIVE element with the same semantics and no
  script (`<div>`, `<label for>`, `<div role="progressbar">`, `<details>`) and add the pair to `NATIVE` in
  `parity.mjs`. A `UiElement` MUST override `TagName`, or it renders itself for ever (a stack overflow that
  kills the test process). A part that puts markup of its own around its children is a `Component`, not a
  `UiElement` (Core's serializer writes an element's children straight from the indexer) — `UiCallout`,
  `UiTable` via `HostedElement`. The one exception is `UiButton`, which stays the `<button>` and composes its
  icon, spinner and `Kbd` in `RenderChildren`; that takes its enclosing component off the cached-render fast
  path, so it only does it when it has such a part (plain `Ui.Button["Save"]` stays fast). With a `Tooltip` it
  does what Flux does and wraps itself: `TagName` is null, `Render` returns `Ui.Tooltip[HostedElement.Owner(this)]`
  and `BypassRenderCache` is on, because the label is baked into that render (`Element` says it bakes nothing,
  and that cannot be overridden from the kit).
- **A new tab:** `Ui.Link.External()` is Flux's prop and writes `rel="noopener noreferrer"` with the target. A
  button has no such prop in Flux, so it has none here: the call site forwards the anchor's own attributes,
  `.Attributes(("target", "_blank"), ("rel", "noopener noreferrer"))`, and nothing is added to them.
- **Icons:** `Ui.Icon.Name(Ui.IconName.ChevronDown).Variant(Ui.IconVariant.Micro)` (or `.Mini`, `.Solid`) —
  never an inline SVG path. The enum is all of Heroicons, generated by `scripts/flux/icons.mjs`.
- **Classes:** complete Tailwind literals in the component (a `private static string` switch per
  enum). The `zinc` scale and `fx-accent` / `fx-accent-content` / `fx-accent-foreground` tokens
  (`src/Rask.Ui/Styles/ui.css`), `dark:` for dark. **No daisyUI class, no `base-*`, no `--color-ui-*`.**
  Never build a class by concatenation — Tailwind only emits what it can read whole.
  Write Flux's classes exactly as Flux writes them: an app compiles them into its ONE stylesheet from
  the kit's class list (`@import "./vendor/rask-ui.css"`, `docs/tailwind.md`), so a `dark:` or `sm:`
  variant follows its base utility by Tailwind's own order. A NEW component never adds a `[:where(&)]:`,
  a `!` or a split-variant spelling to win against an app's utility — if one loses, the page links two
  sheets and that is the bug. (Existing components keep the defensive spellings they have; not rewritten.)
- **CSS only when a utility cannot say it** (a keyframe, a `:has()` chain): in `ui.css` under
  `@layer rask`, keyed on the `data-ui-*` marker.
- Markup, ARIA and keyboard are part of "exactly": same element, same roles, same states.
- Follow `CLAUDE.md` and `docs/api-style.md`; XML-doc every public member in a line or two.

**Form controls** take Flux's `Label` / `Description` / `DescriptionTrailing` / `Badge` and never draw a
label themselves. The recipe (`UiInput.cs` and `UiTextarea.cs` are the two to copy):
1. `public sealed partial class UiX<T> : Component, IFormControl<T>, IUiFormControl` — NOT `UiFormField<T>`, which
   stays only for the daisyUI controls and is deleted with the last of them. Declare the five binding props
   (`Value`, `OnChange`, `Bind`, `Validate`, `AfterBind`), Flux's props, `Invalid`, `ShowValidation`, `Id`, `Class`;
   `string IUiFieldControl.ControlId => UiFieldId.Derive(Id, Bind, Label);`, `LambdaExpression? IUiFieldControl.Bound => Bind;`
   (a prop Flux does not document on that part, e.g. `Badge` on the input: `string? IUiFormControl.Badge => null;`).
2. In `Render`: `var field = UiWithField.For(this);` → build the native control → `.Id(field.ControlId)`,
   `.Aria(field.Aria)` (`aria-invalid`, `aria-describedby`), `data-ui-control` plus `data-invalid` when
   `field.Invalid` → `return field.Wrap(control);` (a checkbox, radio or switch:
   `field.Wrap(control, Ui.FieldVariant.Inline, controlFirst: true)`). No label and no description ⇒ `Wrap` returns
   the control alone, and it shows no message.
3. Binding: a control that IS one native element forwards to Core's — `Bind is { } bind ?
   Input.Bind(bind).Validate(Validate).AfterBind(AfterBind) : Input.Value(Value).OnChange(OnChange)` (both hand back
   the same element; same for `Textarea`, `Select`). A control drawn from several elements reads and commits through
   `UiFormCommit.Resolve(this)` / `UiFormCommit.CommitAsync(…)`.
4. State a click changes (a reveal toggle) is a private field set in the handler; what CSS can decide (a clear button
   hidden while `:placeholder-shown`) is CSS. No script, and nothing Flux does not have: a behaviour that needs page
   script goes in `FluxConformanceTests.NotTranslated` with the hook it is waiting for.
5. Class literals shared by a generic control live in a non-generic `internal static class UiXLook` (a static in
   `UiX<T>` is one copy per `T`, S2743). An enum member named after a tag (`Button`, `Input`) is not reachable as a
   step — the component inherits the markup entry of that name — so it is `.As(Ui.InputAs.Button)`.
6. A control Flux writes as a custom element WITH PARTS INSIDE (`ui-checkbox`, `ui-radio`, `ui-switch`) cannot be the
   `<input>` itself, which holds nothing: the root is a `<label>` around the real input (`sr-only`, never `hidden`),
   and every part reads the input's own state — `has-checked:` on the root, `group-has-checked/option:` inside it
   (`UiOptionLook.cs`). Where two states meet, write the rule for the pair with both variants stacked: two rules of
   equal weight leave the winner to the order Tailwind prints them in. `parity.mjs`'s `HOLDS_INPUT` names the tags
   whose label holds that one node Flux has none for.
7. A GROUP whose choices are children (`UiRadioGroup<T>`, `UiCheckboxGroup<T>`) is the `IFormControl<T>`; it hands
   the choices a scope through `Context.Provide` (current value, commit, variant) and they bind nothing themselves.
   A prop that is Flux's `value` on a choice stays `Value` there, so the choice's own state is `Checked`.

A custom element is written as the native one that behaves that way without script (`ui-label` → `<label for>`);
`parity.mjs`'s `NATIVE` (by tag) and `NATIVE_PART` (by marker) tables name each pair, and a stand-in for a control
not rebuilt yet carries `data-parity-skip` (held to its place and size only).

**Bleed** is one contract, the card's (Flux's `--flux-bleed-*`): `Ui.Card` and `Ui.CardBody` set
`--ui-card-radius`, `--ui-bleed-x`, `--ui-bleed`, `--ui-bleed-top|bottom` and `--ui-bleed-top|bottom-radius`;
whatever bleeds (`Ui.CardBleed`, `Ui.Table.Bleed()`) only reads them, with `first:`/`last:` for the edges.
Never key on `[data-ui-card]` from another component.

## 3. Prove it
1. `tests/Rask.Ui.Tests/Flux/Parity/<Name>Parity.cs` — derive from `FluxParity`, translate EVERY
   example on Flux's page (same order, same words; section = the `<h2>` id above it). Lay out with `Row`,
   `Stack`, `SpaceY(n, …)` (Tailwind's `space-y-n`) or an inline style: a utility written in a test is in no
   sheet. A utility Flux's example hands to `class` is an APP's: state it in a `<style>` under a name of
   your own (`.parity-up{…}`) — never as `.bg-white{…}`, which unlayered would beat the kit's `dark:`.
2. Add the part to `Built` in `FluxConformanceTests`; anything that does not translate goes in
   `NotTranslated` with its reason.
3. ```bash
   dotnet test tests/Rask.Ui.Tests --filter "FullyQualifiedName~Flux"
   node scripts/flux/parity.mjs <slug>            # until: "matches Flux"
   ```
   A menu loads CLOSED, so `parity.mjs` never sees it. `node scripts/flux/parity-menu.mjs <dropdown|context>`
   opens every example on both pages and compares the popup: placement against the trigger, each row at
   rest, under a real pointer, pressed and under the cursor, a submenu's flyout, Escape and a click outside.
   Fix the COMPONENT until it passes. A difference is only accepted when it is Livewire-specific or
   comes from the docs page rather than the component; say which, in a comment on the example.
   Two false greens to know: a crashed `parity.mjs` prints no `FAIL` (read its last line), and
   `dotnet test` prints `Passed!` with a lower total after the test process crashed — look for
   `FATAL ERROR` / `Test Run Failed`, and compare the total with the last run.
   `parity.mjs` measures a page AS LOADED, where a modal is a trigger and a dialog nobody is shown.
   `node scripts/flux/parity-modal.mjs` presses each example's trigger on both pages and compares the
   open dialog — its subtree, its box in the viewport, `::backdrop`, `data-open`, the page lock, focus — then
   the transitions in and out, and what Escape, a click outside, a press dragged across the panel's edge and
   each close button do (36 checks). It then opens the same dialogs the way a RENDER does, on the pseudo-page
   `modal-state` (`ModalStateParity`, `Ui.Modal.Open(false)`): it changes `data-rask-modal-open` and nothing
   else, and holds the result to what Flux's trigger opened. Last (24 more, 60 in all) the confirmation as an
   app writes it, which Flux's page does not show: the script turns Flux's confirmation into a BARE modal and
   into one whose heading is a whole question (`CONFIRM`, made on the live page) and holds `modal-confirm`
   (`ModalConfirmParity`) to it at 1280 and 390 — the width, the close button against its corner, how near the
   heading's first line comes to it — and `Ui.ConfirmLeave` to Flux's corner. It waits on state (open, no transition running,
   the same box for three frames). A component that is only itself once opened needs the same; start from it.
4. Unit tests in `tests/Rask.Ui.Tests/Components/Ui<Name>Tests.cs`: behaviour and markup contract
   (roles, attributes, what a prop writes). Names are sentences; three blank-line-separated blocks.

### The harness, as it is (`scripts/flux/lib.mjs`, `parity.mjs`, `FluxParityPages.cs`)
One harness for every page. Do not patch it to pass a page; if a rule is missing, add ONE general rule
with a comment, and re-run every built page (`field heading text icon separator skeleton progress table
card accordion callout button toast badge tooltip kanban dropdown context input textarea select autocomplete pillbox modal checkbox radio switch editor
calendar date-picker time-picker slider otp-input pagination timeline tabs file-upload navbar brand profile breadcrumbs avatar layouts/sidebar layouts/header` today, plus `rail.mjs` (the collapsed sidebar, below) and the open-state scripts `parity-toast.mjs`, `parity-tooltip.mjs`,
`parity-menu.mjs dropdown|context`, `parity-modal.mjs`, `parity-select.mjs`, `parity-autocomplete.mjs`,
`parity-pillbox.mjs`, `parity-editor.mjs` and `parity-date.mjs date-picker|time-picker`; `pillbox-picked` is a page only `parity-pillbox.mjs` reads, as `toast-shown` is the toast's).
- **What opens** is not in a page as loaded. `scripts/flux/open.mjs` is the one module for it, and
  `parity-select.mjs`, `parity-autocomplete.mjs` and `parity-pillbox.mjs` are its configs (selectors, NATIVE
  pairs, walks): it opens each example on Flux's page and on the parity page, compares the popup subtree, its
  box against the trigger and a row hovered and pressed. `--record` walks Flux's page alone and prints every
  step — write the behaviour table from that BEFORE the component; `--live <url>` walks a running site against
  Flux's, since a static page has no runtime (`--wait 2200` on a Debug WASM site, which takes over a second
  to draw a page of demos again). A state that needs picks first (`pick: [1, 2]`) gets a parity page of its
  own written with them (`PillboxPickedParity`, `raskPage`). A difference the runtime cannot close yet is a
  walk of its own marked `accepted`, and an entry in `NotTranslated`.
- **A control built over another** hands it what is its own through an INTERNAL chain step written by hand
  (`UiInput.HostedBy`, `UiSelectControl.AsPillbox`): no public prop, so no step Flux does not have. Such a
  step must call `BuilderRuntime.MarkChanged(this)` as a generated one does, or the child serves its cached
  render, and it goes BEFORE the `[children]` indexer, which hands back a plain `Component`.
- The runtime runs the CLOSEST handler of an event and no ancestor's: a cross inside a trigger needs no
  stop-propagation, and a click on a child with no handler is the trigger's.
- **A popup `parity.mjs` itself opens**: `data-parity-open` on what a reader presses (`="hover"` to hover it).
  `lib.mjs` presses its centre with a real pointer while that example is measured, then Escape.
  `parity-date.mjs` marks Flux's pickers that way and files the result as the page `<slug>-open`; the Rask page
  of that name (`DatePickerOpenParity`, `TimePickerOpenParity`) carries the same mark. It is NOT an `open.mjs`
  config: that module compares a list of rows under a `:popover-open` popup with no pinned clock, and Flux's
  date popup is a `<dialog open>` whose calendar opens on today. A popover the kit anchors (`position: fixed`,
  the gap a margin) where Flux places its popup by script (`position: absolute`, insets) is held to where it
  LANDS: for exactly that pair `position` and the four margins are not compared (`placed` in `parity.mjs`).
- **Never drawn, never collected** (`lib.mjs`): a `<template>` and what is inside one (Flux keeps one per day of
  a calendar — they used up the 500 nodes an example is measured to), and an `<input type="hidden">` (the one
  bound field of a `data-rask-segments` group).
- **Line height is copied as a RATIO** of the font size, as the docs page states it, so a node that sets its own
  font size and no line height (the 9px number in the calendar's today icon) computes as Flux's does.
- **Two clocks**: the measuring browser is pinned to 2026-01-15, and Flux's SERVER writes `now()` from the real
  date. A parity example pins the first through the internal `IUiClock` (`.On(FluxClock.Today)`), takes the
  second from `FluxClock.Now` / `FluxClock.Day(n)`, and states `.Locale("en-US")`, the browser's.
- **The page** is the kit's sheet, then a preflight-like reset in `@layer base`. Nothing of Flux's docs
  page is hard-coded in it.
- **Inherited context** (ink, font, size, weight, line height, letter spacing) is copied from each
  example's Flux twin onto the Rask wrapper before measuring (`measurePage`'s `prepare`). Never write
  `line-height:26px` or `color:#000` into a parity class.
- **Pairing** is by marker NAME, in document order. A part the page documents (from `flux.snapshot.json`,
  or `<slug>-*`) must be there node for node. A marker from another page with NO marked node on the Rask
  side is printed as a note and not compared. `MISMARKED` corrects a root Flux marks oddly.
- **Stand-ins** for a neighbour not rebuilt yet, on the Rask node: `data-parity-skip` (held to its place
  and size, inside not compared — give it the exact measured box, to 1/64px); `="self"` (its own look is
  another component's, children compared); `="width"`/`"height"` (that dimension is `rand()` on Flux's
  page, here and below). When the real component lands, the stand-in goes: Heading, Text, Field, Label,
  Description, Icon, Separator, Table, Card, Callout, Button, Badge, Tooltip, Dropdown, Menu, Context are
  real now — use them.
- **A real component inside a wrapper that is not rebuilt** (Flux's button sits in a `<ui-dropdown>`):
  write the wrapper as a `<div data-ui-dropdown data-parity-skip="self">` holding the real component and a
  `display:none` `data-parity-skip` box per hidden sibling (the menu). Without the marked wrapper the tool
  sees Flux's button under a marked ancestor and yours at the top, and the counts differ. See
  `TableParity.RowMenu`. A tooltip around a button is the real `Ui.Tooltip` now (`SeparatorParity.Bar`).
- **A colour the docs page hands a component by class** and that must lose to the component's own hover
  (`text-zinc-300` on the header's subtle button): `.parity-x:not(:hover){…}` on the page, and
  `not-hover:text-zinc-300` in an app.
- **A root Flux leaves unmarked** (`<ui-chart>` carries no `data-flux-*`) is named in `MISMARKED` by its tag.
  A `<template>` is no child on either side (Flux keeps a prototype of every chart node in one). A node in
  `EXTRA` (`data-ui-chart-hover`) is what the kit adds to do WITHOUT script what Flux does with it: not paired.
- **Not differences:** `NATIVE` tag pairs (and `button`→`summary`; `ui-button`, Flux's pressable that is not a
  `<button>`, pairs with one); the colour of a border 0px wide on
  both sides, or of an outline with `outline-style:none` on both, at rest and in a forced state; the
  offset of a node with no box (`display:none` itself or above it, or 0×0 on both sides); `oklch(… none)`
  ≡ `oklch(… 0)`; a forced state where only one side measured the node (60 per example), or on a node
  displayed on neither side (the rows of a closed menu: Flux lights them by script, the kit by `:hover`).
- **Animations:** clock-driven CSS animations are paused at their first frame and their DEFINITION (name
  with `flux-`≡`ui-`, duration, delay, iterations, direction, fill, keyframes with easing) is recorded per
  node and compared. Scroll-driven ones are neither paused nor recorded: the kit uses one where Flux runs
  script. After any change to `lib.mjs`, cached Flux measurements are stale — `parity.mjs <slug> --refresh`.
- **A state the page does not load in** (a shown toast) gets its own script beside `parity.mjs` and a
  pseudo-page: `node scripts/flux/parity-toast.mjs` raises each toast on Flux's page, measures it with
  `lib.mjs`, compares through `parity.mjs` under the name `toast-shown` (`ToastShownParity`) and adds a
  where-on-screen check (38 + 38). Its NATIVE pairs (`ui-toast`, `ui-toast-group`, `ui-close` → `div`) stay
  local to it. It ends with the COUNTDOWN, timed on both pages (`ToastTimedParity`, page `toast-timed`): a
  hovered stack holds every toast, each resumes its remainder, focus holds nothing.
  `node scripts/flux/parity-tooltip.mjs` does the same for the tooltip (`TooltipShownParity`,
  page `tooltip-shown`): 36 cases per scheme — box, styles and the ARIA each side writes — then a
  pointer-and-keyboard walk in which every step must agree (one, `OPEN`, is the runtime hook's to change).
  **A page that is walked gets the runtime**: `runtime.mjs` bundles the hook modules from
  `src/Rask.Core/Resources` with the build's cached esbuild and adds them to the static page, because what
  Flux does in script the kit asks the runtime for. Both scripts wait on STATE (`:popover-open`, the same box
  for three frames, every declared font face loaded), never on a delay: the tooltip script used to fail one
  case in a batch when the medium Inter face, first needed by the first tooltip shown, was still loading.
  In `parity.mjs` itself `ui-tooltip` and `ui-dropdown` (what Flux renders a TOGGLEABLE tooltip as,
  under the tooltip's marker) pair with `div`, and so does the menu family (`ui-context`, `ui-menu`,
  `ui-submenu`, `ui-menu-radio-group`, `ui-menu-checkbox-group` → `div`; `ui-menu-radio`, `ui-menu-checkbox` →
  `button`). `toast-shown` is no Flux slug: nothing that walks Flux's pages may assume a parity page is one.
  `ui-modal` and `ui-close` (the wrapper of a modal, and of a button that closes it) pair with `div` too, and a
  root that is `display: contents` on both sides (a modal's trigger) anchors no offsets: it has no box.
- **A component that is USED rather than shown** (the editor) is proved by a transcript: `node
  scripts/flux/parity-editor.mjs` drives ONE scenario with real keys and a real pointer on Flux's live page and
  on the Rask parity page (the kit's engine mounted on it), each step writing down what it observes — the
  value, the events, a control's states, where a popover opened, what has focus, how every kind of node
  computes — and the two transcripts must be equal line for line, in light and dark. Both pages settle a
  frame or two after a click, so the script waits; a single differing line on one run that is gone on the
  next is that, not the component (its waits are delays, not states — three runs in a row agreed on merging;
  make them state-based the day one does not). In `parity.mjs` the editor's own elements (`ui-editor`,
  `ui-toolbar`, `ui-editor-content`, and `ui-menu` for the stand-in) pair with `div` beside the select's, and
  a `<path>` outside an `<svg>` is no node, as a `<template>` is none.
- **Public API:** `python3 scripts/public-api/record.py src/Rask.Ui` builds and applies RS0016/RS0017 to
  both baselines (run it twice: a step exists only once its property compiles). It is the only such script.

### The chart is measured its own way (`scripts/flux/parity-chart.mjs`)
Flux's docs make chart data up on EVERY request (random values, dates counted back from now), and lay a chart
out around tick labels measured before Inter may have loaded. So **never `parity.mjs chart --refresh`**:
`node scripts/flux/parity-chart.mjs --measure` pins one load (the document is replayed for both schemes, the
charts are redrawn once the fonts are in), writes the rows each chart drew to
`tests/Rask.Ui.Tests/Flux/Parity/ChartParity.data.json` (commit it with the change) and the measurement where
`parity.mjs chart` caches it. Then `parity.mjs chart` for boxes and styles and `parity-chart.mjs` for geometry:
every number of every `d`, `x1…y2`, `cx/cy/r` and `translate()`, to 0.05px. Flux's rules were found by handing
its live `<ui-chart>` a dataset (`element.value = rows`) and reading back what it drew — the way to answer any
new question about its layout. `scripts/flux/inter-metrics.mjs` regenerates the label-width table.
The pin itself lives under `artifacts/` (ignored), so a checkout that did not make it has only the committed
rows and BOTH chart scripts fail on numbers alone. To prove the chart there: `--measure`, rewrite the pages,
run both, then `git checkout -- …/ChartParity.data.json` unless the chart itself changed — committing a new pin
breaks the replay of whoever holds the old one.

## 4. Replace what it supersedes
One commit per component, the solution building throughout:
- delete the daisy-drawn component(s) it replaces and their `UiClassNames` entries and tests;
- convert every call site — `src/Rask.Site`, `src/Rask.Dashboard`, `src/Rask.DevTools`,
  `src/Rask.Site.DataDemo`, `src/Rask.Templates`, `tests/**`, `docs/**`, `README.md`, `llms.txt`;
- showcase — REQUIRED, two agents skipped it: the component's demo on the site shows every Flux example
  (`src/Rask.Site/Features/UiKit`), with its E2E; `docs/ui-kit.md`; CHANGELOG `[Unreleased]` (BREAKING where
  a name or value changed). A demo that changes structure needs the golden regenerated:
  `RASK_UPDATE_GOLDEN=1 dotnet test tests/Rask.Site.Tests --filter FullyQualifiedName~DemoMarkupGolden`;
- `src/Rask.Ui/PublicAPI/*/PublicAPI.Unshipped.txt` (both TFMs): `scripts/public-api/record.py src/Rask.Ui`.
- A bare word in the kit's SOURCES that is a daisyUI class (`divider`, `alert`, `badge`) — in a comment
  too — makes Tailwind emit that class again. Reword it.

Then the `rask-ship` gate and `land-on-main`. Screenshot the showcase demo in light and dark
(`run-rask`) before landing.

When the daily run reports that Flux moved: `flux.lock.json` is CI's, measured on its Linux runner, so
a local `node scripts/flux/sync.mjs <slug>` measures for `parity.mjs` but compares nothing. Match the
component, land it, then relock: `gh workflow run upstream.yml -f relock=true`.

## Leftovers, each to delete with what it waits for
- `UiStyles.Card` points at the default Flux surface and is still used by `UiStat`, HttpFetchDemo, GuideCards
  and PropsIdClassStyleDemo: goes with the old chrome.
- `Ui.ThemeName` / `UiTheme` / `Ui.Shell.Theme` remain for the Dashboard's light pin and three DevTools test
  apps, and go with daisyUI; then `.dark` must set `color-scheme: dark` in `ui.css`, and the OS-preference
  arm of `@custom-variant dark` goes too. `AppearanceToggle` is `Ui.Button.Subtle` with the button's own
  `Tooltip` / `TooltipKbd` / `TooltipPosition`, as Flux's header is.
- Button: `UiClassNames.ButtonTone/ButtonVariant/ButtonSize` and the `.btn-*`, `.btn > svg`,
  `.btn[data-loading]` CSS stay until Popover, Fab, DayGrid, Filter, MultiSelect, Profile,
  Pagination, Sidebar and the templates stop writing raw `btn`. `Disabled`, `Command`/`CommandFor` are the
  `<button>`'s own attributes, kept as such, not Flux props. The button's tooltip is the real `Ui.Tooltip`
  (measured where Flux's pages use the prop: the separator page's moon, which `SeparatorParity` now writes
  with it, and the docs header). Measured and NOT as the reference reads: `kbd` alone is no tooltip — Flux
  draws it inside the button, 12px zinc-400 after the label (the popover page's Cancel `esc`). Unmeasured,
  no live example: where `kbd` sits against `icon:trailing` (written before it), and `tooltip:kbd` without
  `tooltip` (shows nothing). A grouped button reaches Flux's group THROUGH its tooltip (measured by wrapping
  the live groups' buttons): `UiButtonClasses.GroupedInTooltip`, added only to a button that has a tooltip or
  that a `Ui.Tooltip` flagged (`InTooltip`), so a plain button's class list does not grow. Flux leaks its
  Blade props onto the element (`tooltip="…"`, `tooltip:kbd="D"`); the kit does not write them.
- The `.alert-*` contrast corrections stay in `ui.css` for the templates' hand-written `alert alert-*`.
- The Dashboard's queue tiles lost their icon and hover (Flux's card has no link or icon props, and the
  Dashboard may not write classes): rebuilt on Flux pieces later.
- Chart (pointer and size wired 2026-10-09; `parity-chart.mjs --pointer` walks both pages). Measured on Flux and
  built: the active region is the PLOT box; the tooltip sits 15px from the row and the pointer and flips at the
  DRAWING's right / bottom edge (not the chart root's — the slice-colours example has a legend beside it); Flux
  measures the tooltip BEFORE writing the new row's text, so the hook does too; an area cursor covers the row's
  whole band, whatever the bar's `width`; a heading with no `format` shows the field as the row holds it (a
  string in Flux's rows — the parity page reads the same string; a C# date reads as the reader's short date).
  Flux redraws in the frame its box changes; the kit after 100 ms of no change (a round trip per frame per
  chart on a Server page otherwise), scaled by its `viewBox` meanwhile. `Ui.ChartSvg.Width/Height` are the box
  drawn for BEFORE the browser has measured — a `Translations` row each; whether they stay is the owner's call (asked 2026-10-09).
  The area cursor is a dashed CSS border at 10% where Flux strokes a path `4,4`. A pie's default hues go in
  palette order; Flux hashes the slice's id, by a rule not derived.
  Not built for want of an example to measure: `scale` on an axis, `tick-start="min"` / `tick-end="max"`, an X
  axis on top, the look of `axis.mark` and `zero-line` (drawn, unverified), smooth curves on a horizontal chart.
- Modal: `Ui.Modal`, `Ui.ModalTrigger`, `Ui.ModalClose`; `Ui.Drawer` and the daisy modal are gone (the command
  palette still draws daisyUI's `dialog.modal`, and keeps the one `:root:has()` scroll-lock rule in `ui.css`).
  ONE dialog for both paths, always with an id (`Name`, or a generated `ui-modal-<n>`): its own buttons and a
  `Ui.ModalClose` are `command="close"` invokers, so a state-driven modal is closed IN THE BROWSER and `OnClose`
  (the dialog's `close` event) is how the page catches up — a page that does not clear its field there goes on
  saying "true", and the hook only acts on a change. `Open` unset and no `Name` renders `"true"`. `scroll="body"`
  keeps an inner button (`command="request-close"`: `cancel`, then `close`) behind the panel, because the dialog
  fills the viewport and nothing is outside its box; Flux's page shows no `scroll="body"`, `bare`, `left` or
  `bottom` example, so those are built on the Reference and the measured variants, unmeasured. The corner button
  is the kit's own `Ui.Button.Subtle.Sm` with Flux's lighter resting colour by `!` utilities (the one way to say
  it over the button's own). Measured 2026-10-09, when a hugging leave dialog was taken for a kit bug: a bare
  `flux:modal` HUGS its content too (min 20rem, max 36rem, both in `:where()`; the Site's sheet computes the
  same), the corner button is 32px at 16/16 at every width, and Flux's heading keeps NO room for it — a first
  line that fills the panel ends 24px under the button on Flux's own page. Flux's docs look right because
  their headings are short and the sentence is a `flux:text`; the confirmation's `min-w-[22rem]` is the
  example's class, not a default. So `Ui.ConfirmLeave`'s `min-w-[22rem]` is Flux's and its `pe-8` is its own
  (its heading is a whole question); neither belongs in `Ui.Modal`. The focus placeholder stays (`autofocus` + the `ui-modal-placeholder` keyframe):
  the hook does not do it. Not written, as Flux writes none: `closedby`, `popover`, `aria-modal`, a label.
  Parity stand-ins: Flux's spacer (the kit's `Ui.Spacer` carries no `data-ui-spacer`) and the subheading of
  the floating example. The Dashboard's queue sheet writes two layout
  classes (`DashboardIsKitOnlyTests.Allowed`), compiled by its own sheet.
- Parity stand-ins still standing: none on the chart page; card page (fields, the heading/text lines whose variant was
  not looked up — its switches are the real `Ui.Switch` now), table page (avatar, the dropdown and menu around the row button — its pager is the real `Ui.Pagination` now), timeline page (the avatars, the reaction button and the composer of the comment thread), progress page
  (none: its slider is the real `Ui.Slider` now), dropdown page (the profile trigger and the two icon-only triggers,
  whose icon the snippets do not name). The field page's inputs and select and the input page's buttons are real
  now; the input page's `flux:select` inside a group is still a stand-in.
- Dropdown / Menu / Context (merged 2026-10-07). Made Flux's on merging: the trigger says
  `aria-haspopup="true"` whatever it opens; a submenu's row has NO `aria-haspopup`/`aria-expanded`/
  `aria-controls` and its flyout no `aria-label`/`id` (the runtime tells such a row by the `role="menu"` right
  after it); a leading icon and a check carry `data-ui-menu-item-icon`; a separator's line carries
  `data-ui-separator` and a group's lines `data-ui-menu-separator` beside `-top`/`-bottom`; the kit's own
  `data-ui-menu-indent` is `data-indent`; `Ui.MenuCheckboxGroup.KeepOpen` is gone. `Open`/`OnToggle`
  (dropdown, context) stand for `wire:model`, `Bind`/`Value`/… for a checkbox's or radio group's, `OnClick`
  for `wire:click`, `Ui.MenuItem.Href` for the `href` Flux's own demo sets on a menu item (`fluxui.dev/demo/qa.md`: `<flux:menu.item
  href="/settings/profile">`, live an `<a role="menuitem">`; the reference omits it) — each a `Translations` row.
  Not Flux's yet: `data-open` on the OPEN menu (Flux writes it on `ui-menu`; the kit writes it on the dropdown
  and the trigger only); Flux's reflected element props (`position`, `gap`, `checked`, `keep-open` as plain
  attributes) are not written; the popup is `popover="auto"` + `autofocus` + `popovertarget` where Flux's is
  `popover="manual"` and script. `Ui.MenuGroup`, `Ui.Navmenu`, `Ui.NavmenuItem` are on Flux's page but not in
  `flux.snapshot.json`, so the conformance gate does not see them. `Ui.Profile` is still the daisy-drawn row
  around the new dropdown (its `Open`/`OnToggle`/`KeepOpen` forward), until the navigation branch brings
  Flux's. `Ui.Command` draws its rows the old way through `UiCommandRows` until it is rebuilt.
  The generic hooks in the table above EXIST and the menu components do NOT write them yet: no
  `data-rask-menu-pointer` (a row is lit by `:hover` AND `data-active`, so two can be lit at once), no
  `data-rask-safe-area` (the safe triangle is the CSS wedge in `ui.css`), no `data-rask-lock` (the page lock is the `:root:has(…)`
  rule in `ui.css`, which leaves out a menu under a `[data-rask-hover]` root: a locked page takes the pointer
  from the trigger and the menu shuts under it). `Ui.Dropdown.Hover()` is Flux's `hover` and writes
  `data-rask-hover` (2026-10-08). The menu block in `rask-dom.ts` stays beside them: its focusout / toggle handlers are
  idempotent with the hooks'. Wiring them is the next step, and then that CSS and the block's overlap go.
  The context hook keeps the pointer's position in a constructable stylesheet (`:root{--rask-context-x/-y}`),
  not in `<html>`'s style attribute, which the WASM host's takeover morph of a prerendered page strips.
- Tooltip: daisyUI's own is kept out of the sheet by `exclude: … tooltip` on the `@plugin` line in `ui.css`
  (the bare word stands in the kit's comments; `UiTooltipTests` asserts the absence) — the way to drop a
  daisy component whose name the kit still has to say. A `Toggleable()` tooltip around something that is
  not a `<button>` puts `tabindex="0"` on the WRAPPER so a tap can open it by focus: not Flux's markup, it
  stands in for Flux's script and goes with a runtime hook that does not exist yet (below). Removed as non-Flux on merging: the
  wrapper's `role="group"` + `aria-describedby` around a trigger that is not one element (Flux wires only
  the trigger; nothing is wired there now). Added because Flux writes them: `aria-haspopup="true"` on a
  toggleable trigger, and `role="tooltip"` on an `Interactive()` tooltip's content.
- Kanban: Flux's page shows NO drag, drop or keyboard reordering (no `draggable`, no sort attribute, nothing
  moves under a pointer drag), so the kit has none and needs no hook. `flux:kanban.column.header`'s `badge`
  prop is `NotTranslated`: no example draws it. Unmeasured for the same reason, and built on the plain
  reading of the Reference: where a header's or card's children go, and a `Count` of 0 (drawn). Flux marks its
  div card `flux-kanban-card` (no `data-`) and its button card `data-flux-kanban-card`; the kit copies both
  (`ui-kanban-card`, `data-ui-kanban-card`). Parity stand-ins: the dropdown and menu in a column's actions, the
  avatars in a card's footer. The site's demo has two plain buttons where Flux has that dropdown.
- Input, select, autocomplete, pillbox — the hooks are wired (2026-10-07): `Clearable` is `data-rask-clear`,
  `Copyable` `data-rask-copy` (tick on `in-data-copied:`), `Mask` `data-rask-mask` (and still applied in C# to the
  value drawn and committed), the select's listbox button `data-rask-listbox-button`, every list popover
  `data-rask-lock`, the pillbox's trigger `data-rask-contain-keys` (`Enter Space ArrowUp ArrowDown` as a combobox,
  `Space ArrowUp ArrowDown` as the button over a search field). The select's search field and the pillbox's inline
  input say NO `aria-expanded`, as Flux's: they keep the list's keys by `data-rask-contain-keys` on the field
  itself (`UiListboxLook.ListKeys`; the pill input only while its list is open). Still open: `mask:dynamic` (the
  runtime has `data-rask-mask-money`; what a C# prop for an Alpine expression takes is the owner's call).
  `Ui.Input.Attributes(…)` forwards attributes to the `<input>` (how an unlabelled input gets `aria-label`); the
  typed `Min` / `Max` / `Step` / `MaxLength` / `Autofocus` / `Name` it also keeps are `Translations` rows — whether
  they should all go through `Attributes` instead is undecided.
- Pillbox: `Ui.PillboxTrigger.Clearable()`, a disabled pillbox and an invalid `Ui.PillboxInput` are drawn
  from the select's and the input's looks — no example on Flux's page shows them, so nothing measured them.
  A create row written before the options is DRAWN first and still comes last for the arrow keys.
- Checkbox, radio, switch (2026-10-07): every radio of every variant is a NATIVE `<input type="radio">` sharing the
  group's id as its `name`, so Flux's keys are the browser's and `data-rask-roving` is NOT written — recorded on
  Flux's page for the list, segmented, cards, pills and buttons alike: all four arrows move AND choose, wrapping;
  Home / End do nothing; Space chooses; Tab leaves the group. A checkbox flips on Space only (Enter does nothing, as
  Flux's); a switch on Space and Enter — the `<input role="switch">` is what asks the runtime for Enter. ARIA is
  Flux's live DOM and no more: an indeterminate checkbox (a check-all over some) is `data-indeterminate` on the
  root with NO `aria-checked="mixed"` (Flux says `aria-checked="false"` there, which is what a native unticked box
  says); groups say `role="group"` / `"radiogroup"` + `aria-labelledby`. `Attributes(…)` forwards to the `<input>`
  (`aria-label`, `required`); `Name` is a typed step again on the checkbox, radio, switch and radio group (the
  owner, 2026-10-08: a typed step for a forwarded NATIVE attribute is a translation, not an addition — a
  `Translations` row each, as `Ui.Input`'s `Min` / `Max` / `Step` / `Name`). Unmeasured, no example on Flux's pages: the INVALID look
  (the unticked box takes the input's red-500 border), a disabled switch, `Ui.Switch` with `checked` (the card
  page writes it; it is `Value(true)`). `flux:checkbox.indicator` is on Flux's page and not in its reference, so
  `Ui.CheckboxIndicator` is in no `Built` row. `flux:switch`'s `align` lists `right|start` and `left|end` as one
  option each: two enum members, two `NotTranslated` rows.
- Slider (2026-10-08): script-free but for `big-step`. Each thumb holds a native `<input type="range">` laid over
  the track it can reach (`NATIVE_CONTROL` in `parity.mjs`: tag and place compared, box and look the platform's),
  so drag, track press, arrows, Home / End are the browser's and `data-rask-drag` is NOT used. Measured on Flux's
  page: with `big-step="100"` Shift+Arrow and PageUp / PageDown move by 100; WITHOUT it Shift+Arrow moves one
  step and PageUp / PageDown are the browser's own tenth of the track (50 → 60 at 0–100 step 1; 500 → 600 at
  0–1000) — so `data-rask-big-step` is written ONLY when `BigStep` is set (`BigStep ?? Step` would turn PageUp
  into one step). A range thumb's input has its neighbour as its `min` / `max`, so its native PageUp is a tenth
  of what it can reach, where Flux's is a tenth of the whole track: unaligned without `BigStep`. No `Label`,
  `Description` or `Invalid` (Flux documents none and its examples use `flux:field`): every thumb in a field says
  `aria-labelledby`, as Flux's; a range's say `aria-valuetext="200 start range"`. Not written: Flux's root
  `tabindex="-1"` and its leaked `data-flux-aria-range-*`. `Disabled` is the input's own.
- OTP (2026-10-08): wired to `data-rask-otp` as the table says — cells with no `value`, no handler, no key; one
  `Input.Type(Hidden)` FIRST in the group (`parity.mjs`'s `BOUND` leaves it unpaired), bound on `OnInput`
  (what the hook's own tests bind) through `UiFormCommit`, and rendered back EXACTLY as the hook announced it
  (`UiOtp._typed`): an echo that differs by one character is taken for the page changing the code and written
  over the keys typed since. That state is why an unkeyed `Ui.Otp` beside a keyed one loses keys (below, "Key
  every sibling"): the site's demo did, and six fast keys reached C# as four. The
  proof: a 30-step walk (type, type-over, arrows, click past the end, Backspace, Delete, paste, six keys at
  CPU ×8 with no delay) on all seven non-submitting examples, Flux's page beside the static parity page with
  the runtime and beside the running WASM site — 189 of 210 steps agree, and the 21 are (3) and (4) below. What the hook
  does NOT do, measured on Flux, each a change to `rask-otp.ts`:
  (1) `submit="auto"` — Flux calls `requestSubmit()` on the enclosing form when the last cell is filled (one
  `submit` event, no submitter). Needs `data-rask-otp-submit` on the group: after a commit that leaves every
  cell filled, `group.closest('form')?.requestSubmit()`. `Ui.Otp.OnComplete` stands in; `flux:otp/submit` is
  `NotTranslated`.
  (2) Upper-casing — Flux's alpha and alphanumeric cells show and hold capitals; `otpChars` keeps the case typed.
  The kit upper-cases the VALUE in C# and the cells go on showing what was typed (a CSS `uppercase` would differ
  in `text-transform` on the parity page). Fix in `otpChars`, then `UiOtp` can bind the hidden field directly
  and `_typed` goes.
  (3) Roving `tabindex` — Flux moves `tabindex="0"` with focus, so Tab always leaves the group. The kit renders
  it on the first empty cell (right at rest and while typing forward); after ArrowLeft, Tab stops there once.
  (4) ArrowUp, ArrowDown and Home collapse the selection in Flux's cell (the browser's own); the hook cancels them.
  `Label` and `DescriptionTrailing` are from Flux's examples (rows in `Translations`); `Description`, `Invalid`
  and an `autofocus` are not Flux's and not built. `Name` is the hidden field's.
- Badge: `Ui.NavItem` / `Ui.NavTab` still take `BadgeTone` (`Ui.Tone`), mapped to a colour by
  `UiBadge.ToneColor`; both go with the old chrome. `Mono()` and the close button's default `aria-label` were
  removed as non-Flux: a long token says `.Class("font-mono max-w-full break-all whitespace-normal!")` —
  named in `UiBadge`'s remarks so the kit's sheet carries it for the Dashboard and DevTools, whose sources
  Tailwind never reads (`UiConsoleChromeTests` pins it; `DashboardIsKitOnlyTests` allows exactly that call
  until the Dashboard has a sheet of its own). daisyUI's `.badge` is STILL in the compiled sheet: the bare
  word stands in some sixty kit comments and identifiers (the last rule of section 4 was not applied) — reword them, then
  assert its absence in `UiStylesheetTests`.
- Editor: the one component with an ENGINE. `Resources/editor/ui-editor.ts` (Tiptap pinned in
  `package.json`, locked) is bundled by `Resources/editor/build.mjs` into the committed
  `Resources/ui-editor.js`; after changing either, run `npm ci && node build.mjs` there — it rewrites
  `UiEditorEngine.Version`, which `UiEditorTests` holds to the bundle's hash. There is NO switch, as Flux's has
  none: `build/Rask.Ui.targets` copies the file to `wwwroot/js` of every app that references the kit (a Web
  or WebAssembly SDK project; `RaskUiEditorEngine=false` opts one out, `=true` is for a host that is neither), and
  `UiEditor.ts` — the kit's only scoped script — imports it when an editor mounts. Tiptap is the release
  Flux's docs name; `prosemirror-model` is held at 1.25.1 because later ones re-serialise a `style`
  attribute with a trailing semicolon, which is not the HTML Flux answers. Stand-ins on its parity page:
  the `flux:dropdown` and `flux:menu` of "customization". Not measurable on Flux's page, so not proved:
  the LOOK of the `subscript` / `superscript` / `highlight` / `code` buttons (Lucide icons, no shortcut hint —
  what each DOES is in the transcript, by a control of that name made on both pages), the
  look of `Invalid` (`aria-invalid:border-red-500` is a guess), a `Ui.EditorButton` with text, and h4–h6.
  Its notices (`Resources/ui-editor.LICENSES.txt`, written by `build.mjs`; Tiptap's and Lucide's texts are
  kept in `Resources/editor/notices/`) ship in the package and are written beside the script in `wwwroot/js`.
  On the Server host it is pinned by `tests/Rask.Server.E2E.Tests/EditorOnServerTests.cs`. The `code` item is INLINE code:
  observed on Flux's live `<ui-editor>` (a control named `data-editor="code"` runs `toggleCode` and shows
  `aria-pressed` / `data-match`; the block is a separate control name, `code-block`), with the label "Code"
  and the `Ctrl`+`E` of its shortcut table — the reference's one line, "Code block formatting", says otherwise
  and no example renders the item, so what name the Blade item writes is the one thing not seen.
- Tabs (2026-10-08): `Ui.TabGroup` / `Ui.Tabs` / `Ui.Tab` / `Ui.TabPanel`. A tab is ALWAYS a `<button>` — Flux's
  tab has no `href` (its page: "for full-page navigation, use the navbar"), so `Ui.Tab.Href` was removed on
  merging and a row that navigates is `Ui.Tabs.Value(x).OnChange(v => Go.To(…))` (the console's log page). The
  panel's `pt-8` is a plain utility: Flux's own examples override it with `pt-6!`, and so does a call site.
  `ScrollableFade()` is the kit's one scroll-driven animation beside the table's (`.ui-tabs-fade`,
  `@property --ui-tabs-scrolled`), where Flux measures in script; the harness neither pauses nor records it.
  Keys recorded on Flux's live page, list by list, and equal on the parity page with the hook: all four arrows
  move AND select, wrapping, never scrolling; they pass over an `action` tab, which Tab reaches; Home, End,
  Enter and Space do nothing of the tabs' own. Unmeasured, no example on Flux's page: a DISABLED tab (drawn
  from the reference: 50% / 75% dark opacity, no pointer, skipped by the arrows), `accent="false"`,
  `icon:trailing`, `scrollable:scrollbar="hide"`, a tab's own `size`. Still daisyUI's `tabs` / `tab`, written
  by hand: the site's `CodeSample` file tabs and the landing page's hero / install tabs — neither is a `Ui.Tab`.
- Tooltip wrapper display: measured on Flux's live pages — a plain `flux:tooltip` writes NO display class and
  computes `inline-flex`, a button's own tooltip writes `inline-flex`, every toolbar tooltip of the editor
  writes `contents`. So the default is a rule (`[data-ui-tooltip]{display:inline-flex}` in `@layer rask`,
  below every utility), `Ui.Tooltip` writes only the call site's `Class`, and `Ui.Button` / `Ui.EditorButton`
  pass `inline-flex` as Flux does. No `[:where(&)]:` was needed, and none is to be added for this.

- Calendar, date picker, time picker (merged 2026-10-08). Flux's `mode` is the step that OPENS the control:
  `Ui.Calendar.Range` / `.Multiple` and `Ui.DatePicker.Range` are C# extension properties on the generated seed
  (`UiCalendarModes`, `UiDatePickerModes`) handing back the typed twin's seed, so a wrong pairing does not
  compile; `Mode(value)` and the bare `Multiple()` are checked against the bound type at render. The twins are
  reachable as entries too — `Ui.CalendarMultiple`, `Ui.CalendarRange`, `Ui.DatePickerRange` — because the
  generator cannot hide one: to remove when it can. Every hook is wired and was driven on the published site
  against Flux's pages (`UiKitDataInputTests`): the grid's keys and focus, the typed segments (the whole
  trigger is the `data-rask-toggle`; the runtime leaves a press in a field to the field), the roving presets
  (tab stop = the checked preset, or the FIRST while Custom is the checked one, with `data-active`, as Flux
  marks it), `data-rask-lock` on both popups, `data-rask-listbox-button` on the time button, arrows opening the
  closed date button (`data-rask-contain-keys="ArrowUp ArrowDown"` + `data-rask-popover-open`). A paging key
  names NO focus target for its one render and the control renders once more from `OnRendered`
  (`UiCalendarState.RenameAsync`) so the tab stop is named again — without that an arrow pressed after
  tabbing back in moved the mark and left the focus behind. Kept, not Flux's: the popup `<dialog>` carries
  `autofocus`, so an open date picker has the focus on the dialog where Flux leaves it on `<body>` (a
  `<dialog popover>` would otherwise hand it to its first control). Unmeasured, no example on Flux's pages:
  calendar sizes `Lg` / `Xxl`, every date-picker size but the default, `with-inputs`, `clearable` on the date
  picker, the SHOWN confirmation footer, the time picker's `Sm` / `Xs` / invalid / disabled / clearable looks.
  The today shortcut draws Heroicons' mini calendar where Flux draws a glyph of its own (1px taller window).
  ja-JP: Flux's button reads `2026年10月20日`, which is what the kit writes (measured; the earlier note of a
  `2026/01/20` difference did not reproduce). A typed part is EMPTY in server-rendered markup until the hooks
  run and fill it from the hidden field.

## Sidebar, header and navigation (merged 2026-10-08; the rail on the follow-up)
- **Two checkboxes, no script.** `Ui.Sidebar` holds `#sidebar-open` (slid over the page; `data-rask-uncheck-on-navigate`)
  and `#sidebar-rail` (narrowed; `data-rask-persist="flux-sidebar-collapsed-desktop"`), and every control is a
  `<label for>` with `role="button"`. The states are the variants `sidebar-desktop:` / `sidebar-rail:` /
  `sidebar-open:` (`ui.css`), which weigh more than a plain utility on purpose. `Ui.SidebarScript` in a WASM
  app's head restores the rail before first paint AND records a press made before the hook bundle has arrived.
- **`IUiTrigger`** (`UiInvoked`): a kit part that DRAWS the button a dropdown opens from (`Ui.Profile`,
  `Ui.SidebarProfile`, `Ui.NavbarItem`) is no element, so `Ui.Dropdown` tells it the panel id and it wires its
  own button (`UiInvoker.Decorate`). A dropdown a part owns outright takes the internal step
  `UiDropdown.OwnedBy(marker, hoverIf)` (the rail group's) and that part gives its button the dropdown's
  `AnchorName`, so the menu sits against the button and not the row around it.
- **The rail, measured on `fluxui.dev/demo/sidebar-collapsible`** and held by `node scripts/flux/rail.mjs`
  (36 steps, light and dark; then an application's menu, `long-menu.html` from `LayoutDemo.LongMenu`):
  every `Ui.SidebarItem` sits in a `Ui.Tooltip` (`right`, wrapper `block min-w-0`, `aria-describedby`) whose
  content OPENS whenever the pointer is there and is DRAWN only in the rail and never inside a menu
  (`UiSidebarItem.RailTooltip`); the collapse control's tooltip is its name (`aria-labelledby`) and shows at
  every width; the search button's shows its placeholder; the profile has none. The count is Flux's
  `data-flux-navlist-badge` span, not `flux:badge`. A group with an icon is, in the rail, Flux's
  `flux:dropdown position="right" align="start" hover` around a `flux:menu` whose one `menu.group` (heading =
  the group's) holds THE SAME items again, each `role="menuitem"`: `UiSidebarItem` reads `UiMenuLevel` from
  context and registers as a row, so the group places its children twice (once in the `<details>`, once in the
  menu) — the item's look in a menu is `in-data-ui-menu:` utilities, and every rail-only utility is
  `sidebar-rail:not-in-data-ui-menu:` because the menu is INSIDE the collapsed sidebar. Flux gives the rail
  button no accessible name (its heading span is `display:none`); the kit writes the same. Flux neither
  animates nor remembers a group's fold. Leaving the icon for anywhere but the menu closes it at once, on both
  (no delay, no safe area): move straight across the 5px gap.
- **Layout parity:** `parity.mjs layouts/<slug>` → `measureLayouts` (`lib.mjs`: `VIEWS` × states `rail` / `open`,
  put there by pressing `CONTROLS`), one document per demo from a `FluxLayoutParity`. `LayoutDemo.Utilities`
  are the demos' own classes, unlayered: never a bare `.bg-white{}` there — the kit's menu says `bg-white` and
  lost its dark ground to it. A demo the class yields that Flux has not (`long-menu`) is written and never
  compared.
- **Links:** only a generated route is a `NavLink`; a string `Href` is a plain `<a>` (#1070,
  `UiRoutePathBaseTests`). `Ui.NavmenuItem` without `Href` is a `<button>` (`OnClick`), its icon carries
  Flux's `data-navmenu-icon`. A menu's ink is stated (`text-black dark:text-white`), never inherited.
- **Removed as not Flux's:** `Ui.Navlist.Variant` (no page documents it; the profile page's outlined list is
  the docs' own decor and a box on `ProfileParity`), the `title` attributes, the `ui-rail-hide` classes.
- **Open:** the second press on a hover-opened rail button closes Flux's menu and not the kit's (`rask-hover.ts`
  swallows every such click); Enter on the rail button puts Flux's cursor on the first row, the kit's menu
  opens with none (ArrowDown takes it there); a hover-opened menu takes focus (`autofocus`), Flux's does not.
  `Ui.SidebarSearch` as a FIELD (`OnInput`) is the kit's translation — Flux's is only ever a button — drawn as
  Flux's filled input; in the rail it is unmeasured. A plain (non-expandable) group in the rail, a `current`
  item inside the rail menu and an item without an icon in the rail have no live example. Neither Flux nor
  the kit scrolls the current item into view, and the whole sidebar scrolls (header and profile with it): an
  app that wants them pinned gives `Ui.SidebarNav` `min-h-0 flex-1 overflow-y-auto`, as the Site does.
  `flux:navmenu.separator` and `flux:navmenu.item indent` (the profile page's first menu) are not built.
  `SiteHeader` is still a raw `<header>` (its own height, padding and z-order would each need a `!` over
  `Ui.Header`'s), and the templates still write daisyUI's `navbar`.

## Runtime hooks that exist (Flux does it in script; the component writes the attribute)
The kit ships no script but the editor's (`UiEditor.ts`, which only loads the engine). Rask's RUNTIME carries generic hooks keyed on attributes
(`src/Rask.Core/Resources/rask-hooks.ts`, one module per concern; `docs/js-interop-runtime.md#behaviour-hooks-data-rask-`
is the reference; `tests/Rask.Server.E2E.Tests/RuntimeHook*Tests.cs` pin each one to what Flux did). A
component reaches Flux's behaviour by writing exactly these — never by a handler that round-trips:

**The hooks are a bundle of their own, loaded on demand** (`rask-hooks.js`; `rask-hook-loader.ts` is the part
every page loads). A NEW hook therefore needs three things beyond its module: its import in `rask-hooks.ts`
(never in `rask.ts` / `rask.wasm.ts` — that puts it back in every app's download), its attribute in
`HOOK_ATTRIBUTES` (`rask-hook-loader.ts`) and in `HookBundleTag.Attributes` (`src/Rask.Server/Http`), and its
document listeners added with `listen(...)` from `rask-owned.ts`, not `document.addEventListener` — that is what
keeps a late-arriving hook's place ahead of the host's own click/change listener and replays what the reader did
while the bundle was on its way. `HookBundleContractTests` (Rask.Core.Tests) fails on the first two;
`RuntimeHookLoadingTests` (Rask.Server.E2E.Tests) proves the loading. An attribute read only on or inside an
element that already carries a listed one (`data-rask-segment`) is declared in that test instead.

| Component | Writes | Gets |
|---|---|---|
| Tooltip (and Button's `Tooltip`) | root `data-rask-tooltip="<bubble id>"`; bubble `popover="manual"`; `interactive`: `aria-expanded="false"` + `aria-controls` on the trigger | shown at 0 ms on pointer and keyboard focus, hidden on leave / blur / Escape / press, top layer for any trigger, `aria-expanded` mirrored |
| Dropdown / Popover `hover` | root `data-rask-hover="<panel id>"` (trigger keeps `popovertarget`); NO `data-rask-lock` on its panel (WIRED: `UiPopupHost.Hover`) | opens over trigger or panel, closes over neither, a press keeps it, Enter opens, no lock, and NO focus — a panel that carries `autofocus` does not take it when the pointer opens it (Flux's rail menu: `document.activeElement` stays where it was) |
| Sidebar rail item | the same + `data-rask-hover-if="<selector true while collapsed>"` | the menu opens on hover only while the rail is collapsed |
| Dropdown, Popover, Select, Context | panel `data-rask-lock` (WIRED on `Ui.Menu` and `Ui.Navmenu` — a dropdown's and a context menu's, measured locked on Flux — and on the select, autocomplete, date and time popups; NOT yet on `Ui.Popover`, whose Flux twin was measured locked too; never in the kit's stylesheet) | `<html>`: `overflow:hidden; pointer-events:none; scrollbar-gutter:stable` while open — the gutter only where a scrollbar was showing, so a page that shows none (every headless browser) is neither narrowed nor scrolled |
| any `popover="auto"` panel | nothing | Tab out closes it; a press outside hands focus to its `popovertarget` button |
| Menu (WIRED: `UiMenu`) | the outermost `[role=menu]` `data-rask-menu-cursor` + `data-rask-menu-pointer`, each flyout `data-rask-menu-pointer`; rows rendered with `tabindex="-1"`, NO `data-active`, NO key handler, NO pointer handler; no `:hover` highlight, only `[data-active]`. The cursor is NOT C# state: a hover or an arrow key must send the server nothing (`UiMenuHookTests` asserts zero WebSocket frames) | Flux's whole menu in the browser: one lit row and focus left where it was; the first arrow takes the row the pointer is on, later ones count from the lit row; ends, disabled rows, type-ahead; the keyboard's row stays lit when the pointer leaves, the pointer's does not |
| Menu submenu (WIRED: `UiMenuSubmenu`) | its row `data-rask-safe-area` (no value: the flyout is the next element, as Flux has it, with no id), no `OnClick`; the flyout rendered always and shown by the stylesheet from `[data-open]` on the wrapper, which the RUNTIME writes — never `:hover`, never a render | Flux's safe area; a flyout that opens under the pointer, stays when the pointer leaves the menu and closes when another row is entered; ArrowRight / Enter / Space / ArrowLeft; its submenu's row lit while the arrows are inside it; a tap toggles |
| Modal (state-driven) | `<dialog data-rask-modal-open="true|false">` instead of `open` | `showModal()`, `::backdrop`, closes when the state says so; `OnClose` / `OnCancel` still fire |
| Modal | `data-rask-modal="any|press|escape|none"` from `dismissible` × `escapable` (both → `any`, outside only → `press`, Escape only → `escape`, neither → `none`); `data-rask-lock="scroll"`; keep `command`/`commandfor` | the four dismissals, `cancel` for a press outside, `data-open` while shown, a fallback where invoker commands are missing, Flux's modal lock |
| Input `copyable` | button `data-rask-copy="<input id>"`; style the tick on `[data-copied]` | clipboard in the click, 2 s copied state |
| Input `clearable` | button `data-rask-clear="<input id>"` (no `OnClick`) | emptied, `input` fired, focus in the field |
| Input `mask` / `mask:dynamic="$money($input)"` | `data-rask-mask="<pattern>"` / `data-rask-mask-money` (`=".,2"`) | Flux's (Alpine's) shaping; any other `mask:dynamic` expression is NOT supported |
| Switch | `<input type="checkbox" role="switch">` | Enter toggles |
| Tabs | `role="tablist"` over `button[role=tab]`, roving `tabindex`, `aria-selected`; each tab's `OnClick` selects it — NO key handler in C# | the arrows move focus and select, past disabled tabs and around the ends; Home / End are the page's, as Flux's (`rask-tabs.ts`; the role alone loads the bundle, like the switch's) |
| Slider | `data-rask-big-step="<BigStep>"` on each thumb's `<input type="range">`, only when `BigStep` is set (Flux without `big-step`: Shift changes nothing, the Page keys are the browser's) | Shift+Arrow, PageUp / PageDown |
| Select (listbox button) | `data-rask-listbox-button` on the closed `button[role=combobox]` | Enter does nothing, the arrows do not scroll (open the list from the C# key handler: Flux opens on ArrowUp / ArrowDown / Space) |
| Calendar | grid `data-rask-contain-keys="Arrows Home End PageUp PageDown"` (NOT Space — Flux lets it scroll); the calendar root `data-rask-focus-follows`, the day that is the tab stop `data-rask-focus-target` + `tabindex="0"`; key the day cells by DATE. On Home / End / PageUp / PageDown render NO `data-rask-focus-target` for that render (or let the keyed day leave) | arrows never scroll the page; focus lands on the new day after the morph, across a month change too; after Home / End / Page keys focus is on `<body>`, as Flux |
| Color picker | area and tracks `data-rask-contain-keys="Arrows Home End PageUp PageDown"` on the `[role=slider]`; swatch `[role=listbox]` the same list plus `Space Enter`; the area `data-rask-press-keeps-focus` | keys kept; a press on the area leaves focus where it was |
| Pillbox trigger | `[role=combobox]`: `data-rask-contain-keys="Enter Space ArrowUp ArrowDown"`; the `[role=button]` variant: `"Space ArrowUp ArrowDown"` (Flux lets Enter through there) | the trigger's keys do not scroll or press |
| Select / Time picker list | `aria-activedescendant` on the `[role=combobox]` or `[role=listbox]` (as today) | the active option scrolls into view inside the list only |
| Date picker presets | `[role=radiogroup]` `data-rask-roving`; each preset `[role=radio]` button with `OnClick` selecting it, `tabindex` 0 on the checked one | arrows walk and select, wrapping |
| Tooltip `Toggleable()` | non-button trigger wrapper: `data-rask-toggle="<bubble id>"` + `aria-expanded="false"` + `tabindex="0"`, bubble `popover="manual"`; a `<button>` trigger: `popovertarget` + `aria-expanded="false"`, bubble `popover` | Flux's toggleable tooltip: click / Enter / Space toggle, Escape / outside press / Tab away close; `aria-expanded` mirrored |
| any popover invoker | `aria-expanded="false"` beside `popovertarget` / `commandfor` / `data-rask-toggle` | mirrored from the popover's `toggle` event; never added for you |
| Color picker area / hue / alpha, a custom slider | surface `data-rask-drag="x y"` (tracks: `"x"`), `data-rask-drag-inset="<half the thumb>"` when Flux keeps the thumb inside, `touch-action:none`, ONE `Input.Type(Hidden)` inside bound with `OnInput` (live) / `OnChange` (settled) to `"x y"` fractions; thumb CSS from `--rask-drag-x` / `--rask-drag-y`; NO pointer handlers in C# | the thumb under the pointer every frame; one event per frame and one on release |
| Color picker eyedropper | button rendered `hidden` with `data-rask-requires="EyeDropper"` | shown only where the API exists (Flux hides it, it does not disable it) |
| File upload / dropzone | `data-rask-loading` on the element around the `Input.Type(File).OnFiles(…)` (WIRED: `Ui.FileUpload`'s root, which is where Flux writes `data-loading`); CSS from `in-data-loading:`, `width: var(--rask-progress)` and `content: var(--rask-progress-as-string)` — the kit hands the pair on as `--ui-file-upload-progress*` in `ui.css` | Flux's `data-loading` + percent pair; real upload progress on Server, bytes read on WASM |
| Date picker `type="input"`, Time picker typed trigger | group `data-rask-segments`; each part `Input.Of<string>()` with `data-rask-segment="month|day|year|hour|minute|meridiem"`, a `placeholder`, NO `value`, NO handler, in the locale's order; ONE `Input.Type(Hidden)` inside bound to `yyyy-mm-dd` / `HH:mm` / both joined by `T` | every key of Flux's typed date and time; one committed value. NOT covered: keeping a click on a part from reaching a handler on the surrounding trigger — put the parts beside the element that opens the popover, not inside it |
| Chart (WIRED 2026-10-09) | root `data-rask-plot` (bare); over the drawing ONE layer that is `data-rask-measure` (its bound `Input.Type(Hidden)` rendered with the size DRAWN, `"<w> <h>"`) and `data-rask-plot-frame`, holding the plot box `data-rask-plot-area="<each row's place, 0–1>"` (`data-rask-plot-axis="y"` on a horizontal chart) with ONE cursor placed by `--rask-plot-at`; every part that reads a row rendered once with `data-rask-plot-text` (a line per row); points, slices and a pie's per-slice dots `data-rask-plot-row="<i>"`; tooltip `data-rask-plot-tooltip="15"`, `opacity-0 data-active:opacity-100` | active row, cursor, tooltip, summary and a pie's slice follow the pointer with no round trip; the chart is drawn again at its own box, once per settled size |
| OTP | group `data-rask-otp` (`="alpha"`, `="alphanumeric"`); cells rendered with NO `value`, NO handler, NO re-keying; ONE `Input.Type(Hidden)` inside, bound to the string | every key of Flux's otp input, fast typing included |
| Toast | `data-rask-dismiss-hold="pointer"` beside `data-rask-dismiss-after` | focus no longer holds the countdown |
| ToastGroup | `data-rask-dismiss-scope` on the group (wired). `data-rask-stack` on the parent of the stacked toasts, newest LAST; no rendered `--ui-toast-index` / anchor names in `style`; CSS from `--rask-stack-index` / `-height` / `-offset` / `-front`, the rule that cuts the card written under `[data-rask-stack]:not([data-rask-measuring])` (hook ready since round two, NOT wired — see below) | one pointer holds them all; the 350 ms glide |
| Sidebar | collapse checkbox `data-rask-persist="flux-sidebar-collapsed-desktop"` (plus a head script for a WASM cold load); mobile checkbox `data-rask-uncheck-on-navigate` | state kept across visits; drawer closed on navigation |
| Carousel | `data-rask-carousel`, `data-rask-carousel-track`, `data-rask-carousel-indicators`, `data-rask-carousel-controls` beside the `data-ui-*` markers; `data-direction`, `data-name`, `data-advance`, `data-wrap`, `data-scroll`, `data-autoplay` as today | position flags, arrows, indicators, autoplay |
| Pagination `scroll-to` | root `data-rask-scroll-to="<selector>"` (`rask-scroll.ts`) | a press on a button or link inside brings the selector's element into view; the press still runs |

Measured, and NOT built because Flux does not do it: a toast's countdown does not RESTART under the pointer — it
resumes the remainder (shown 1000 ms, hovered 3000 ms, gone 4359 ms after the pointer left; 5390 would be a
restart). The earlier note here said otherwise.

Known and open: two toasts whose countdowns end in the same frame press two dismiss buttons at once, and the
second press can carry a handler id the first render retired — give stacked toasts distinct durations or key
the handler. The built-in toast shows one at a time, as Flux's.

Wired (2026-10-07): `Ui.Modal` writes `data-rask-modal` (all four values), `data-rask-lock="scroll"` and, with
`Open` or without a `Name`, `data-rask-modal-open`; its trigger and close buttons are `command` / `commandfor`
alone. `parity-modal.mjs` holds every step of both paths to Flux's page, and `UiModalHookTests`
(`tests/Rask.Server.E2E.Tests`) drives the component on a Server host. `Ui.Tooltip` writes `data-rask-tooltip` and an `Interactive()` trigger's
`aria-expanded="false"`; the interest invoker and the `:hover` CSS are gone (`:hover` survives only under
`@media (scripting: none)`). `Ui.Toast` writes `data-rask-dismiss-hold="pointer"`, `Ui.ToastGroup`
`data-rask-dismiss-scope`, and `ui.css` pauses the fade of every toast of a hovered group (a held toast that
faded would stay, unseen). What is NOT wired, each with what it needs:

- **`Toggleable()`** (both hooks below exist since round two — `data-rask-toggle` and the `aria-expanded`
  mirror; the component is still to be converted) — measured on Flux's `info` example (it renders a `ui-dropdown`): a hover does nothing, a
  click opens, it stays when the pointer leaves and on a click inside, a second click / Escape / a click
  outside close it, Enter opens. That is `popover="auto"` + `popovertarget` exactly, which is what a
  `<button>` trigger gets. Missing hooks: (a) one that toggles a popover from an element that is NOT a button
  (`data-rask-toggle="<id>"` on the wrapper: click and Enter/Space on its first child) — until then the
  wrapper's `tabindex` stand-in stays; (b) one that mirrors `aria-expanded` on a `popovertarget` button from
  its popover's `toggle` event — Flux writes `aria-expanded` there, the kit leaves it to the browser because a
  written one would never change.
- **`Interactive()` and a focus dropped to nothing** (ALIGNED in round two: `rask-hover.ts` now keeps a tooltip
  whose trigger carries `aria-expanded` open on a focus that goes to nothing, until a press outside) — Flux keeps an interactive tooltip open when its trigger
  loses focus with no next element (`blur()`, the window), until a press outside; `rask-hover.ts` closes on
  any `focusout` that leaves the wrapper. Tab into the content and out of it agree. `parity-tooltip.mjs`
  prints it as `OPEN`.
- **`data-rask-stack` (the 350 ms glide)** — NOT wired, and cannot be from the kit. Measured on Flux: every
  toast is `position:absolute; bottom:0`; in the deck each toast's CARD is given the FRONT toast's height
  (50px, `overflow:hidden`, so the dialog is 62px whatever its own height) and the dialog
  `scaleX(1 − .05·min(i,2)) translateY(−10px·i)`; laid out, the card gets its own height back and the dialog
  `translateY(−Σ heights in front)`; opacity, transform and height all transition over 0.35s ease. The hook
  measures each child's `offsetHeight` when the child list changes — and in the deck that IS the cut height,
  so `--rask-stack-height` / `-offset` come out as multiples of the front toast's height and a fan-out built
  on them is wrong as soon as two toasts differ in height (`group-deck` in `parity-toast.mjs` is that case).
  Cutting the card instead of the dialog needs the front toast's height inside every toast, which the three
  variables do not carry (only index 1 has it, as its offset) and anchors cannot bring (an anchor is not
  visible from inside a transformed toast, and `anchor-size()` is for the positioned box itself). The hook has
  to (1) measure the natural height — the toast's first child's `scrollHeight` plus the toast's own padding —
  and (2) write `--rask-stack-front`, the front child's. Then: newest LAST in the tree, drop the rendered
  `--ui-toast-index` / anchor names from `style` (the hook holds `style` against the morph), and the CSS is
  Flux's four lines. Until then the stack stays on CSS anchors, newest first: right in both states, and it
  snaps between them.
  **Round two built both**: the hook measures with `data-rask-measuring` on the stack (write the cutting rule
  as `[data-rask-stack]:not([data-rask-measuring]):not(:hover) > * > .card { height: var(--rask-stack-front) }`)
  and writes `--rask-stack-front` on every child. The component is still to be converted.

- Pagination (2026-10-08): `UiPaginator` (`Page`, `PerPage`, `Total`, `HasMore`) stands in for the Laravel paginator
  Flux's `paginator` prop is handed. ARIA is Flux's live DOM and differs between the two forms: a COUNTED pager
  names its steps `aria-label="&laquo; Previous"` / `"Next &raquo;"` — the entity unresolved, which is what Flux's
  DOM says — and a spent one is a `<div aria-disabled="true">`; the SIMPLE pager writes no `aria-label` and its
  spent step is a bare `<div>`. The numbers show once the pager's OWN box is 640px (`@container`), which Flux's
  606px previews never reach, so `parity.mjs pagination` compares the narrow form and `UiPaginationTests` holds
  the numbered one. `PaginationParity` writes "1 to 75 of" over the large example's summary: Flux's demo data
  is not a list a paginator could describe. Flux's "Scroll to top" section renders no example, so `ScrollTo`
  (`data-rask-scroll-to`) is built on the Reference and pinned by `RuntimeHookScrollTests`.
- Timeline (2026-10-08): the track is CSS in `ui.css` (one grid, a subgrid per item); daisyUI's component of
  that name is kept out of the sheet by `exclude: … timeline` on the `@plugin` line, as the tooltip's is.
  `Ui.Color`'s five greys are `Translations` rows on the indicator and draw the plain one.

## One stylesheet per app (merged 2026-10-07)
- `Styles/ui.css` is the kit as Tailwind SOURCE (theme, `dark` variant, daisyUI, the `@layer rask` blocks):
  no entry point, no layer order. Two entries import it: `ui.precompiled.css` (the embedded
  `UiStylesheet.Css` — what `FluxParityPages`, DevTools and a Tailwind-less app use) and `rask-ui.css` (what
  an app's `Styles/app.css` imports as `./vendor/rask-ui.css`; the build writes `Styles/vendor/`). A CSS rule
  for a component still goes in `ui.css`, and reaches both.
- An app's sheet gets the kit's utilities from `rask-ui.classes.txt`, which the kit's build lists from its
  own COMPILED sheet. So a class the precompiled sheet lacks is missing from every app too: a class named
  only in a doc comment for the Dashboard's sake (the badge's `font-mono …`) still has to be in a kit source.
- The Site, the Dashboard (`Styles/dashboard.css`, inlined) and the three templates compile their own sheet;
  building `src/Rask.Site` needs `node` on PATH (RASKISLAND001 otherwise).
- Proof to repeat after a change to the entries or targets: in the built `src/Rask.Site/wwwroot/css/app.css`
  a base utility (`.bg-white{`, `.rounded-lg{`) sits before its `dark:` / `in-data-ui-button-group:`
  variant and the kit-only classes are there (`OneStylesheetCascadeTests` holds the same in the unit gate);
  and the browser test
  `UiKitActionsTests.Grouped_buttons_share_one_border_and_keep_their_corners_only_at_the_ends`, which is
  the one that catches two sheets.

## A component is never kept in a static field
An entry built during a render (`Div`, `Ui.Button`) is a positional slot of the component being rendered.
`static readonly Component Empty = Div;` shares one page's slot with every page, and `Ui.Button`'s own
`Div` (the loading indicator, built while the button is serialized) landed on it: the demo's placeholder
came back as a spinner box, only when one particular test ran first. `DemoMarkupGoldenTests
.No_site_component_keeps_a_component_in_a_static_field` guards the Site; the same holds anywhere.

## Keys: a component's key is scoped to the component that writes it
A child WITHOUT a `Key` beside keyed ones of its type keeps its instance, by its order among the unkeyed ones
(#1215) — there is no need to key every sibling. A component's `Key` names one component of its type among
everything its owner writes, NOT among the children of one element: the same rows written into two menus of
one demo need keys that differ per menu (`Key($"{menu}-{row}")`). A repeat still gets an instance and values
of its own, told apart by order, and is reported once as a `Rask.Live` warning (`docs/composition.md`).

- File upload (2026-10-08): `Ui.FileUpload` is a `<label>` around the real, `sr-only` input, and writes
  `data-rask-dropzone` and `data-rask-loading` on itself. Its states are the plain idiom (`in-data-dragging:`,
  `in-data-loading:`), which holds now that an app has ONE sheet. Read on Flux's live page, with a real pointer:
  a click on the field label OPENS the picker (three of three enabled examples; the label is `aria-hidden` with
  an id and no `for`, the input says `aria-labelledby` and `tabindex="-1"`, and the dropzone is the tab stop).
  So the label stays the kit's `<label for>` and the input says `aria-labelledby`; the input keeps the keyboard
  where Flux's dropzone does, and the ring is drawn on the dropzone by one rule in `ui.css`. Pinned on the Server
  host by `tests/Rask.Server.E2E.Tests/UiFileUploadHookTests.cs` (the request slowed, so the bar is seen at
  points between nothing and everything) and on WASM by the site's `UiKitDataInputTests`. Unmeasured, no example
  on Flux's page: an `invalid` file item (it takes the invalid input's border), sizes beyond KB (`UiFileSize`
  follows the reference's B / KB / MB / GB), the icon of a two-line item with no image. The site's demo reads a
  chosen picture for its preview, which Flux's page does on the server.

## Merging a component branch
`git rerere` is on and has replayed a one-sided resolution of `scripts/flux/lib.mjs` that silently dropped
the `__fluxRest` calls. After resolving, `git diff HEAD -- scripts/flux tests/Rask.Ui.Tests/Flux/FluxParity.cs
tests/Rask.Ui.Tests/Flux/FluxParityPages.cs tests/Rask.Ui.Tests/Flux/flux.lock.json` must be empty apart from
what the branch ADDS; `git checkout HEAD -- <file>` puts ours back.

- Calendar and date picker take Flux's `mode` as the step that OPENS them: `Ui.Calendar.Range` / `.Multiple` and
  `Ui.DatePicker.Range` are C# extension properties on the generated seed (`UiCalendarModes`, `UiDatePickerModes`)
  handing back the typed twin's own seed, so a wrong pairing does not compile; the twins are no longer joined by
  `[RaskChainEntry]`, which leaves `Ui.CalendarMultiple`, `Ui.CalendarRange` and `Ui.DatePickerRange` reachable as
  entries too (the generator has no way to hide one — to remove when it has). `Mode(value)` and the bare
  `Multiple()` are checked against the bound type at render.
- Calendar, date picker, time picker — waiting for a runtime hook each, none faked: containing the arrow, page
  and Space keys inside `[role=grid]` (the page scrolls under the calendar today); scrolling an expanded
  combobox's `aria-activedescendant` into view as the cursor moves (the time list); auto-advance, arrow
  increments and per-keystroke normalising in the typed date and time fields; arrow keys in the date picker's
  preset radio group; a press inside a typed segment not opening the popup. The calendar's focus move after an
  arrow key is `ElementRef.Focus()` from `OnRendered` (`UiCalendarFocus`) and is not yet proven in a browser.
  Not measurable on Flux's public pages, so extrapolated or `NotTranslated`: calendar sizes `Lg`/`Xxl`, every
  date-picker size but the default, `with-inputs`, `clearable` on the date picker, the shown confirmation footer.
  The today shortcut draws Heroicons' mini calendar where Flux draws a glyph of its own (1px taller window).

## Open work (integration stopped here on 2026-10-07 — see the integrator's report)
- A stale `src/Rask.Site/obj/**/rask-external` folder can fail the site build after merging main
  (`@rask/routes` not found): delete that folder.
- `sync.mjs` and `lib.mjs` on main changed (#1189): the lock is CI's, animations rest through
  `window.__fluxRest()`. Merge main's with this harness; keep the recording of animation definitions.
- The comparison in `parity.mjs` is not importable; `open.mjs` holds the one copy the open-state scripts
  share. Move parity.mjs's to a module and have `open.mjs` import it.
