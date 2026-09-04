# The UI kit (`Rask.Ui`)

The components the framework's own surfaces are drawn with — the operator console at `/_rask`, the
landing site, and the docs showcase. It exists so those three look like one product without copying
utility strings between them.

It is **markup and nothing else**: no data access, no host dependency, no JavaScript. It runs on the
ASP.NET host and in browser-WebAssembly, which is the one place it differs from `Rask.Dashboard` — the
console is deliberately server-only because its panels read a `DbContext`.

```bash
dotnet add package Rask.Ui
```

## The stylesheet comes with the package

Tailwind is a compiler: it scans the project it runs in for the class names actually written, and emits
only those utilities. A compiled library's class names are **invisible** to your Tailwind build, so a
kit that left styling to its consumer would render as unstyled HTML with nothing reporting it.

So the kit compiles its own sheet at its own build, embeds it, and hands it over:

```csharp
protected override Component? HeadAssets =>
[
    Style[Raw.Value(UiStylesheet.Css)],   // the kit's, FIRST
    Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/css/app.css"),
];
```

**Order is the contract.** Two properties of that sheet depend on it:

- It declares the `--color-ui-*` palette. Redefining any of those in your own `@theme` re-skins every
  component without overriding a single rule — which only works while your copy is what the cascade
  reads last.
- It carries **no preflight** and no `html`/`body` rules. Your application owns its document. A reset
  arriving from a library restyles pages that never asked for it; that is not hypothetical, it is what
  happened to host applications while the console still rendered inside their document.

There is nothing to serve: no static web assets, no Razor SDK, no `_content/` path to map. For a
stylesheet this size a `<style>` is smaller than the machinery.

## Re-skinning

Redefine the tokens you want and leave the rest:

```css
@import "tailwindcss";

@theme {
  --color-ui-brand: oklch(0.62 0.19 292);
  --color-ui-ink: oklch(0.94 0.01 285);
}
```

| Token | What it is |
| --- | --- |
| `--color-ui-bg` `--color-ui-panel` `--color-ui-well` | the surface ladder, deliberately close together — depth comes from hairlines, not contrast |
| `--color-ui-line` | every hairline |
| `--color-ui-ink` `--color-ui-muted` | primary and secondary text |
| `--color-ui-brand` | links, focus rings, the active tab |
| `--color-ui-ok` `--color-ui-warn` `--color-ui-danger` | status **fills** — dots, tinted grounds, text on a dark toast |
| `--color-ui-ok-ink` `--color-ui-warn-ink` | the same two statuses as **text on a light ground**, which the fills fail 4.5:1 for |

## What is in it

| | |
| --- | --- |
| **Chrome** | `UiShell` `UiTopBar` `UiBrand` `UiNav` `UiNavTab` `UiCrumbSwitcher` `UiCrumbSeparator` `UiTopLink` `UiMain` |
| **Controls** | `UiButton` `UiSearch` `UiStatusDot` |
| **Data** | `UiMetricRow` `UiMetric` `UiDetailList` `UiDetailRow` `UiCode` |
| **Overlays** | `UiModal` `UiToast` |
| **Support** | `UiIcon` / `UiIconName`, `UiTone`, `UiStylesheet` |

`UiTone` is the vocabulary each component colours by — `Neutral` `Primary` `Quiet` `Ok` `Warn`
`Danger` `Busy`. Not every component honours every member (a status dot has no `Primary`, a button no
`Busy`); anything a component has no meaning for reads as `Neutral`.

## Two rules it holds itself to

**Mobile-first, which is a different claim from responsive.** Every control takes a 44px touch target
below `sm`. The tab bar scrolls sideways rather than wrapping, so the header is exactly one row tall
however many tabs there are — a wrapping bar changes the page's header height between deployments and
moves the content under a thumb. A detail sheet is a bottom sheet on a phone and a centred card above
it, because a centred dialog at 360px either overflows or shrinks its content past reading.

**It ships no JavaScript.** `UiCrumbSwitcher` is a real `<select>` with the chrome stripped off rather
than a custom menu, because a menu is a popover, and a popover is a key listener and an outside click.
The `<select>` is keyboard-navigable for free, announces itself correctly, and opens the platform's own
picker on a phone. Overlays are a state flip on the owning page, so they behave identically on the
Server transport and in WebAssembly. What that does *not* buy is a focus trap: closing is reachable by
keyboard, but focus is free to leave the sheet.

## Names

Every component carries the `Ui` prefix, and that is load-bearing rather than decorative. Inside a
markup host a bare `X` is the chain's `Build<X>` entry, so a component called `Shell` would collide
with `Component.Shell`, and `Nav`/`Main`/`Button`/`Select`/`Search` with the Rask.Html tags. On a
collision [RASK040](diagnostics.md#rask040) gives **neither** type an entry, across the whole
compilation.

The same rule applies to the namespace. `Rask.Ui` is an enclosing namespace of every `Rask.*`
compilation, so a type of your own named `Ui` inside a `Rask.`-rooted namespace will be shadowed by it
— C# resolves a simple name against enclosing namespaces before it looks at imports.

## See also

- [Dashboard](dashboard.md) — the operator console this kit was extracted from
- [Tailwind](tailwind.md) — how the compiler is wired into a Rask build
- [Building components](building-components.md) — the chain the kit is composed with
