# Bootstrap — toasts

`BsToast` from [`Rask.Bootstrap`](bootstrap.md) is a controlled toast: shown, stacked, dismissed and
auto-hidden entirely from Rask state (no `bootstrap.js`, no `data-bs-dismiss`, no `setTimeout`).

`BsToaster` is the ready-made outlet that drains Rask's `IToaster` messages (see
[composition](composition.md)) — drop it once in your layout and inject `IToaster` anywhere to raise
transient messages that survive a client-side navigation.

```csharp
// In the layout, once:
BsToaster

// Anywhere, via the injected IToaster:
toaster.Show("Saved.", ToastLevel.Success);
```

## Live example

Toasts shown, stacked, dismissed and auto-hidden entirely from Rask state — **no `bootstrap.js`**:

<!-- demo:bootstrap-toast -->
