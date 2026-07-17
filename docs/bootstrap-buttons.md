# Bootstrap — buttons & badges

Buttons and badges from [`Rask.Bootstrap`](bootstrap.md) — `BsButton`, `BsLink`, `BsButtonGroup`,
`BsBadge`, and `BsCloseButton`. Typed `BsColor`/`BsSize` variants replace stringly-typed class strings.

```csharp
BsButton(Color: BsColor.Primary, Size: BsSize.Lg)["Save"]
BsButton(Color: BsColor.Danger, Outline: true)["Delete"]
BsButtonGroup()[ BsButton()["Left"], BsButton()["Right"] ]
BsBadge(Color: BsColor.Success)["New"]
```

`BsButton` wraps a `<button>` (an in-page action). For a real link (navigation, an external URL)
styled as a Bootstrap button, use **`BsLink`** — the same typed `Color`/`Outline`/`Size`/`Active`
props over an `<a>` with `Href`/`Target`/`Rel`:

```csharp
BsLink(Href: "/docs", Color: BsColor.Primary)["Read the docs"]
BsLink(Href: "https://github.com/pal-tamas/rask", Target: "_blank", Rel: "noopener",
    Color: BsColor.Light, Outline: true)[BsIcon(Name: BsIconName.Github, Class: "me-1"), "GitHub"]
```

## Live example

Buttons and badges, driven entirely by Rask's live runtime — **no `bootstrap.js`**:

<!-- demo:bootstrap-buttons -->
