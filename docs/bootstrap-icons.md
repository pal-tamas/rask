# Bootstrap — icons

`BsIcon` from [`Rask.Bootstrap`](bootstrap.md) is a typed wrapper over every Bootstrap Icons glyph.
`BsIconName` is a generated enum with one member per icon, so the glyph you want is discoverable by
IntelliSense instead of a stringly-typed class:

```csharp
BsIcon.Name(BsIconName.HeartFill).Color(BsColor.Danger)
BsIcon.Name(BsIconName.Gear)
```

The icon font ships with the package and is linked by `BootstrapStyles()` (pass
`BootstrapStyles(Icons: false)` to skip it).

## Live example

The typed `BsIcon` over every Bootstrap Icons glyph:

<!-- demo:bootstrap-icons -->
