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
    Ui.Button.Tone(Ui.Tone.Primary).Variant(Ui.Variant.Outline).Size(Ui.Size.Lg)["Save"],

    Ui.Modal.Title("Delete order").Id("confirm").Trigger("Delete")[
        P["This cannot be undone."]
    ],

    // Every data-input control is an IFormControl<T>: Value opens the controlled chain and Bind the
    // bound one, and the opening step fixes the value type and the mode together.
    Ui.Input.Value(_email).Label("Email").Type(InputType.Email)
           .Tone(_email.Contains('@') ? null : Ui.Tone.Error)
           .OnChange(value => _email = value),

    Ui.Accordion.Open(_section).OnOpen(key => _section = key)[
        Ui.AccordionSection.Key("ship").Title("Shipping")[P["Two working days."]],
        Ui.AccordionSection.Key("pay").Title("Payment")[P["Card or transfer."]]
    ],
];
```

Order matters: your `@theme` only wins while it is the copy the cascade reads last. The kit's sheet
deliberately carries **no preflight** and no `html`/`body` rules — your application owns its document,
and a reset arriving from a library restyles pages that never asked for it.

## What is in it

| | |
| --- | --- |
| Actions | `Ui.Button` `Ui.Dropdown` `Ui.Modal` `Ui.Swap` `Ui.ThemeController` `Ui.Fab` |
| Data display | `Ui.Accordion` `Ui.Collapse` `Ui.Avatar` `Ui.Aura` `Ui.Badge` `Ui.Card` `Ui.Carousel` `Ui.ChatBubble` `Ui.Countdown` `Ui.Diff` `Ui.Empty` `Ui.Hover3d` `Ui.HoverGallery` `Ui.Kbd` `Ui.List` `Ui.Stat` `Ui.StatusDot` `Ui.Table` `Ui.DataGrid` `Ui.Tree` `Ui.TextRotate` `Ui.Timeline` |
| Navigation | `Ui.Breadcrumbs` `Ui.Dock` `Ui.Link` `Ui.Megamenu` `Ui.Menu` `Ui.Navbar` `Ui.Pagination` `Ui.Steps` `Ui.Tabs` |
| Feedback | `Ui.Alert` `Ui.Loading` `Ui.Progress` `Ui.RadialProgress` `Ui.Skeleton` `Ui.Toast` `Ui.Tooltip` |
| Data input | `Ui.Input` `Ui.Textarea` `Ui.Select` `Ui.MultiSelect` `Ui.FileInput` `Ui.Checkbox` `Ui.Toggle` `Ui.Radio` `Ui.Range` `Ui.Rating` `Ui.Fieldset` `Ui.Validator` `Ui.Label` `Ui.Otp` `Ui.Filter` `Ui.Calendar` |
| Layout | `Ui.Divider` `Ui.Drawer` `Ui.Footer` `Ui.Hero` `Ui.Indicator` `Ui.Join` `Ui.Stack` `Ui.Mask` |
| Mockup | `Ui.MockupBrowser` `Ui.MockupCode` `Ui.MockupPhone` `Ui.MockupWindow` |
| Chrome | `Ui.Shell` `Ui.TopBar` `Ui.Brand` `Ui.Nav` `Ui.NavTab` `Ui.CrumbSwitcher` `Ui.TopLink` `Ui.Main` `Ui.Header` `Ui.MetricRow` `Ui.DetailList` `Ui.Code` `Ui.Search` |
| Support | `Ui.Icon` / `Ui.IconName`, `UiTheme` / `Ui.ThemeName`, `Ui.Breakpoint`, `UiStyles`, `UiStylesheet` |

Requires .NET 10. Runs on both the ASP.NET host and browser-WebAssembly.

## Links

- [Repository](https://github.com/pal-tamas/rask)
- [Documentation](https://rask.sh/docs/guides/ui-kit)
- [Live components](https://rask.sh/docs/ui/actions)
