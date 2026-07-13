# Bootstrap — buttons & badges

Buttons and badges from [`Rask.Bootstrap`](bootstrap.md) — `BsButton`, `BsButtonGroup`, `BsBadge`,
and `BsCloseButton`. Typed `BsColor`/`BsSize` variants replace stringly-typed class strings.

```csharp
BsButton(Color: BsColor.Primary, Size: BsSize.Lg)["Save"]
BsButton(Color: BsColor.Danger, Outline: true)["Delete"]
BsButtonGroup()[ BsButton()["Left"], BsButton()["Right"] ]
BsBadge(Color: BsColor.Success)["New"]
```

## Live example

Buttons and badges, driven entirely by Rask's live runtime — **no `bootstrap.js`**:

<!-- demo:bootstrap-buttons -->
