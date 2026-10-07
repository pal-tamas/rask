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

Behaviour is measured too: open the page, use the component with keyboard and pointer (Playwright via
`scripts/flux/lib.mjs`'s `chromium()`), and write down what each key does before implementing it.

## 2. Write the component
**Nothing beyond Flux** — the owner, 2026-10-07: *"ne csináljunk ilyen kiegészítéseket"*. A component carries
exactly Flux's props, values, attributes and behaviour: no extra prop, no extra ARIA, no convenience step, no
wider range, however reasonable. The only non-Flux surface is the Rask TRANSLATION of a Livewire / Alpine /
PHP mechanism — `Bind`/`Value` for `wire:model`, a `Callback` for an event or `wire:click`, `RouteUrl? Href`
with client-side navigation for a link, a `Toast`-style facade for a `Flux::…()` call — plus `Class`, which
every Flux component takes. Same spirit, 2026-10-06: Flux stops no animation under reduced motion, and
neither does the kit. (A `Translations` table in `FluxConformanceTests`, naming each non-Flux property and
why, is to be the gate for this; it is NOT written yet — see "Open work" at the end.)

- **File and type:** `src/Rask.Ui/Ui<Name>.cs`, `public sealed partial class Ui<Name>`; a part is
  `Ui<Parent><Part>` (`flux:button.group` → `UiButtonGroup`, reached as `Ui.ButtonGroup`).
- **Props are Flux's**, one property per documented prop: `icon:trailing` → `IconTrailing`,
  `tooltip:position` → `TooltipPosition`. Optional props are nullable; `tests/Rask.Ui.Tests/Flux/flux.snapshot.json`
  is the list.
- **One enum per component** for a prop's values, nested in `Ui` (`src/Rask.Ui/Ui.<Name>.cs`):
  `Ui.ButtonVariant { Outline, Primary, Filled, Danger, Ghost, Subtle }`, FIRST member = Flux's default.
  The chain generator turns each member into a step (`Ui.Button.Primary.Sm`). Shared across components:
  `Ui.Color` (ONE file, `Ui.Color.cs`: Tailwind's 17 chromatic hues in its order, then Slate, Gray, Zinc,
  Neutral, Stone — a component whose Flux prop has no neutrals draws a neutral as its default, as `UiText`
  does), `Ui.IconName`, `Ui.IconVariant`, `Ui.Position`, `Ui.Align`. A member named like an inherited HTML
  tag entry (`Strong`, `Desc`, `Button`, `A`, `Div`, `Text`, `Search`, `Time`) gets a step that cannot be
  called (CS0176): write `.Variant(Ui.TextVariant.Strong)` there. Known generator limit, not fixed.
- **Rask's own idiom where Flux's is Livewire's:** `wire:model` → `Bind`/`Value` (see
  `docs/building-form-controls.md`); an event → a non-nullable `Callback`/`Callback<T>`; a link →
  `RouteUrl? Href`; a slot → children (the indexer) or a `Component?` property for a named slot.
- **Marker:** the root writes `data-ui-<part>` where Flux writes `data-flux-<part>` (`data-ui-button`,
  `data-ui-button-group`), bare, through the ONE helper: `private static readonly UiPartMarker Marker =
  new("ui-button");` and `ResolveData() => Marker.With(Data)` (`With(Data, "color", name)` for one more
  attribute; `new("slot", "text")` for a valued one). The parity tool pairs nodes by it. Write only the
  `data-ui-*` attributes Flux writes: every one is a marker to the tool.
- **Element:** where Flux renders a custom element (`<ui-field>`, `<ui-label>`, `<ui-progress>`,
  `<ui-table-scroll-area>`, `<ui-disclosure>`), write the NATIVE element with the same semantics and no
  script (`<div>`, `<label for>`, `<div role="progressbar">`, `<details>`) and add the pair to `NATIVE` in
  `parity.mjs`. A `UiElement` MUST override `TagName`, or it renders itself for ever (a stack overflow that
  kills the test process). A part that puts markup of its own around its children is a `Component`, not a
  `UiElement` (Core's serializer writes an element's children straight from the indexer) — `UiCallout`,
  `UiTable` via `HostedElement`.
