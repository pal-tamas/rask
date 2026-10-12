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
    public Callback<int> OnRate { get; set; }

    protected override Component? Render() =>
        Div.Class("inline-flex gap-1")[
            Enumerable.Range(1, 5).Select(i => Button.Key(i).OnClick(() => Rate(i))[i <= Value ? "★" : "☆"])
        ];

    // Raise the event. Unset, it does nothing; a synchronous handler completes without a Task.
    private async Task Rate(int n) => await OnRate.Invoke(n);
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

**A callback runs for the component that wrote it.** The lambda is the parent's code, so the calls it
makes that pass no token — `await Product.Where(…)`, `Cache.Remember(…)` — and the parent's own
`CancellationToken` are cancelled when the *parent* leaves the page, not when the child that invoked it
does. That is what lets a callback take the child off the page and carry on:

```csharp
_editing ? Editor.Record(_record).OnSaved(async () =>
{
    _editing = false;     // the editor is unmounted
    await Reload();       // still the page's work: it runs to the end
}) : null
```

The owner is whoever *wrote* the lambda, however many components handed it on (`.OnSaved(OnSaved)`), and
it holds from a child's event handler and from its lifecycle hook alike. Once the callback returns, the
child's code is back under its own lifetime. Only the cancellation changes hands: the user, the services
and a configured `HandlerTimeout` are still the event's, so under a timeout the callback is cancelled by
its owner leaving *or* the timeout passing, whichever is first. The same rule as for re-rendering applies:
a lambda over plain locals, a static method, or a `new Callback(…)` built by hand has no owner and runs
for whoever invokes it. See [cancellation](lifecycle.md#cancellation-tied-to-component-lifetime).

**Why a `Callback` and not a plain delegate.** The chain's receiver is the component itself, so
`.OnClick(Save)` is looked up on the component. A delegate-typed property there would be *invocable*: C#
would read the call as invoking the property (CS1593) and never reach the setter of the same name.
`Callback` and `Callback<T>` are **structs**, not delegates, so lookup falls through to the step, and the
one property takes both handler shapes — there is no `OnXxxAsync` twin anywhere on the surface. The same
holds for a value the framework *asks* a component for rather than an event: a template or a selector
(`Ui.DataGrid`'s `RowClass`) is an `Fn<…>`, called during the render and
never auto-wrapped. An event declared as `Action<T>` or `Func<T, Task>` is [RASK096](diagnostics.md#rask096),
an error whose quick-fix makes it a `Callback<T>`.

Declare the event non-nullable — `public Callback<int> OnRate { get; set; }` — and call it back with
`await OnRate.Invoke(i);`. An unset callback is a no-op, so there is nothing to null-check, and it is never a
required step: leaving `.OnRate(…)` off the chain is how a caller says it does not care. `Invoke` returns a
`ValueTask` that is already complete for a synchronous handler, so the sync path never acquires a `Task`.
Ask `OnRate.HasValue` only when *whether* anyone listens changes what you render. A `Callback<int>?` you
already wrote still works. **Wrapping is unchanged:** a component callback is auto-wrapped, a DOM handler
is not.

**DOM events on elements.** The events are **generated from MDN's data**: every event MDN's
`GlobalEventHandlers` lists that ships in two browser engines, on **every** element, named the way MDN
names it (`click` → `OnClick`, `dblclick` → `OnDblClick`) and carrying MDN's own event type, inheritance
included — `PointerEvent : MouseEvent : UIEvent : Event`, with MDN's member names (`e.ClientX`,
`e.ShiftKey`). Every event is **one** property, `OnXxx`, typed `Callback<TEvent>`, and the step takes
the event **or nothing**, sync or async:

```csharp
Button.OnClick(Refresh)                        // () => … — the event is optional
Button.OnClick(e => _shift = e.ShiftKey)       // e is MDN's PointerEvent
Button.OnClick(async () => await Save())  // async — awaited before the re-render
Div.OnScroll(e => _top = e.Target!.ScrollTop)  // the target's state, as JavaScript reads it
```

A component's own argument-less `Callback` forwards straight in — `Button.OnClick(OnClick)` — and a
handler typed to a base event (`Action<MouseEvent>`) still receives the whole `PointerEvent`. Each
property's doc comment carries the browser versions and links to MDN and the spec.

There is nothing to choose between and no pair to get wrong: one name, one slot. An `async` lambda
binds the asynchronous overload, never async void. Writing the step twice is simply a duplicated step
([RASK044](diagnostics.md#rask044)) — the last one wins, as with any other step.

Pass a **bare lambda or method group** — `.OnMouseMove(e => { _x = e.OffsetX; })`, `.OnKeyDown(OnKey)`
— never `new Action<T>(…)`: the step already gives the lambda its type. The surface:

- **Mouse & pointer** — `OnClick`/`OnAuxClick`/`OnContextMenu` (`PointerEvent`), `OnDblClick` and
  `OnMouseDown`/`Up`/`Move`/`Enter`/`Leave`/`Over`/`Out` (`MouseEvent`), `OnPointerDown`/`Up`/`Move`/…
  (`PointerEvent`), `OnWheel` (`WheelEvent`).
- **Touch** — `OnTouchStart`/`End`/`Move`/`Cancel` (`TouchEvent`: `Touches`, `TargetTouches`,
  `ChangedTouches` — lists of `Touch` in MDN's shape).
- **Keyboard** — `OnKeyDown`/`OnKeyUp` (`KeyboardEvent`: `Key` `"Escape"`, `Code` `"KeyA"`,
  `ShiftKey`/`CtrlKey`/`AltKey`/`MetaKey`, `Repeat`). Never `preventDefault`-ed, so handlers compose
  with normal typing. Compare against **`Keys`** and **`Codes`** rather than string literals: one
  constant per value [UI Events](https://w3c.github.io/uievents-key/) defines (`Keys.Escape`,
  `Keys.ArrowDown`, `Codes.KeyQ`, `Codes.Space`), generated with the elements, so a typo is a compile
  error and they work as patterns and switch cases — `e.Key is Keys.Escape`, `case Keys.Home or Keys.PageUp:`.
  A key that types a character reports the character (`"q"`, `" "`), which no constant names.
- **Clipboard** — `OnCopy`/`OnCut`/`OnPaste` (`ClipboardEvent.ClipboardData.GetData("text/plain")`).
- **Focus, forms, drag** — `OnFocus`/`OnBlur`/`OnFocusIn`/`OnFocusOut` (`FocusEvent`),
  `OnBeforeInput` (`InputEvent`: `Data`, `InputType`), `OnSelect`/`OnInvalid`/`OnReset`, and the drag
  events (`DragEvent`).
- **Open state** — `OnToggle`/`OnBeforeToggle` (`ToggleEvent`: `OldState`/`NewState`, `"open"` or
  `"closed"`); a `<dialog>`'s `OnCancel` (Escape or a light dismiss) and `OnClose` (any close, after
  `OnCancel`).
- **Scroll & media** — MDN's `scroll` and media events are plain `Event`s, so their state travels as
  the target's, the way JavaScript reads `e.target`: `e.Target.ScrollTop`/`ClientHeight`/`ScrollHeight`
  (rAF-coalesced) and `e.Target.CurrentTime`/`Duration`/`Paused`/`Volume`/`PlaybackRate`/… for
  `OnTimeUpdate`, `OnPlay`, `OnVolumeChange` and the rest.

The quirks come from MDN's data too: an event that does not bubble (`focus`, `scroll`, the media
events) reaches only the element it fired on, and only `contextmenu`/`dragover`/`drop` — which need it
to work at all — are `preventDefault`-ed; every other listener is passive.

You never name the `Callback`: you pass the lambda or method group and the step does the rest. It exists
so a property and its builder setter can share a name — a delegate-typed property *is* invocable, which would make
`.OnClick(Save)` try to call the handler (CS1593). Reading a handler back off an element is the one
place it shows: `await el.OnClick.Invoke(e)`. DOM handlers are **never** auto-wrapped — they go straight to the
DOM, where handler-owner resolution already re-renders the owner.

All of these are delegated by a single capture-phase listener per event in the shared client module
(`rask-events.ts`, imported by both the Server and WASM runtimes), so there is no per-element JS. The
Todos sample uses `OnKeyDown` to close its dialog on Escape (`e.Key is Keys.Escape`) (it focuses the `<dialog>` on open via an
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
    _rows = await _api.Load(CancellationToken))["Load"]
```

It is cooperative: a handler that ignores the token (or runs unbounded synchronous work) can't be
force-aborted — that's a .NET reality, not a Rask limitation. In a lifecycle hook (no handler
dispatch) the token is simply the component's lifetime token.

A child raises an event through a `Callback<T>` prop, fired with `await OnRate.Invoke(n)`; the framework wraps
the parent's handler so the click re-renders the owning parent — no `StateHasChanged`:

<!-- demo:callback-rating -->

---

## Context: provide / consume

Context passes a value from high in the tree to a deep consumer **without prop
drilling** — React's provide/consume, type-erased so it stays trim-safe.

```csharp
// Provide near the top. `Provide<T>` is a transparent node; children render under it.
Context.Provide(_theme)[
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

Context answers only while `Render()` runs. A task you start there that reads it later finds no provider,
so read the value in `Render()` and hand it to the task.

**Nearest provider wins**, matched by optional `Name:` plus `IsAssignableFrom` — so you
can **provide a concrete type and consume by an interface**. A provider supplying `null`
still resolves (it is a real provider of `null`).

**Reactivity:** reading a context value latches the consumer out of the render cache, so
it re-reads when the provider re-renders — *even through a render-cached intermediate*
that never re-renders itself. That is the whole point: `ThemeCard` above is cached after
first paint, yet the `ThemeBadge` it nests still updates on every toggle.

<!-- demo:context-theme -->
