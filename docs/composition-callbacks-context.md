# Composition — callbacks & context

Sending events up from a child and passing values down to deep consumers without prop drilling.

‹ Back to [Composition](composition.md)

## Callbacks: child → parent

**For parent callbacks, Rask has no Blazor-style `EventCallback` wrapper.** A child raises an
event up to its parent with a plain delegate property — `Action`, `Action<T>`, `Func<Task>`, or
`Func<T, Task>`. (DOM event handlers further down use the named `Callback<T>` / `CallbackAsync<T>`
delegate types, but you still never *construct* one — see below.) The generated factory wraps the delegate so that **invoking it
re-renders the parent that owns it**, with no `StateHasChanged` threaded through by hand.

```csharp
// Component: declares the event as a delegate prop and invokes it.
public sealed class RatingStars : Component
{
    public int Value { get; set; }
    public Action<int>? OnRate { get; set; }

    protected override Component? Render() =>
        Div(Class: "d-inline-flex gap-1")[
            Enumerable.Range(1, 5).Select(i => (Component)Button(
                OnClick: () => OnRate?.Invoke(i),   // raise the event
                Key: i)[i <= Value ? "★" : "☆"])
        ];
}

// Parent: passes a lambda that mutates its own state.
public sealed class RatingDemo : Component
{
    private int _rating;

    protected override Component? Render() =>
        Div()[
            RatingStars(Value: _rating, OnRate: n => _rating = n),   // re-renders the parent
            P()[_rating == 0 ? "Click a star." : $"You rated {_rating}/5"]
        ];
}
```

**When a delegate is auto-wrapped** (so invoking it re-renders the owner): its `Invoke`
returns `void` or `Task`, it takes **0 or 1** arguments, and the declaring component is
**not** an `Element` subclass (so DOM handlers like `Button.OnClick` stay on the free
fast path). It must also be over a member of a `Component` — write the lambda *inside*
the component so it captures `this`. A lambda over a plain local, or a static method,
returns unchanged and does **not** trigger a re-render.

Auto-wrapped delegates are excluded from the `propsChanged` diff — changing only the
lambda identity between renders does not refire `OnPropsChanged`.

