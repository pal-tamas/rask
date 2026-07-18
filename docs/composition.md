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
    items.Select(i => (Component)Li(Key: i.Id)[i.Name])
]
```

> **Keys must be unique among siblings.** `Key:` is the reconciliation identity the live diff
> uses to move a row instead of rebuilding it. If two siblings share a key, keyed
> reconciliation can't tell them apart, so the diff falls back to a positional walk that may
> attach a row's DOM state (focus, input value, scroll) to the wrong sibling on reorder. The
> diff codec reports a one-time `data-rask-key="…"` error (via the diagnostics seam) when it detects
> a duplicate — treat it as a bug to fix, not noise.

A `[...]` collection expression renders its items with **no wrapping element** — use it for a list
of siblings. For the conditional "render nothing" branch, return `null` (a `null` child renders
nothing):

```csharp
show ? Panel() : null    // null renders nothing
```

> The `..` spread fails inside `[…]` (the compiler parses it as a `Range`). Pass the
> enumerable directly — `Div()[items]` — instead of `Div()[..items]`.

The page root is itself a fragment that renders the full shell:

```csharp
protected override Component? Render() =>
    [Doctype(), Html()[Head()[Title()["My app"]], Body()[ /* … */ ]]];
```

---

## Component tiers: static method · stateless · stateful

There are **three ways** to author a reusable unit of UI, in ascending order of cost and
capability. Reach for the cheapest one that does the job.

<!-- demo:component-tiers -->

**Tier 0 — a plain static method.** Just a function that returns markup:

```csharp
internal static class Ui
{
    public static Component Badge(string label) => Span(Class: "badge")[label];
}
// call it like any method — Ui.Badge("new")
```

There is no `Component` instance, so it has **no state, no lifecycle hooks, and no independent
render cache** — its markup is inlined into whichever component calls it, on every render of that
caller. It is the leanest way to factor out repeated markup. Two things it *cannot* do: hold
mutable state, and safely consume ambient context — a static helper has no instance to carry the
"re-run me when context changes" latch, so a `Context.Get<T>()` inside one only refreshes when its
caller happens to re-render. The moment you need either, promote to Tier 1.

**Tier 1 — a stateless component.** A `Component` subclass whose `Render()` is a pure function of
its props, with **no mutable fields**:

```csharp
public sealed class Greeting : Component
{
    public required string Name { get; set; }   // non-nullable, no initializer → required factory param
    protected override Component? Render() => P()["Hello, ", Strong()[Name], "!"];
}
// call the generated factory by its bare name — Greeting(Name: "Ada")
```

Public settable props become a generated bare-name factory (see the factory rules in
[the README](../README.md) and [lifecycle.md](lifecycle.md)). Over a static method it gains a
reconciliation identity, the [lifecycle hooks](lifecycle.md), render caching, `<head>`
contribution, and safe context reads — it simply carries no local state.

**Tier 2 — a stateful component.** A `Component` subclass that keeps local state in **private
fields** and mutates them in handlers:

```csharp
public sealed class Counter : Component
{
    private int _count;
    protected override Component? Render() =>
        Button(OnClick: () => _count++)[$"Clicked {_count} times"];
}
```

The instance persists across renders (Rask reconciles it by `(type, sibling-position)` or by an
explicit `Key`), so the field survives. The `OnClick` lambda captures `this`, so after it runs the
framework re-renders this component **automatically — no `StateHasChanged()`** (the same auto-wrap
that powers [callbacks](#callbacks-child--parent)). You only call `StateHasChanged()` when the
mutation happens *off* the event-dispatch path (e.g. a background poll loop — see
[lifecycle.md](lifecycle.md)).

| Tier | You write | State | Lifecycle | Render cache | Factory | Context reads |
|------|-----------|-------|-----------|--------------|---------|----------------|
| **0 · static method** | `static Component Foo(…)` | none (inlined) | none | none | none — call it | ⚠️ only refreshes with the caller |
| **1 · stateless component** | subclass, props → `Render()`, no fields | none | ✅ | ✅ | ✅ generated | ✅ |
| **2 · stateful component** | subclass with private fields | private fields | ✅ | ✅ | ✅ generated | ✅ |

**Rule of thumb:** start with a static method; promote to a stateless component when you need an
identity, lifecycle, `<head>` assets, context-driven re-render, or a clean factory API; promote to
a stateful component when you need mutable local state.

---

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

**Items mode** — a fixed in-memory list, windowed:

<!-- demo:virtualize-items -->

**Provider mode** — rows fetched on demand as they scroll into view:

<!-- demo:virtualize-provider -->

---

## Keyed lists

A `Key:` on a list item gives it a stable identity across renders, so a reorder **moves** the live
DOM node (with its focus, caret, and uncommitted input) instead of detaching and re-creating it. This
is the same reconciliation identity the diff uses everywhere — not a reactive prop.

<!-- demo:keyed-lists-reorder -->

A **master-detail** grid is the same identity trick at work: each order row carries a `Key`, and expanding
one **inserts a keyed detail `<tr>`** right after it. The diff reconciles that as an in-place keyed insert
(collapse → remove), so the other open rows keep their own independently-sorted inner grid across the change
— no wholesale re-render of the table:

<!-- demo:master-detail -->

---

## Toast messages

`IToaster` is Rask's take on flash messages — transient, consumed-once user messages that survive a
client-side navigation. Inject it and queue a message; a single `ToastOutlet` shows it once.

```csharp
public sealed class SavePage(IToaster toast, Navigator nav) : Component
{
    private void Save()
    {
        // ... persist ...
        toast.Success("Your changes were saved.");   // Info / Warning / Error / Add(level, …) too
        nav.NavigateTo(Routes.ListPage());            // the message survives the navigation
    }
    // ...
}
```

Why it survives the navigation: `IToaster` is registered **scoped** per session (a Server WebSocket
session or a WASM app instance), and a client-side `NavigateTo` does not recreate the session — so a
message queued before navigating is still in the queue when the destination mounts.

Show them by mounting **one** outlet in your app layout. The headless `ToastOutlet` ships no markup —
you own it through `Template`, which receives the messages plus a `dismiss(id)` callback:

```csharp
ToastOutlet(Template: (messages, dismiss) =>
    Div()[messages.Select(m => (Component)Div(Class: "alert", Key: m.Id.ToString())[
        m.Message,
        Button(OnClick: () => dismiss(m.Id))["×"]])])
