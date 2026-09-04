# Rask.Ui

The component kit the [Rask](https://github.com/pal-tamas/rask) One Person Framework builds its own
surfaces from — the operator console, the landing site and the docs showcase all draw with these.

- **Mobile-first, not merely responsive.** Every control takes a 44px touch target below `sm`; the tab
  bar scrolls sideways rather than wrapping, so the header is always exactly one row tall; a detail sheet
  is a bottom sheet on a phone and a centred card above it.
- **Ships no JavaScript.** A breadcrumb switcher is a real `<select>` with the chrome stripped off it, so
  it is keyboard-navigable, announces itself correctly, and opens the platform's own picker on a phone.
  Overlays are a state flip on the owning page, so they work identically on the Server transport and in
  WebAssembly.
- **The stylesheet comes with it.** Tailwind scans the project it runs in, so a compiled library's class
  names are invisible to *your* Tailwind build and would emit nothing. This package compiles its own
  sheet and hands it to you — there is nothing to configure, no static web assets, and no `_content/`
  path to map.
- **Re-skinned by tokens, not overrides.** The palette is `--color-ui-*` custom properties. Redefine any
  of them in your own `@theme` and every component follows, without a single rule being overridden.

## Use

```bash
dotnet add package Rask.Ui
```

Inline the kit's stylesheet **before** your own, then compose:

```csharp
protected override Component? HeadAssets =>
[
    Style[Raw.Value(UiStylesheet.Css)],   // the kit's, first
    Link.Rel("stylesheet").Href("/css/app.css"),
];

protected override Component? Render() =>
    UiShell[
        UiTopBar.Trailing(UiTopLink.Label("Docs").Href("https://rask.sh/docs/"))[
            UiBrand.Label("Acme").Href(Routes.Home())
        ],
        UiNav[
            UiNavTab.Label("Overview").Href(Routes.Home()).Active(true),
            UiNavTab.Label("Reports").Href(Routes.Reports())
        ],
        UiMain[
            UiMetricRow.Columns(4)[
                UiMetric.Label("Outstanding").Value("0"),
                UiMetric.Label("Failed").Value("3").Tone(UiTone.Danger)
            ]
        ]
    ];
```

Order matters: your `@theme` only wins while it is the copy the cascade reads last. The kit's sheet
deliberately carries **no preflight** and no `html`/`body` rules — your application owns its document,
and a reset arriving from a library restyles pages that never asked for it.

## What is in it

| | |
| --- | --- |
| Chrome | `UiShell` `UiTopBar` `UiBrand` `UiNav` `UiNavTab` `UiCrumbSwitcher` `UiCrumbSeparator` `UiTopLink` `UiMain` |
| Controls | `UiButton` `UiSearch` `UiStatusDot` |
| Data | `UiMetricRow` `UiMetric` `UiDetailList` `UiDetailRow` `UiCode` |
| Overlays | `UiModal` `UiToast` |
| Other | `UiIcon` / `UiIconName`, `UiTone`, `UiStylesheet` |

Requires .NET 10. Runs on both the ASP.NET host and browser-WebAssembly.

## Links

- [Repository](https://github.com/pal-tamas/rask)
- [Documentation](https://rask.sh/docs/)
