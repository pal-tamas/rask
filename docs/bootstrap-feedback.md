# Bootstrap — alerts, spinners & progress

The status and feedback components from [`Rask.Bootstrap`](bootstrap.md) — `BsAlert` (dismissible,
with the close driven by controlled state), `BsSpinner` (`BsSpinnerKind.Border`/`.Grow`), and
`BsProgress`.

```csharp
BsAlert(Color: BsColor.Warning, Open: _show, OnClose: () => _show = false)["Heads up!"]
BsSpinner(Kind: BsSpinnerKind.Border, Color: BsColor.Primary)
BsProgress(Value: 60)
```

## Live example

Dismissible alerts — the close is controlled state, driven entirely by Rask's live runtime,
**no `bootstrap.js`**:

<!-- demo:bootstrap-alerts -->
