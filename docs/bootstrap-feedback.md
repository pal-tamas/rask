# Bootstrap — alerts, spinners & progress

The status and feedback components from [`Rask.Bootstrap`](bootstrap.md) — `BsAlert` (dismissible,
with the close driven by controlled state), `BsSpinner` (`BsSpinnerKind.Border`/`.Grow`), and
`BsProgress`.

```csharp
BsAlert.Color(BsColor.Warning).Open(_show).OnClose(() => _show = false)["Heads up!"]
BsSpinner.Kind(BsSpinnerKind.Border).Color(BsColor.Primary)
BsProgress.Value(60)
```

## Live example

Dismissible alerts — the close is controlled state, driven entirely by Rask's live runtime,
**no `bootstrap.js`**:

<!-- demo:bootstrap-alerts -->

`BsSpinner` — border and grow, in theme colours and a compact size, each with a visually-hidden
status label for assistive tech:

<!-- demo:bootstrap-spinner -->

`BsProgress` — the fill, colour, and striped/animated treatment, with `role`/`aria` on the outer
`.progress` (Bootstrap 5.3):

<!-- demo:bootstrap-progress -->
