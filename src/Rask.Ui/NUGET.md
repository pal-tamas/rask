# Rask.Ui

**Every [daisyUI](https://daisyui.com) component, as a typed C# component** for the
[Rask](https://github.com/pal-tamas/rask) One Person Framework — the operator console, the landing
site and the docs showcase all draw with these.

- **The whole daisyUI vocabulary, typed.** Colour, fill and size are independent enums that compose,
  so an outlined error button needs no member of its own. daisyUI's own words, deliberately: its
  documentation is the documentation for everything these components render.
- **The stylesheet comes with it.** Tailwind scans the project it runs in, so a compiled library's
  class names are invisible to *your* Tailwind build and would emit nothing. This package compiles its
  own sheet — daisyUI included — and hands it to you. No npm install, no Tailwind configuration, no
  `_content/` path to map.
- **Ships no JavaScript.** Where the platform can own the interaction it does: a dialog is a real
  `<dialog popover>`, so the browser supplies the top layer, Escape, light-dismiss and a native
  backdrop, and it all works on a prerendered page before any runtime has booted. Where it cannot, the
  state is a field in C# and redraws through the live diff.
- **Mobile-first, not merely responsive.** Every control takes a 44px touch target below `sm`; a
  dialog is a bottom sheet on a phone and a centred card above it, because a centred dialog at 360px
  either overflows or shrinks its content past reading.
- **Every control has a name.** A label is required rather than optional, and it becomes the
  accessible name rather than a placeholder — which disappears the moment typing starts.
- **35 themes, re-skinned by tokens rather than overrides.** Redefine a custom property in your own
  `@theme` and every component follows, without a single rule being overridden.

## Use

```bash
dotnet add package Rask.Ui
```

Two steps, and skipping either renders structurally correct components with **no colour at all**.
Opt into the build writing the sheet:

```xml
<PropertyGroup>
  <RaskUiWriteStylesheet>true</RaskUiWriteStylesheet>
</PropertyGroup>
```

Then link it before your own, and turn the theme scope on:

```csharp
protected override Component? HeadAssets =>
[
    Link.Rel("stylesheet").Href(UiStylesheet.Href()),   // the kit's, first
    Link.Rel("stylesheet").Href("/css/app.css"),
];

// Nothing in the kit has a colour until an ancestor carries this.
protected override Component Shell(Component head, Component body) =>
    Html.Lang("en").Attributes((UiStylesheet.ThemeScopeAttribute, ""))[head, body];
```

```csharp
protected override Component? Render() =>
[
    UiButton.Tone(UiTone.Primary).Variant(UiVariant.Outline).Size(UiSize.Lg)["Save"],

    UiModal.Title("Delete order").Id("confirm").Trigger("Delete")[
        P["This cannot be undone."]
    ],

    // Every data-input control is an IFormControl<T>: Value opens the controlled chain and Bind the
    // bound one, and the opening step fixes the value type and the mode together.
    UiInput.Value(_email).Label("Email").Type(InputType.Email)
           .Tone(_email.Contains('@') ? null : UiTone.Error)
           .OnChange(value => _email = value),

    UiAccordion.Open(_section).OnOpen(key => _section = key)[
        UiAccordionSection.Key("ship").Title("Shipping")[P["Two working days."]],
        UiAccordionSection.Key("pay").Title("Payment")[P["Card or transfer."]]
    ],
];
```

Order matters: your `@theme` only wins while it is the copy the cascade reads last. The kit's sheet
deliberately carries **no preflight** and no `html`/`body` rules — your application owns its document,
and a reset arriving from a library restyles pages that never asked for it.

## What is in it

| | |
| --- | --- |
| Actions | `UiButton` `UiDropdown` `UiModal` `UiSwap` `UiThemeController` `UiFab` |
| Data display | `UiAccordion` `UiCollapse` `UiAvatar` `UiAura` `UiBadge` `UiCard` `UiCarousel` `UiChatBubble` `UiCountdown` `UiDiff` `UiEmpty` `UiHover3d` `UiHoverGallery` `UiKbd` `UiList` `UiStat` `UiStatusDot` `UiTable` `UiDataGrid` `UiTextRotate` `UiTimeline` |
| Navigation | `UiBreadcrumbs` `UiDock` `UiLink` `UiMegamenu` `UiMenu` `UiNavbar` `UiPagination` `UiSteps` `UiTabs` |
| Feedback | `UiAlert` `UiLoading` `UiProgress` `UiRadialProgress` `UiSkeleton` `UiToast` `UiTooltip` |
| Data input | `UiInput` `UiTextarea` `UiSelect` `UiMultiSelect` `UiFileInput` `UiCheckbox` `UiToggle` `UiRadio` `UiRange` `UiRating` `UiFieldset` `UiValidator` `UiLabel` `UiOtp` `UiFilter` `UiCalendar` |
| Layout | `UiDivider` `UiDrawer` `UiFooter` `UiHero` `UiIndicator` `UiJoin` `UiStack` `UiMask` |
| Mockup | `UiMockupBrowser` `UiMockupCode` `UiMockupPhone` `UiMockupWindow` |
| Chrome | `UiShell` `UiTopBar` `UiBrand` `UiNav` `UiNavTab` `UiCrumbSwitcher` `UiTopLink` `UiMain` `UiHeader` `UiMetricRow` `UiDetailList` `UiCode` `UiSearch` |
| Support | `UiIcon` / `UiIconName`, `UiTheme` / `UiThemeName`, `UiBreakpoint`, `UiStyles`, `UiStylesheet` |

Requires .NET 10. Runs on both the ASP.NET host and browser-WebAssembly.

## Links

- [Repository](https://github.com/pal-tamas/rask)
- [Documentation](https://rask.sh/docs/guides/ui-kit)
- [Live components](https://rask.sh/docs/ui/actions)
