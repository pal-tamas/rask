# Composition: context, callbacks, children & lists

How Rask components talk to each other — passing data down without prop drilling,
sending events up, nesting children, and rendering windowed or reorderable lists.

- [Children & fragments](#children--fragments)
- [Callbacks: child → parent](#callbacks-child--parent)
- [Context: provide / consume](#context-provide--consume)
- [Virtualize: windowed lists](#virtualize--windowed-lists)
- [Drag and drop](#drag-and-drop)

---

## Children & fragments

Children attach through the **indexer**, not a `Children:` parameter:

```csharp
Div(Class: "card")[
    Span(Class: "title")["Hello"],
    "plain text becomes a Text node",   // string → Text (HTML-encoded)
    items.Select(i => (Child)Li(Key: i.Id)[i.Name])
]
```

> **Keys must be unique among siblings.** `Key:` is the reconciliation identity the live diff
> uses to move a row instead of rebuilding it. If two siblings share a key, keyed
> reconciliation can't tell them apart, so the diff falls back to a positional walk that may
> attach a row's DOM state (focus, input value, scroll) to the wrong sibling on reorder. The
> diff codec writes a one-time `data-rask-key="…"` warning to standard error when it detects a
> duplicate — treat it as a bug to fix, not noise.

`Fragment()` renders its children with **no wrapping element** — use it for a list of
siblings, or as the conditional "render nothing" branch:

```csharp
show ? Panel() : (Child)Fragment()    // Fragment() renders nothing
```

> The `..` spread fails inside `[…]` (the compiler parses it as a `Range`). Pass the
> enumerable directly — `Div()[items]` — instead of `Div()[..items]`.

The page root is itself a fragment that renders the full shell:

```csharp
protected override RenderResult Render() =>
    Fragment()[Doctype(), Html()[Head()[Title()["My app"]], Body()[ /* … */ ]]];
```

---

## Callbacks: child → parent

**For parent callbacks, Rask has no Blazor-style `EventCallback` wrapper.** A child raises an
event up to its parent with a plain delegate property — `Action`, `Action<T>`, `Func<Task>`, or
`Func<T, Task>`. (DOM event handlers further down use the named `Callback<T>` / `CallbackAsync<T>`
delegate types, but you still never *construct* one — see below.) The generated factory wraps the delegate so that **invoking it
re-renders the parent that owns it**, with no `StateHasChanged` threaded through by hand.

```csharp
// Child: declares the event as a delegate prop and invokes it.
public sealed class RatingStars : Component
{
    public int Value { get; set; }
    public Action<int>? OnRate { get; set; }

    protected override RenderResult Render() =>
        Div(Class: "d-inline-flex gap-1")[
            Enumerable.Range(1, 5).Select(i => (Child)Button(
                OnClick: () => OnRate?.Invoke(i),   // raise the event
                Key: i)[i <= Value ? "★" : "☆"])
        ];
}

// Parent: passes a lambda that mutates its own state.
public sealed class RatingDemo : Component
{
    private int _rating;

    protected override RenderResult Render() =>
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
`ElementRef`, since a diff-inserted element never fires the HTML `autofocus` attribute). See the
**DOM events** showcase page for a live demo.

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
    protected override RenderResult Render()
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

Runnable demo: [`samples/Rask.Example.Shared/Features/Context/ContextThemeDemo.cs`](../samples/Rask.Example.Shared/Features/Context/ContextThemeDemo.cs).

---

## Virtualize — windowed lists

`VirtualizeModel<T>` is **headless**: *you* render the scroll container and rows from a
`VirtualizationState ctx`, and it tells you which slice is visible. The first argument is
the body builder; pass **exactly one** of `Items` (positional or named) or `ItemsProvider`,
plus `ItemSize` (row height in px, required) and optional `OverscanCount`.

```csharp
VirtualizeModel<Row>(
    ctx => Div(Style: "height:400px; overflow:auto;", OnScroll: ctx.OnScroll)[
        Div(Style: $"height:{ctx.OffsetBefore}px"),          // top spacer
        Table()[Tbody()[
            ctx.VisibleItems.Select(item => Tr(
                Style: $"height:{ctx.ItemSize}px;",
                Data: new() { ["rask-key"] = item.Index.ToString() })[  // key → reuse <tr> on scroll
                Td()[item.IsPlaceholder ? "—" : item.Value!.Name])
        ]],
        Div(Style: $"height:{ctx.OffsetAfter}px")             // bottom spacer
    ],
    _rows,                 // Items (in memory)
    ItemSize: 32,
    OverscanCount: 4)
```

For lazy / server-paged data pass `ItemsProvider:` instead of the items list. **The
provider must propagate the `CancellationToken` it receives** or it will leak in-flight
requests:

```csharp
ItemsProvider: async req =>
{
    var page = await _api.GetRowsAsync(req.StartIndex, req.Count, req.CancellationToken);
    return new ItemsProviderResult<Row>(page.Items, page.TotalCount);
}
```

Provider mode caches by index, marks rows `IsPlaceholder` while a page is in flight, and
cancels + disposes superseded requests (and on unmount).

Runnable demo: [`samples/Rask.Example.Shared/Features/Virtualize/VirtualizePage.cs`](../samples/Rask.Example.Shared/Features/Virtualize/VirtualizePage.cs).

---

## Flash messages

`IFlash` is Rask's take on Rails' `flash` — transient, consumed-once user messages that survive a
client-side navigation. Inject it and queue a message; a single `FlashOutlet` shows it once.

```csharp
public sealed class SavePage(IFlash flash, Navigator nav) : Component
{
    private void Save()
    {
        // ... persist ...
        flash.Success("Your changes were saved.");   // Info / Warning / Error / Add(level, …) too
        nav.NavigateTo(Routes.ListPage());            // the message survives the navigation
    }
    // ...
}
```

Why it survives the navigation: `IFlash` is registered **scoped** per session (a Server WebSocket
session or a WASM app instance), and a client-side `NavigateTo` does not recreate the session — so a
message queued before navigating is still in the queue when the destination mounts.

Show them by mounting **one** outlet in your app layout. The headless `FlashOutlet` ships no markup —
you own it through `Template`, which receives the messages plus a `dismiss(id)` callback:

```csharp
FlashOutlet(Template: (messages, dismiss) =>
    Div()[messages.Select(m => (Child)Div(Class: "alert", Key: m.Id.ToString())[
        m.Message,
        Button(OnClick: () => dismiss(m.Id))["×"]])])
```

`FlashOutlet` calls `Consume()` (which drains the queue) on mount and whenever `IFlash.Changed` fires,
so each message is delivered to exactly one outlet and never reappears on a later render. `Rask.Bootstrap`
ships a ready-made `BsFlash` — a fixed toast-container of `BsToast`s; mount a single `BsFlash()` in your
layout instead of writing a `Template`. Runnable demo:
[`samples/Rask.Example.Shared/Features/Flash/FlashDemo.cs`](../samples/Rask.Example.Shared/Features/Flash/FlashDemo.cs).

## Drag and drop

A headless drag-and-drop primitive lives in `Rask.Core/DragAndDrop`. It tracks the
dragged item and the drop target and raises a callback when an item is dropped; you own
the visuals. See the runnable demo:
[`samples/Rask.Example.Shared/Features/DragDrop/DragDropPage.cs`](../samples/Rask.Example.Shared/Features/DragDrop/DragDropPage.cs)
(+ `DragDropPage.css`).

---

See also: [Lifecycle](lifecycle.md) for when `OnPropsChanged` refires, and
[JS interop](js-interop.md) for element refs and scoped JS.
