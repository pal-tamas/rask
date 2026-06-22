# Composition: context, callbacks, children & lists

How Rask components talk to each other — passing data down without prop drilling,
sending events up, nesting children, and rendering windowed or reorderable lists.

- [Children & fragments](#children--fragments)
- [Callbacks: child → parent](#callbacks-child--parent)
- [Context: provide / consume](#context-provide--consume)
- [Virtualize: windowed lists](#virtualize--windowed-lists)
- [TableModel: headless tables](#tablemodel--headless-tables)
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

**Rask has no `Callback` / `EventCallback` type.** A child raises an event up to its
parent with a plain delegate property — `Action`, `Action<T>`, `Func<Task>`, or
`Func<T, Task>`. The generated factory wraps the delegate so that **invoking it
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

**DOM events on elements.** `Element` exposes handler props the client runtime binds to real
DOM events. Every one ships a **typed sync + async pair** — a synchronous `OnXxx` and an
asynchronous `OnXxxAsync` (`Func<…, Task>`) — the same convention as `OnClick` / `OnClickAsync`;
set at most one per event. The set: `OnClick` (on `Button`/`Div`/…), `OnScroll` /
`OnScrollAsync` (`Action<ScrollEvent>` / `Func<ScrollEvent, Task>`), the drag hooks
(`OnDragStart`/`OnDragOver`/`OnDrop`/`OnDragEnd`, each `Action` + `…Async` `Func<Task>`), and the
keyboard pairs **`OnKeyDown` / `OnKeyDownAsync`** and **`OnKeyUp` / `OnKeyUpAsync`**.
A key handler takes `Action<KeyboardEventArgs>` (or `Func<KeyboardEventArgs, Task>` for the async
sibling); `KeyboardEventArgs` carries `Key` (`"Escape"`), `Code` (`"KeyA"`), the
`Shift`/`Ctrl`/`Alt`/`Meta` modifiers, and `Repeat`. Like
click, a key event is **focus-scoped** — it fires only while the element (or a descendant) holds
focus — and the runtime never `preventDefault`s it, so handlers compose with normal typing. The
Todos sample uses it to close its dialog on Escape (it focuses the `<dialog>` on open via an
`ElementRef`, since a diff-inserted element never fires the HTML `autofocus` attribute).

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

## TableModel — headless tables

`TableModel<T>` is a **fully controlled, headless** table primitive (à la TanStack Table). It owns
**no state** and **transforms no data** — *you* sort, filter, and page your own data and pass it the
final `Rows` plus the current view state. The model projects sort-aware headers and selection-aware
rows into a `TableModelContext<T>` and raises **intent** events; you apply them to your own state and
re-render, and the new props flow back down.

```csharp
TableModel<Person>(
    ctx => Table()[
        Thead()[Tr()[
            // each HeaderCell carries the current sort Direction + a ToggleSort intent
            ctx.Headers.Select(h => Th(Key: h.ColumnId)[
                Button(OnClick: h.ToggleSort)[h.Header, h.Direction switch {
                    SortDirection.Ascending => " ▲", SortDirection.Descending => " ▼", _ => "" }]])
        ]],
        Tbody()[
            ctx.Rows.Select(row => Tr(Key: row.Key)[                 // already sorted + paged by you
                Td()[Input("checkbox", Checked: row.IsSelected, OnChange: _ => row.ToggleSelected())],
                Td()[row.Value.Name]])
        ]
    ],
    Columns: columns,            // IReadOnlyList<ColumnDef<Person>> (Id / Header / Sortable)
    Rows: pageRows,              // the final page YOU produced
    KeySelector: p => p.Id,      // selection / row identity (defaults to the row reference)
    Sort: sort,                  // current sort state (controlled in)
    PageIndex: page, PageCount: pages, SelectedKeys: selected,
    OnSort:   s => { sort = s; },        // YOU apply + re-render
    OnPage:   p => { page = p; },
    OnSelect: k => { selected = k; })
```

`OnSort` / `OnPage` / `OnSelect` are auto-wrapped callbacks, so invoking an intent re-renders the
owning host. `MultiSort: true` makes `ToggleSort` additive (asc → desc → removed per column, others
preserved). Runnable demo:
[`samples/Rask.Example.Shared/Features/Table/TablePage.cs`](../samples/Rask.Example.Shared/Features/Table/TablePage.cs)
drives it entirely from the URL query string. For a master-detail variant —
collapsible rows whose expanded panel hosts a second, independently sortable `TableModel<T>`, with
expand/sort state held in plain component fields — see
[`samples/Rask.Example.Shared/Features/Orders/OrdersPage.cs`](../samples/Rask.Example.Shared/Features/Orders/OrdersPage.cs).
Each open row inserts a keyed detail `<tr>`, so the live diff reconciles expand/collapse as an
in-place insert/remove and sibling open rows keep their own inner sort.

---

## Drag and drop

A headless drag-and-drop primitive lives in `Rask.Core/DragAndDrop`. It tracks the
dragged item and the drop target and raises a callback when an item is dropped; you own
the visuals. See the runnable demo:
[`samples/Rask.Example.Shared/Features/DragDrop/DragDropPage.cs`](../samples/Rask.Example.Shared/Features/DragDrop/DragDropPage.cs)
(+ `DragDropPage.css`).

---

See also: [Lifecycle](lifecycle.md) for when `OnPropsChanged` refires, and
[JS interop](js-interop.md) for element refs and scoped JS.
