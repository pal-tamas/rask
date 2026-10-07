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
  icon, spinner and tooltip in `RenderChildren`; that takes its enclosing component off the cached-render fast
  path, so it only does it when it has such a part (plain `Ui.Button["Save"]` stays fast).
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
label themselves: implement `IUiFieldControl` (`ControlId` = `UiFieldId.Derive(Id, Bind, Label)`, `Bound` =
`Bind`) and, in `Render`, `var field = UiWithField.For(this, Label, Description, DescriptionTrailing, Badge);`
→ put `field.ControlId`, `data-ui-control` and `.Aria(field.Aria)` on the control's own element →
`return field.Wrap(control);` (a checkbox, radio or switch: `field.Wrap(control, Ui.FieldVariant.Inline, controlFirst: true)`).

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
card accordion callout button toast badge` today).
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
  Description, Icon, Separator, Table, Card, Callout, Button, Badge are real now — use them.
- **A real component inside a wrapper that is not rebuilt** (Flux's button sits in `<ui-tooltip>` or
  `<ui-dropdown>`): write the wrapper as a `<div data-ui-tooltip data-parity-skip="self">` holding the real
  component and a `display:none` `data-parity-skip` box per hidden sibling (the bubble, the menu). Without
  the marked wrapper the tool sees Flux's button under a marked ancestor and yours at the top, and the
  counts differ. See `SeparatorParity.Bar`, `TableParity.RowMenu`.
- **A colour the docs page hands a component by class** and that must lose to the component's own hover
  (`text-zinc-300` on the header's subtle button): `.parity-x:not(:hover){…}` on the page, and
  `not-hover:text-zinc-300` in an app.
- **Not differences:** `NATIVE` tag pairs (and `button`→`summary`); the colour of a border 0px wide on
  both sides, or of an outline with `outline-style:none` on both, at rest and in a forced state; the
  offset of a node with no box (`display:none` itself or above it, or 0×0 on both sides); `oklch(… none)`
  ≡ `oklch(… 0)`; a forced state where only one side measured the node (60 per example).
- **Animations:** clock-driven CSS animations are paused at their first frame and their DEFINITION (name
  with `flux-`≡`ui-`, duration, delay, iterations, direction, fill, keyframes with easing) is recorded per
  node and compared. Scroll-driven ones are neither paused nor recorded: the kit uses one where Flux runs
  script. After any change to `lib.mjs`, cached Flux measurements are stale — `parity.mjs <slug> --refresh`.
- **A state the page does not load in** (a shown toast) gets its own script beside `parity.mjs` and a
  pseudo-page: `node scripts/flux/parity-toast.mjs` raises each toast on Flux's page, measures it with
  `lib.mjs`, compares through `parity.mjs` under the name `toast-shown` (`ToastShownParity`) and adds a
  where-on-screen check (38 + 38). Its NATIVE pairs (`ui-toast`, `ui-toast-group`, `ui-close` → `div`) stay
  local to it. `toast-shown` is no Flux slug: nothing that walks Flux's pages may assume a parity page is one.
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
  arm of `@custom-variant dark` goes too. `AppearanceToggle` is `Ui.Button.Subtle` now; its hand-drawn
  tooltip has a `// SEAM:` to become the button's `Tooltip`/`TooltipKbd` when `Ui.Tooltip` lands.
- Button: `UiClassNames.ButtonTone/ButtonVariant/ButtonSize` and the `.btn-*`, `.btn > svg`,
  `.btn[data-loading]` CSS stay until Popover, Dropdown, Fab, DayGrid, Filter, MultiSelect, Profile,
  Pagination, Sidebar and the templates stop writing raw `btn`. `UiButtonTooltip` is an internal stopgap
  (a child of the button shown by CSS) to be replaced by `Ui.Tooltip` when that branch merges; no parity
  page measures it. `Disabled`, `Command`/`CommandFor` are the `<button>`'s own
  attributes, kept as such, not Flux props.
- The `.alert-*` contrast corrections stay in `ui.css` for the templates' hand-written `alert alert-*`.
- The Dashboard's queue tiles lost their icon and hover (Flux's card has no link or icon props, and the
  Dashboard may not write classes): rebuilt on Flux pieces later.
- Parity stand-ins still standing: card page (fields, switches, the heading/text lines whose variant was
  not looked up), table page (avatar, the dropdown and menu around the row button, pager), progress page
  (slider, as raw `ui-slider` markup), separator page (the tooltip around the theme button), field page
  (inputs).
- Badge: `Ui.NavItem` / `Ui.NavTab` still take `BadgeTone` (`Ui.Tone`), mapped to a colour by
  `UiBadge.ToneColor`; both go with the old chrome. `Mono()` and the close button's default `aria-label` were
  removed as non-Flux: a long token says `.Class("font-mono max-w-full break-all whitespace-normal!")` —
  named in `UiBadge`'s remarks so the kit's sheet carries it for the Dashboard and DevTools, whose sources
  Tailwind never reads (`UiConsoleChromeTests` pins it; `DashboardIsKitOnlyTests` allows exactly that call
  until the Dashboard has a sheet of its own). daisyUI's `.badge` is STILL in the compiled sheet: the bare
  word stands in some sixty kit comments and identifiers (the last rule of section 4 was not applied) — reword them, then
  assert its absence in `UiStylesheetTests`.

## Runtime hooks the kit is waiting for (Flux does it in script; Rask's runtime cannot yet)
- Toast: hovering RESTARTS the countdown in Flux — the runtime resumes the remainder. Hovering a group
  pauses EVERY toast in it — needs a pause scope on an ancestor. The stack's 350 ms glide when a toast
  joins or leaves needs per-child height/offset custom properties. The runtime also pauses on focus,
  where Flux does not.
- Toast, unexplained, for a later look: an unkeyed `Ui.Toast` inside a keyed `Ui.ToastGroup`, chosen by a
  `switch` among keyed call sites, was remounted on every parent render.
- The built-in toast (the host's, when the app places no `Ui.Toast`) shows one at a time, as Flux's does.

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

## Merging a component branch
`git rerere` is on and has replayed a one-sided resolution of `scripts/flux/lib.mjs` that silently dropped
the `__fluxRest` calls. After resolving, `git diff HEAD -- scripts/flux tests/Rask.Ui.Tests/Flux/FluxParity.cs
tests/Rask.Ui.Tests/Flux/FluxParityPages.cs tests/Rask.Ui.Tests/Flux/flux.lock.json` must be empty apart from
what the branch ADDS; `git checkout HEAD -- <file>` puts ours back.

## Open work (integration stopped here on 2026-10-07 — see the integrator's report)
- A stale `src/Rask.Site/obj/**/rask-external` folder can fail the site build after merging main
  (`@rask/routes` not found): delete that folder.
- An icon-only button is named by `aria-label` from its `Tooltip`; Flux names it by `aria-labelledby` through
  `<ui-tooltip>`. Goes Flux's way when `Ui.Tooltip` merges.
- `sync.mjs` and `lib.mjs` on main changed (#1189): the lock is CI's, animations rest through
  `window.__fluxRest()`. Merge main's with this harness; keep the recording of animation definitions.
- The comparison in `parity.mjs` is not importable; open-state scripts copy it. Move it to a module.
