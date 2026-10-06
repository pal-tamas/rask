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
- **File and type:** `src/Rask.Ui/Ui<Name>.cs`, `public sealed partial class Ui<Name>`; a part is
  `Ui<Parent><Part>` (`flux:button.group` → `UiButtonGroup`, reached as `Ui.ButtonGroup`).
- **Props are Flux's**, one property per documented prop: `icon:trailing` → `IconTrailing`,
  `tooltip:position` → `TooltipPosition`. Optional props are nullable; `tests/Rask.Ui.Tests/Flux/flux.snapshot.json`
  is the list.
- **One enum per component** for a prop's values, nested in `Ui` (`src/Rask.Ui/Ui.<Name>.cs`):
  `Ui.ButtonVariant { Outline, Primary, Filled, Danger, Ghost, Subtle }`, FIRST member = Flux's default.
  The chain generator turns each member into a step (`Ui.Button.Primary.Sm`). Shared across components:
  `Ui.Color` (Tailwind's hues), `Ui.IconName`, `Ui.IconVariant`, `Ui.Position`, `Ui.Align`.
- **Rask's own idiom where Flux's is Livewire's:** `wire:model` → `Bind`/`Value` (see
  `docs/building-form-controls.md`); an event → a non-nullable `Callback`/`Callback<T>`; a link →
  `RouteUrl? Href`; a slot → children (the indexer) or a `Component?` property for a named slot.
- **Marker:** the root writes `data-ui-<part>` where Flux writes `data-flux-<part>` (`data-ui-button`,
  `data-ui-button-group`). The parity tool pairs nodes by it.
- **Classes:** complete Tailwind literals in the component (a `private static string` switch per
  enum). The `zinc` scale and `fx-accent` / `fx-accent-content` / `fx-accent-foreground` tokens
  (`src/Rask.Ui/Styles/ui.css`), `dark:` for dark. **No daisyUI class, no `base-*`, no `--color-ui-*`.**
  Never build a class by concatenation — Tailwind only emits what it can read whole.
- **CSS only when a utility cannot say it** (a keyframe, a `:has()` chain): in `ui.css` under
  `@layer rask`, keyed on the `data-ui-*` marker.
- Markup, ARIA and keyboard are part of "exactly": same element, same roles, same states.
- Follow `CLAUDE.md` and `docs/api-style.md`; XML-doc every public member in a line or two.

## 3. Prove it
1. `tests/Rask.Ui.Tests/Flux/Parity/<Name>Parity.cs` — derive from `FluxParity`, translate EVERY
   example on Flux's page (same order, same words; section = the `<h2>` id above it).
2. Add the part to `Built` in `FluxConformanceTests`; anything that does not translate goes in
   `NotTranslated` with its reason.
3. ```bash
   dotnet test tests/Rask.Ui.Tests --filter "FullyQualifiedName~Flux"
   node scripts/flux/parity.mjs <slug>            # until: "matches Flux"
   ```
   Fix the COMPONENT until it passes. A difference is only accepted when it is Livewire-specific or
   comes from the docs page rather than the component; say which, in a comment on the example.
4. Unit tests in `tests/Rask.Ui.Tests/Components/Ui<Name>Tests.cs`: behaviour and markup contract
   (roles, attributes, what a prop writes). Names are sentences; three blank-line-separated blocks.

## 4. Replace what it supersedes
One commit per component, the solution building throughout:
- delete the daisy-drawn component(s) it replaces and their `UiClassNames` entries and tests;
- convert every call site — `src/Rask.Site`, `src/Rask.Dashboard`, `src/Rask.DevTools`,
  `src/Rask.Site.DataDemo`, `src/Rask.Templates`, `tests/**`, `docs/**`, `README.md`, `llms.txt`;
- showcase: the component's demo on the site shows every Flux example (`src/Rask.Site/Features/UiKit`),
  with its E2E; `docs/ui-kit.md`; CHANGELOG `[Unreleased]` (BREAKING where a name or value changed);
- `src/Rask.Ui/PublicAPI/*/PublicAPI.Unshipped.txt` (both TFMs) — the build's RS0016/RS0017 messages
  quote the lines.

Then the `rask-ship` gate and `land-on-main`. Screenshot the showcase demo in light and dark
(`run-rask`) before landing.
