# Composition — lists, toasts, drag & error boundaries

Windowed and reorderable lists, transient toast messages, drag-and-drop, and error boundaries.

‹ Back to [Composition](composition.md)

## Virtualize — windowed lists

`Virtualize.Items<T>` is **headless**: *you* render the scroll container and rows from a
`VirtualizationState ctx`, and it tells you which slice is visible. The first argument is
the body builder; pass **exactly one** of `Items` (positional or named) or `ItemsProvider`,
plus `ItemSize` (row height in px, required) and optional `OverscanCount`.

It is the one place on the surface that is a **method call rather than a chain**, and for a reason a
chain cannot work around: a chain infers its type argument from the step that opens it, and `T` here
comes from the *render delegate*, not from a leading step. `Virtualize` is a global alias for the class
that holds it, so no `using` is needed.

```csharp
Virtualize.Items<Row>(
    ctx => Div.Style("height:400px; overflow:auto;").OnScroll(ctx.OnScroll)[
        Div.Style($"height:{ctx.OffsetBefore}px"),          // top spacer
        Table[Tbody[
            ctx.VisibleItems.Select(item => Tr.Style($"height:{ctx.ItemSize}px;").Data(new() { ["rask-key"] = item.Index.ToString() })[  // key → reuse <tr> on scroll
                Td[item.IsPlaceholder ? "—" : item.Value!.Name])
        ]],
        Div.Style($"height:{ctx.OffsetAfter}px")             // bottom spacer
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

A `.Key(…)` on a list item gives it a stable identity across renders, so a reorder **moves** the live
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
public sealed partial class SavePage(IToaster toast, Navigator nav) : Component
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
ToastOutlet.Template((messages, dismiss) =>
    Div[messages.Select(m => (Component)Div.Class("alert").Key(m.Id.ToString())[
        m.Message,
        Button.OnClick(() => dismiss(m.Id))["×"]])])
```

`ToastOutlet` calls `Consume()` (which drains the queue) on mount and whenever `IToaster.Changed` fires,
so each message is delivered to exactly one outlet and never reappears on a later render. Set
`AutoDismissAfter` to have each message clear itself after a delay — a one-shot timer per message that
runs the same dismiss path, so any `Template` auto-dismisses even when its element has no timer of its own.
A toast outlet is a fixed container of messages that auto-hide after
5 s by default (set `AutoHideMs: null` to keep them sticky); mount a single `BsToaster()` in your layout
instead of writing a `Template`. Queue one, show once (this demo auto-dismisses after 5 s):

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