```

`ToastOutlet` calls `Consume()` (which drains the queue) on mount and whenever `IToaster.Changed` fires,
so each message is delivered to exactly one outlet and never reappears on a later render. Set
`AutoDismissAfter` to have each message clear itself after a delay — a one-shot timer per message that
runs the same dismiss path, so any `Template` auto-dismisses even when its element has no timer of its own.
`Rask.Bootstrap` ships a ready-made `BsToaster` — a fixed toast-container of `BsToast`s that auto-hide after
5 s by default (set `AutoHideMs: null` to keep them sticky); mount a single `BsToaster()` in your layout
instead of writing a `Template`. Queue one, show once (this demo auto-dismisses after 5 s):

<!-- demo:toaster -->

## Drag and drop

A headless drag-and-drop primitive lives in `Rask.Core/DragAndDrop`. It tracks the
dragged item and the drop target and raises a callback when an item is dropped; you own
the visuals — a sortable list and a kanban board built on the same primitive:

<!-- demo:drag-drop-sortable -->

<!-- demo:drag-drop-kanban -->

---

## Error boundaries

An error boundary catches an exception thrown by a descendant — during an event handler **or** during
render — and shows a fallback instead of tearing down the whole app. The nearest boundary handles it;
everything outside it (the navbar, the rest of the page) keeps running, and `Recover` restores the
healthy subtree. Boundaries nest, so a local failure stays local.

<!-- demo:boom-handler -->

A render-time throw is rewound cleanly (the serializer discards the partial output) and caught exactly
once:

<!-- demo:boom-render -->

Nested boundaries — the innermost one catches, leaving its siblings untouched:

<!-- demo:boom-nested -->

---

See also: [Lifecycle](lifecycle.md) for when `OnPropsChanged` refires, and
[JS interop](js-interop.md) for element refs and scoped JS.