- **Icons:** `Ui.Icon.Name(Ui.IconName.ChevronDown).Variant(Ui.IconVariant.Micro)` (or `.Mini`, `.Solid`) —
  never an inline SVG path. The enum is all of Heroicons, generated by `scripts/flux/icons.mjs`.
- **Classes:** complete Tailwind literals in the component (a `private static string` switch per
  enum). The `zinc` scale and `fx-accent` / `fx-accent-content` / `fx-accent-foreground` tokens
  (`src/Rask.Ui/Styles/ui.css`), `dark:` for dark. **No daisyUI class, no `base-*`, no `--color-ui-*`.**
  Never build a class by concatenation — Tailwind only emits what it can read whole.
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
   Fix the COMPONENT until it passes. A difference is only accepted when it is Livewire-specific or
   comes from the docs page rather than the component; say which, in a comment on the example.
   Two false greens to know: a crashed `parity.mjs` prints no `FAIL` (read its last line), and
   `dotnet test` prints `Passed!` with a lower total after the test process crashed — look for
   `FATAL ERROR` / `Test Run Failed`, and compare the total with the last run.
4. Unit tests in `tests/Rask.Ui.Tests/Components/Ui<Name>Tests.cs`: behaviour and markup contract
   (roles, attributes, what a prop writes). Names are sentences; three blank-line-separated blocks.

### The harness, as it is (`scripts/flux/lib.mjs`, `parity.mjs`, `FluxParityPages.cs`)
One harness for every page. Do not patch it to pass a page; if a rule is missing, add ONE general rule
with a comment, and re-run every built page (`field heading text icon separator skeleton progress table
card accordion callout` today).
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
  Description, Icon, Separator, Table, Card, Callout are real now — use them.
- **Not differences:** `NATIVE` tag pairs (and `button`→`summary`); the colour of a border 0px wide on
  both sides, or of an outline with `outline-style:none` on both, at rest and in a forced state; the
  offset of a node with no box (`display:none` itself or above it, or 0×0 on both sides); `oklch(… none)`
  ≡ `oklch(… 0)`; a forced state where only one side measured the node (60 per example).
- **Animations:** clock-driven CSS animations are paused at their first frame and their DEFINITION (name
  with `flux-`≡`ui-`, duration, delay, iterations, direction, fill, keyframes with easing) is recorded per
  node and compared. Scroll-driven ones are neither paused nor recorded: the kit uses one where Flux runs
  script. After any change to `lib.mjs`, cached Flux measurements are stale — `parity.mjs <slug> --refresh`.
- **Public API:** `python3 scripts/public-api/record.py src/Rask.Ui` builds and applies RS0016/RS0017 to
  both baselines (run it twice: a step exists only once its property compiles). It is the only such script.

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
  arm of `@custom-variant dark` goes too. `AppearanceToggle` has a `// SEAM:` to become
  `Ui.Button.Subtle.Square.Icon(Moon)` in a `Ui.Tooltip`.
- The `.alert-*` contrast corrections stay in `ui.css` for the templates' hand-written `alert alert-*`.
- The Dashboard's queue tiles lost their icon and hover (Flux's card has no link or icon props, and the
  Dashboard may not write classes): rebuilt on Flux pieces later.
- Parity stand-ins still standing: card page (buttons, fields, switches, the heading/text lines whose
  variant was not looked up), table page (badge, avatar, row menu, pager, the card's heading row),
  progress page (slider, as raw `ui-slider` markup), separator page (two unmarked buttons), field page
  (inputs).

## Open work (integration stopped here on 2026-10-07 — see the integrator's report)
- A stale `src/Rask.Site/obj/**/rask-external` folder can fail the site build after merging main
  (`@rask/routes` not found): delete that folder.
- The `Translations` gate in `FluxConformanceTests`, and the removals the owner's rule asks for.
- `sync.mjs` and `lib.mjs` on main changed (#1189): the lock is CI's, animations rest through
  `window.__fluxRest()`. Merge main's with this harness; keep the recording of animation definitions.
- The comparison in `parity.mjs` is not importable; open-state scripts copy it. Move it to a module.
