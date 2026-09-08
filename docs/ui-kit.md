# The UI kit (`Rask.Ui`)

**Every daisyUI component, as a typed Rask component.** The kit wraps
[daisyUI](https://daisyui.com) 5 — vendored, compiled at the kit's own build, and shipped inside the
package — so a Rask app gets the whole component vocabulary without a single utility string, an npm
install, or a Tailwind configuration of its own.

It is **markup and nothing else**: no data access, no host dependency, and no JavaScript. It runs on
the ASP.NET host and in browser-WebAssembly, which is the one place it differs from `Rask.Dashboard` —
the console is deliberately server-only because its panels read a `DbContext`.

```bash
dotnet add package Rask.Ui
```

Live, on this site: [Actions](/docs/ui/actions) · [Data display](/docs/ui/data-display) ·
[Navigation](/docs/ui/navigation) · [Feedback](/docs/ui/feedback) ·
[Data input](/docs/ui/data-input) · [Layout & mockups](/docs/ui/layout).

## Wiring it up

Two things, and forgetting either produces a page that renders structurally correct components with
**no colour at all** — so both are worth doing before anything else.

**1. Link the stylesheet.** Opt into the build writing it, then link it:

```xml
<PropertyGroup>
  <RaskUiWriteStylesheet>true</RaskUiWriteStylesheet>
</PropertyGroup>
```

```csharp
protected override Component? HeadAssets =>
[
    Link.Rel("stylesheet").Href(UiStylesheet.Href(LiveOptions.PathBase)),   // the kit's, FIRST
    Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/css/app.css"),
];
```

`UiStylesheet.Href()` carries a content hash, so the file can be cached hard and still change when the
kit does. A library that renders kit components into somebody else's host wants no file in a `wwwroot`
it does not own; that case keeps `UiStylesheet.Css` and inlines it in a `<style>`, which is what
`Rask.Dashboard` does.

**2. Turn the theme on.** Nothing in the kit has a colour until an ancestor carries the theme scope:

```csharp
protected override Component Shell(Component head, Component body) =>
    Html.Lang("en").Attributes((UiStylesheet.ThemeScopeAttribute, ""))[head, body];
```

The scope exists so that *referencing* this package cannot repaint an application that only wanted a
button. daisyUI paints `:root` by default; the kit confines it to `[data-rask-ui]` instead, and the
same reasoning is why it ships no preflight.

**Order is the contract.** The kit's sheet declares the palette, so redefining a token in your own
`@theme` re-skins every component without overriding a single rule — which only works while your copy
is what the cascade reads last.

> **CSS layers do not merge across `<link>` elements.** If you find a kit rule beating one of your
> utilities, or the reverse, that is why — and it is not something either sheet's source order can
> settle.

## Themes

daisyUI's 35 themes all ship, as the `UiThemeName` enum. Light is the default and dark follows the
operating system; to choose one, put `data-theme` on the element carrying the theme scope — or on any
container, to re-theme just that subtree.

```csharp
UiThemeController.Label("Dark").Theme(UiThemeName.Dark).Active(_theme is UiThemeName.Dark)
                 .OnChange(theme => _theme = theme)
```

The control **reports** a choice and cannot apply it: the palette is set by an ancestor, and no
component can write an attribute onto something above it. The page holds the value and writes
`UiTheme.Value(theme)` there — which is also what lets it be persisted, something daisyUI's CSS-only
`theme-controller` could not offer, since nothing in C# knew which theme was showing.

`UiThemePicker` and `UiThemeDropdown` are ready-made pickers over the whole set.

## The three axes

Colour, fill and size are independent and compose, so an outlined error button needs no member of its
own:

```csharp
UiButton.Label("Delete").Tone(UiTone.Error).Variant(UiVariant.Outline).Size(UiSize.Lg)
```

| Enum | Members |
| --- | --- |
| `UiTone` | `Neutral` `Primary` `Secondary` `Accent` `Info` `Success` `Warning` `Error` |
| `UiVariant` | `Solid` `Outline` `Soft` `Dash` `Ghost` `Link` |
| `UiSize` | `Default` `Xs` `Sm` `Md` `Lg` `Xl` |

These are daisyUI's own words, deliberately. Translating them into a private vocabulary was the first
thing this kit did and the first thing it stopped doing: daisyUI's documentation is the documentation
for everything the components render, and a second set of words made every example a translation.

Not every component honours every member — daisyUI defines no `input-outline`, and no `tooltip-neutral`
— and **a member a component has no class for writes nothing**, rather than a class that would sit in
the markup looking as though it styled something.

Other axes follow the same rule: `UiPlacement`, `UiModalPlacement`, `UiMaskShape`, `UiLoadingShape`,
`UiSwapAnimation`, `UiAuraStyle`, `UiTabStyle`, `UiMarker`, `UiOpenOn`.

## What is in it

Grouped as daisyUI groups them, so its documentation reads straight across.

| | |
| --- | --- |
| **Actions** | `UiButton` `UiDropdown` `UiModal` `UiSwap` `UiThemeController` `UiFab` |
| **Data display** | `UiAccordion` `UiAccordionSection` `UiCollapse` `UiAvatar` `UiAura` `UiBadge` `UiCard` `UiCarousel` `UiChatBubble` `UiCountdown` `UiDiff` `UiHover3d` `UiHoverGallery` `UiKbd` `UiList` `UiListRow` `UiStat` `UiStatusDot` `UiTable` `UiTextRotate` `UiTimeline` |
| **Navigation** | `UiBreadcrumbs` `UiDock` `UiLink` `UiMegamenu` `UiMegamenuPanel` `UiMenu` `UiMenuItem` `UiNavbar` `UiPagination` `UiSteps` `UiStep` `UiTabs` `UiTab` |
| **Feedback** | `UiAlert` `UiLoading` `UiProgress` `UiRadialProgress` `UiSkeleton` `UiToast` `UiTooltip` |
| **Data input** | `UiInput` `UiTextarea` `UiSelect` `UiFileInput` `UiCheckbox` `UiToggle` `UiRadio` `UiRange` `UiRating` `UiFieldset` `UiValidator` `UiLabel` `UiFloatingLabel` `UiOtp` `UiFilter` `UiCalendar` |
| **Layout** | `UiDivider` `UiDrawer` `UiFooter` `UiHero` `UiIndicator` `UiJoin` `UiStack` `UiMask` |
| **Mockup** | `UiMockupBrowser` `UiMockupCode` `UiMockupPhone` `UiMockupWindow` |
| **Chrome** | `UiShell` `UiTopBar` `UiBrand` `UiNav` `UiNavTab` `UiCrumbSwitcher` `UiCrumbSeparator` `UiTopLink` `UiMain` `UiHeader` `UiGrid` `UiNotice` `UiMetricRow` `UiMetric` `UiDetailList` `UiDetailRow` `UiCode` `UiSearch` |
| **Support** | `UiIcon` / `UiIconName`, `UiTheme` / `UiThemeName`, `UiStyles`, `UiStylesheet` |

## Who owns the state

The kit ships no JavaScript, and that constraint decides the shape of every interactive component. It
resolves three ways, and which one a component takes is a property of what the platform can do rather
than of anyone's preference.

**The browser owns it, declaratively.** `UiModal` with an `Id` and a `Trigger` is a real
`<dialog popover>`: the browser supplies the top layer, Escape, light-dismiss and a native
`::backdrop`. `UiMegamenu` is built the same way. `UiFab` opens on `:focus-within` because daisyUI
defines no class to force it. All of these work on a prerendered page with no runtime booted, and with
scripting off entirely.

```csharp
UiModal.Title("Shortcuts").Id("shortcuts").Trigger("Show shortcuts")[ … ]
```

**The page owns it, in C#.** `UiDropdown`, `UiCollapse`, `UiAccordion`, `UiSwap`, `UiTabs` and
`UiModal`'s `Open` path hold their state in a field and redraw through the live diff — which is what
lets a dropdown close itself when the action inside it completes.

```csharp
UiDropdown.Trigger("Actions").Open(_open).OnToggle(open => _open = open)[ … ]
```

`Open` is nullable and the three settings mean three things: unset is uncontrolled and the browser
decides; `true` and `false` hand it to the page. Closed writes `dropdown-close` rather than merely
omitting `dropdown-open`, because daisyUI also opens on `:focus-within` — without it, tabbing into
the panel would re-open a dropdown the page had just closed.

**The markup owns it.** `UiTab` is a real link with a real URL, so a tab is bookmarkable, survives a
refresh and answers the back button. `UiDrawer` keeps its checkbox because daisyUI's rules are written
against `.drawer-toggle:checked`; C# sets it and hears it change, but the input is the component.

## The rule the whole kit rests on

daisyUI emits a component's CSS only where Tailwind can **see** its class name in the scanned source.
A name built by concatenation — `"btn-" + tone` — is a name no scanner ever reads, so the class is
absent from the compiled sheet and the component renders **with no styling whatsoever**. Not
misaligned, not the wrong colour: unstyled. The build stays green and the markup carries exactly the
class the call site asked for.

That is why every class the kit can write is spelled out as a complete literal in `UiClassNames.cs`,
why the axes above are closed enums rather than strings, and why `UiTextRotate` has no `Interval`
property — daisyUI reads its speed from a `duration-*` utility, and turning a `TimeSpan` into a class
name at run time is exactly the failure this rule prevents.

If you write your own `ui-*` classes, the same applies to you: copy the kit's `@theme` block into your
own stylesheet, because Tailwind emits a utility only where it can see the token.

## Two rules it holds itself to

**Mobile-first, which is a different claim from responsive.** Every control takes a 44px touch target
below `sm`. A dialog is a bottom sheet on a phone and a centred card above it, because a centred
dialog at 360px either overflows or shrinks its content past reading. The tab bar scrolls sideways
rather than wrapping, so the header is exactly one row tall however many tabs there are.

**Every control has a name.** A label is required, not optional, and it becomes the accessible name
rather than a placeholder — a placeholder disappears the moment typing starts, so the one thing saying
what a field is for vanishes exactly when a reader might check it. An icon-only button puts its label
in `aria-label`; a spinner is `aria-hidden` with its words beside it; a failed toast changes its
**icon** and not only its colour.

## Names

Every component carries the `Ui` prefix, and that is load-bearing rather than decorative. Inside a
markup host a bare `X` is the chain's `Build<X>` entry, so a component called `Shell` would collide
with `Component.Shell`, and `Nav`/`Main`/`Button`/`Select`/`Search` with the Rask.Html tags. On a
collision [RASK040](diagnostics.md#rask040) gives **neither** type an entry, across the whole
compilation.

The same rule applies to the namespace. `Rask.Ui` is an enclosing namespace of every `Rask.*`
compilation, so a type of your own named `Ui` inside a `Rask.`-rooted namespace will be shadowed by it
— C# resolves a simple name against enclosing namespaces before it looks at imports.

Each component lives in a file named after it, so the file list in `src/Rask.Ui` is the component list.

## See also

- [Dashboard](dashboard.md) — the operator console this kit was extracted from
- [Tailwind](tailwind.md) — how the compiler is wired into a Rask build
- [Building components](building-components.md) — the chain the kit is composed with