**DOM events on elements.** `Element` exposes the full DOM **`GlobalEventHandlers`** surface — so
**every** element (not a hand-picked few) carries the complete event set, just like the real DOM
mixin. Every event ships a **typed sync + async pair** — a synchronous `OnXxx` (`Callback<TArgs>`)
and an asynchronous `OnXxxAsync` (`CallbackAsync<TArgs>`); set **at most one** per event (wiring both
is a compile error, [RASK027](diagnostics.md#rask027) — the runtime would keep the sync one and drop
the async). Pass a **bare lambda or method group** — `OnMouseMove: e => { _x = e.OffsetX; }`,
`OnKeyDown: OnKey` — never `new Callback<T>(…)`: the named parameter already gives the lambda its
type, exactly like `OnClick: () => _count++`. The surface:

- **Mouse** — `OnClick` (parameterless), `OnDoubleClick`, `OnContextMenu`, `OnMouseDown`/`Up`/`Move`/
  `Enter`/`Leave`/`Over`/`Out`, all taking `MouseEventArgs` (button/buttons, client/screen/page/offset/
  movement coords, modifiers).
- **Wheel** — `OnWheel` (`WheelEventArgs`: the mouse geometry plus `DeltaX/Y/Z` + `DeltaMode`).
- **Pointer & touch** — `OnPointerDown`/`Up`/`Move`/`Enter`/`Leave`/`Over`/`Out`/`Cancel`
  (`PointerEventArgs`: mouse geometry + `PointerId`/`Pressure`/`PointerType`/`IsPrimary`/tilt);
  `OnTouchStart`/`End`/`Move`/`Cancel` (`TouchEventArgs`).
- **Focus** — `OnFocus`/`OnBlur`/`OnFocusIn`/`OnFocusOut` (parameterless; reach the element via
  capture-phase delegation).
- **Keyboard** — `OnKeyDown`/`OnKeyUp` (`KeyboardEventArgs`: `Key` `"Escape"`, `Code` `"KeyA"`, the
  `Shift`/`Ctrl`/`Alt`/`Meta` modifiers, `Repeat`). Focus-scoped; never `preventDefault`-ed, so
  handlers compose with normal typing.
- **Clipboard** — `OnCopy`/`OnCut`/`OnPaste` (`ClipboardEventArgs.Text`).
- **Scroll & drag** — `OnScroll` (`ScrollEvent`, rAF-coalesced); `OnDragStart`/`Over`/`Drop`/`End`
  plus `OnDrag`/`OnDragEnter`/`OnDragLeave` (parameterless — the dragged item's identity rides the
  handler's closure).
- **Forms** — `OnBeforeInput` (`Callback<string>`), `OnSelect`, `OnInvalid`, `OnReset`.
- **Media** — `Audio`/`Video` add the `HTMLMediaElement` events `OnPlay`/`OnPause`/`OnEnded`/
  `OnTimeUpdate`/`OnVolumeChange`/… (`MediaEventArgs`: current time, duration, paused, volume, …).

All of these are delegated by a single capture-phase listener per event in the shared client module
(`rask-events.js`, spliced into both the Server and WASM runtimes), so there is no per-element JS. The
Todos sample uses `OnKeyDown` to close its dialog on Escape (it focuses the `<dialog>` on open via an
`ElementRef`, since a diff-inserted element never fires the HTML `autofocus` attribute).

The full surface, live — every readout updates from a plain field mutation, no `StateHasChanged`:

<!-- demo:events -->

And the everyday handlers on their own — a click counter, `onInput`, `onChange` on a `<select>`, and
`onSubmit` (which receives a `FormData` of the named fields):

<!-- demo:events-click -->

<!-- demo:events-input -->

<!-- demo:events-select -->

<!-- demo:events-form -->

**Cancelling async work.** `Component.CancellationToken` is cancelled when the component unmounts —
and, *while an event handler is running*, **also** when the host cancels that dispatch (the server's
optional `RaskServerOptions.HandlerTimeout` elapsing, or the socket closing). Thread it into the
cancellable async work a handler or lifecycle hook starts, so the work aborts when the component goes
away and a slow handler unwinds instead of pinning the session's render pipeline:

```csharp
Button(OnClickAsync: async () =>
    _rows = await _api.LoadAsync(CancellationToken))["Load"]
```

It is cooperative: a handler that ignores the token (or runs unbounded synchronous work) can't be
force-aborted — that's a .NET reality, not a Rask limitation. In a lifecycle hook (no handler
dispatch) the token is simply the component's lifetime token.

A child raises an event through a plain delegate prop; the framework wraps it so the click re-renders
the owning parent — no `StateHasChanged`:

<!-- demo:callback-rating -->

---

## Context: provide / consume

Context passes a value from high in the tree to a deep consumer **without prop
drilling** — React's provide/consume, type-erased so it stays trim-safe.

```csharp
// Provide near the top. `Provide<T>` is a transparent node; children render under it.
Context.Provide<Theme>(Value: _theme)[
    ThemeCard()        // knows nothing about Theme — no prop passed through it
]

// Consume anywhere below, in Render():
public sealed class ThemeBadge : Component
{
    protected override Component? Render()
    {
        var theme = Context.Required<Theme>();   // throws if no provider
        return Span(Class: theme.IsDark ? "badge bg-dark" : "badge bg-light")[theme.Name];
    }
}
```

Read APIs (call inside `Render()`):

| Call | Behaviour |
|------|-----------|
| `Context.Get<T>()` | nearest value, or `null` if no provider |
| `Context.Required<T>()` | nearest value, or throws |
| `Context.Has<T>()` | `true` if a provider exists |

**Nearest provider wins**, matched by optional `Name:` plus `IsAssignableFrom` — so you
can **provide a concrete type and consume by an interface**. A provider supplying `null`
still resolves (it is a real provider of `null`).

**Reactivity:** reading a context value latches the consumer out of the render cache, so
it re-reads when the provider re-renders — *even through a render-cached intermediate*
that never re-renders itself. That is the whole point: `ThemeCard` above is cached after
first paint, yet the `ThemeBadge` it nests still updates on every toggle.

<!-- demo:context-theme -->
