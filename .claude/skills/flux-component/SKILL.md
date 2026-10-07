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
  `data-ui-*` attributes Flux writes: every one is a marker to the tool. A host that carries SEVERAL markers
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
- **A new tab** is `rel="noopener noreferrer"` everywhere (`Ui.Link.External()`, `Ui.Button.NewTab()`).
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
  page measures it. `Ui.ButtonType.Reset`, `Disabled`, `Command`/`CommandFor` are the `<button>`'s own
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

## Runtime hooks that exist (Flux does it in script; the component writes the attribute)
The kit ships no script. Rask's RUNTIME carries generic hooks keyed on attributes
(`src/Rask.Core/Resources/rask-hooks.ts`, one module per concern; `docs/js-interop-runtime.md#behaviour-hooks-data-rask-`
is the reference; `tests/Rask.Server.E2E.Tests/RuntimeHook*Tests.cs` pin each one to what Flux did). A
component reaches Flux's behaviour by writing exactly these — never by a handler that round-trips:

| Component | Writes | Gets |
|---|---|---|
| Tooltip (and Button's `Tooltip`) | root `data-rask-tooltip="<bubble id>"`; bubble `popover="manual"`; `interactive`: `aria-expanded="false"` + `aria-controls` on the trigger | shown at 0 ms on pointer and keyboard focus, hidden on leave / blur / Escape / press, top layer for any trigger, `aria-expanded` mirrored |
| Dropdown / Popover `hover` | root `data-rask-hover="<panel id>"` (trigger keeps `popovertarget`) | opens over trigger or panel, closes over neither, a press keeps it, Enter opens, no lock |
| Sidebar rail item | the same + `data-rask-hover-if="<selector true while collapsed>"` | the menu opens on hover only while the rail is collapsed |
| Dropdown, Popover, Select, Context | panel `data-rask-lock` | `<html>`: `overflow:hidden; pointer-events:none; scrollbar-gutter:stable` while open |
| any `popover="auto"` panel | nothing | Tab out closes it; a press outside hands focus to its `popovertarget` button |
| Menu | `[role=menu]` `data-rask-menu-pointer`; each row `OnPointerEnter` → move the C# cursor; no `:hover` highlight, only `[data-active]` | one lit row, the arrows continue from the hovered one, none lit when the pointer leaves |
| Menu submenu | its row `data-rask-safe-area="<flyout id>"`; close a submenu only when ANOTHER row is entered, never on the menu's pointerleave | Flux's safe area and a flyout that stays when the pointer leaves the menu |
| Modal (state-driven) | `<dialog data-rask-modal-open="true|false">` instead of `open` | `showModal()`, `::backdrop`, closes when the state says so; `OnClose` / `OnCancel` still fire |
| Modal | `data-rask-modal="any|press|escape|none"` from `dismissible` × `escapable` (both → `any`, outside only → `press`, Escape only → `escape`, neither → `none`); `data-rask-lock="scroll"`; keep `command`/`commandfor` | the four dismissals, `cancel` for a press outside, `data-open` while shown, a fallback where invoker commands are missing, Flux's modal lock |
| Input `copyable` | button `data-rask-copy="<input id>"`; style the tick on `[data-copied]` | clipboard in the click, 2 s copied state |
| Input `clearable` | button `data-rask-clear="<input id>"` (no `OnClick`) | emptied, `input` fired, focus in the field |
| Input `mask` / `mask:dynamic="$money($input)"` | `data-rask-mask="<pattern>"` / `data-rask-mask-money` (`=".,2"`) | Flux's (Alpine's) shaping; any other `mask:dynamic` expression is NOT supported |
| Switch | `<input type="checkbox" role="switch">` | Enter toggles |
| Slider | `data-rask-big-step="<BigStep ?? Step>"` on the `<input type="range">` | Shift+Arrow, PageUp / PageDown |
| Select (listbox button) | `data-rask-listbox-button` on the closed `button[role=combobox]` | Enter does nothing, the arrows do not scroll (open the list from the C# key handler: Flux opens on ArrowUp / ArrowDown / Space) |
| OTP | group `data-rask-otp` (`="alpha"`, `="alphanumeric"`); cells rendered with NO `value`, NO handler, NO re-keying; ONE `Input.Type(Hidden)` inside, bound to the string | every key of Flux's otp input, fast typing included |
| Toast | `data-rask-dismiss-hold="pointer"` beside `data-rask-dismiss-after` | focus no longer holds the countdown |
| ToastGroup | `data-rask-dismiss-scope` on the group; `data-rask-stack` on the parent of the stacked toasts, and CSS from `--rask-stack-index` / `-height` / `-offset` | one pointer holds them all; the 350 ms glide |
| Sidebar | collapse checkbox `data-rask-persist="flux-sidebar-collapsed-desktop"` (plus a head script for a WASM cold load); mobile checkbox `data-rask-uncheck-on-navigate` | state kept across visits; drawer closed on navigation |
| Carousel | `data-rask-carousel`, `data-rask-carousel-track`, `data-rask-carousel-indicators`, `data-rask-carousel-controls` beside the `data-ui-*` markers; `data-direction`, `data-name`, `data-advance`, `data-wrap`, `data-scroll`, `data-autoplay` as today | position flags, arrows, indicators, autoplay |

Measured, and NOT built because Flux does not do it: a toast's countdown does not RESTART under the pointer — it
resumes the remainder (shown 1000 ms, hovered 3000 ms, gone 4359 ms after the pointer left; 5390 would be a
restart). The earlier note here said otherwise.

Known and open: two toasts whose countdowns end in the same frame press two dismiss buttons at once, and the
second press can carry a handler id the first render retired — give stacked toasts distinct durations or key
the handler. An unkeyed `Ui.Toast` inside a keyed `Ui.ToastGroup`, chosen by a `switch` among keyed call
sites, was remounted on every parent render (unexplained). The built-in toast shows one at a time, as Flux's.

## Merging a component branch
`git rerere` is on and has replayed a one-sided resolution of `scripts/flux/lib.mjs` that silently dropped
the `__fluxRest` calls. After resolving, `git diff HEAD -- scripts/flux tests/Rask.Ui.Tests/Flux/FluxParity.cs
tests/Rask.Ui.Tests/Flux/FluxParityPages.cs tests/Rask.Ui.Tests/Flux/flux.lock.json` must be empty apart from
what the branch ADDS; `git checkout HEAD -- <file>` puts ours back.

## Open work (integration stopped here on 2026-10-07 — see the integrator's report)
- A stale `src/Rask.Site/obj/**/rask-external` folder can fail the site build after merging main
  (`@rask/routes` not found): delete that folder.
- The `Translations` gate in `FluxConformanceTests`, and the removals the owner's rule asks for.
- `sync.mjs` and `lib.mjs` on main changed (#1189): the lock is CI's, animations rest through
  `window.__fluxRest()`. Merge main's with this harness; keep the recording of animation definitions.
- The comparison in `parity.mjs` is not importable; open-state scripts copy it. Move it to a module.
