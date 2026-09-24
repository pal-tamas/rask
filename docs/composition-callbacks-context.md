# Composition — callbacks & context

Sending events up from a child and passing values down to deep consumers without prop drilling.

‹ Back to [Composition](composition.md)

## Callbacks: child → parent

**For parent callbacks, Rask has no Blazor-style `EventCallback` wrapper to thread through.** A child
raises an event up to its parent with a `Callback` property (or `Callback<T>` when the event carries a
value), and the caller hands it **either** a synchronous or an asynchronous handler — `Action`/`Action<T>`
or `Func<Task>`/`Func<T, Task>` — through one step. DOM event handlers further down use the same type.
The chain step wraps the handler so that **invoking it re-renders the parent that owns it**, with no
`StateHasChanged` threaded through by hand.

```csharp
// Component: declares the event as a Callback<T> prop and invokes it.
public sealed partial class RatingStars : Component
{
    public int Value { get; set; }
    public Callback<int>? OnRate { get; set; }

    protected override Component? Render() =>
        Div.Class("inline-flex gap-1")[
            Enumerable.Range(1, 5).Select(i => Button.Key(i).OnClick(() => Rate(i))[i <= Value ? "★" : "☆"])
        ];

    // Raise the event. Invoke returns null for a synchronous handler — nothing to await.
    private Task Rate(int n) => OnRate?.Invoke(n) ?? Task.CompletedTask;
}

// Parent: passes a lambda that mutates its own state.
public sealed partial class RatingDemo : Component
{
    private int _rating;

    protected override Component? Render() =>
        Div[
            RatingStars.Value(_rating).OnRate(n => _rating = n),   // re-renders the parent
            P[_rating == 0 ? "Click a star." : $"You rated {_rating}/5"]
        ];
}
```

**When a callback is auto-wrapped** (so invoking it re-renders the owner): its handler
returns `void` or `Task`, it takes **0 or 1** arguments, and the declaring component is
**not** an `Element` subclass (so DOM handlers like `Button.OnClick` stay on the free
fast path). It must also be over a member of a `Component` — write the lambda *inside*
the component so it captures `this`. A lambda over a plain local, or a static method,
returns unchanged and does **not** trigger a re-render.

Auto-wrapped callbacks are excluded from the `propsChanged` diff — changing only the
lambda identity between renders does not refire `OnUpdated`.

**Why a `Callback` and not a plain delegate.** The chain's receiver is the component itself, so
`.OnClick(Save)` is looked up on the component. A delegate-typed property there would be *invocable*: C#
would read the call as invoking the property (CS1593) and never reach the setter of the same name.
`Callback` and `Callback<T>` are **structs**, not delegates, so lookup falls through to the step, and the
one property takes both handler shapes — there is no `OnXxxAsync` twin anywhere on the surface. The same
holds for a value the framework *asks* a component for rather than an event: a template or a selector
(`Ui.DataGrid`'s `RowClass`, `Ui.Select`'s `OptionTemplate`) is an `Fn<…>`, called during the render and
never auto-wrapped.

Calling one back: `if (OnRate?.Invoke(i) is { } t) await t;` — `Invoke` returns `null` for a synchronous
handler, so the sync path never acquires a `Task`. **Wrapping is unchanged:** a component callback is
auto-wrapped, a DOM handler is not.

**DOM events on elements.** `Element` exposes the full DOM **`GlobalEventHandlers`** surface — so
**every** element (not a hand-picked few) carries the complete event set, just like the real DOM
mixin. Every event is **one** property, `OnXxx`, typed `Callback` (or `Callback<TArgs>`), and the step
takes **either shape**:

```csharp
Button.OnClick(Refresh)                        // sync
Button.OnClick(async () => await SaveAsync())  // async — awaited before the re-render
```

There is nothing to choose between and no pair to get wrong: one name, one slot. An `async` lambda
binds the asynchronous overload, never async void. Writing the step twice is simply a duplicated step
([RASK044](diagnostics.md#rask044)) — the last one wins, as with any other step.

Pass a **bare lambda or method group** — `.OnMouseMove(e => { _x = e.OffsetX; })`, `.OnKeyDown(OnKey)`
— never `new Action<T>(…)`: the step already gives the lambda its type. The surface:

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
- **Open state** — `OnToggle`/`OnBeforeToggle` (`ToggleEventArgs`: `OldState`/`NewState`/`IsOpen`) for a
  popover or `<details>`; a `<dialog>`'s `OnCancel` (a dismissal — Escape or a light dismiss) and `OnClose` (any
  close, after `OnCancel`), both parameterless. None of them can veto the change: the client never
  `preventDefault`s.
- **Media** — `Audio`/`Video` add the `HTMLMediaElement` events `OnPlay`/`OnPause`/`OnEnded`/
  `OnTimeUpdate`/`OnVolumeChange`/… (`MediaEventArgs`: current time, duration, paused, volume, …).

You never name the `Callback`: you pass the lambda or method group and the step does the rest. It exists
so a property and its builder setter can share a name — a delegate-typed property *is* invocable, which would make
`.OnClick(Save)` try to call the handler (CS1593). Reading a handler back off an element is the one
place it shows: `el.OnClick?.Invoke()`. DOM handlers are **never** auto-wrapped — they go straight to the
DOM, where handler-owner resolution already re-renders the owner.

All of these are delegated by a single capture-phase listener per event in the shared client module
(`rask-events.ts`, imported by both the Server and WASM runtimes), so there is no per-element JS. The
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
Button.OnClick(async () =>
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
Context.Provide<Theme>(_theme)[
    ThemeCard        // knows nothing about Theme — no prop passed through it
]

// Consume anywhere below, in Render():
public sealed partial class ThemeBadge : Component
{
    protected override Component? Render()
    {
        var theme = Context.Required<Theme>();   // throws if no provider
        return Span.Class(theme.IsDark ? "badge bg-dark" : "badge bg-light")[theme.Name];
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
