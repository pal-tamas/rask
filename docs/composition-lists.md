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
    var page = await _api.GetRows(req.StartIndex, req.Count, req.CancellationToken);
    return new ItemsProviderResult<Row>(page.Items, page.TotalCount);
}
```

Provider mode caches by index, marks rows `IsPlaceholder` while a page is in flight, and
cancels + disposes superseded requests (and on unmount).

Pass **`InitialTotalCount:`** as well when the first paint matters. Until the provider has answered
there is no total, so there is no window and your body renders *no rows at all* — an empty box that
pops into a full list when the fetch resolves. That is a layout shift on every load, and on a
prerendered page it makes the markup's shape depend on when it was sampled. With an estimate the
window is drawn at full size immediately, every row flagged `IsPlaceholder`, so a body that already
renders a pending row needs no new branch:

```csharp
ItemsProvider: FetchRowsAsync,
ItemSize: 32,
InitialClientHeight: 360,
InitialTotalCount: 500        // a guess; the provider's real total replaces it on the next paint
```

An estimate is all it needs — the real count wins as soon as the provider reports one, and only the
spacer heights change. It is the provider-mode sibling of `InitialClientHeight`, and for the same
reason: the first render happens before the thing that knows the answer has answered. Ignored in
items mode, where the count is never in doubt.

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

Toasts are built in. Say something to the person using the app from anywhere — an event handler, a save, a
render — with nothing injected and nothing mounted:

```csharp
private async Task Save()
{
    await Product.Create(_model);
    Toast.Success("Saved");
    Routes.ProductsPage().Go();   // the toast shows on the page they land on
}
```

`Toast.Info`, `Toast.Warning` and `Toast.Error` say the rest. A toast can carry a heading, a button or a link,
and its own time on screen:

```csharp
Toast.Success("Your order was placed").Heading("Order 42");
Toast.Info("Changes saved.").Action("Undo", UndoChanges);          // runs it, then dismisses the toast
Toast.Success("Invoice created.").Link("View invoice", Routes.InvoicePage(invoice.Id));
Toast.Error("Payment failed").For(30.Seconds);
Toast.Error("Couldn't reach the server").UntilDismissed();
```

<!-- demo:toast-built-in -->

A toast belongs to the session, not the page, so one raised just before navigating survives the navigation
and shows once on the destination. The host draws them — as the [UI kit](ui-kit.md)'s `Ui.Toast`, or in a small
look of Rask's own when the kit is off — in the bottom end corner, one at a time, each gone after five seconds
unless it says otherwise. Where they appear and how long they stay is the app's to set, in `Program.cs` or
`appsettings.json`:

```csharp
RaskApp.Create(args)
    .Configure(c => c.Toasts.At(Ui.ToastPosition.TopEnd).For(8.Seconds))
    .Run<App>();
```

```json
{ "Rask": { "Toasts": { "Position": "TopEnd", "Duration": "00:00:08" } } }
```

For anything else — a stack, the inverted look — place the kit's toast in your layout and the built-in one
steps aside:

```csharp
Ui.ToastGroup[Ui.Toast]          // toasts stack, and the deck opens under the pointer
Ui.Toast.TopEnd.Invert()         // one at a time, dark on a light page
```

### Your own look

Mount a `ToastOutlet` anywhere and the built-in one steps aside — your outlet gets every toast, and draws
them through `Template`, which receives the messages showing now and a `dismiss(id)` callback:

```csharp
protected override Component? Render() =>
[
    Router,
    ToastOutlet.Template((messages, dismiss) =>
        Div.Class("notices")[messages.Select(m => Div.Key(m.Id).Class("notice")[
            m.Message,
            Button.OnClick(() => dismiss(m.Id))["×"]])]),
];
```

Each message is delivered to exactly one outlet and never reappears on a later render. `AutoDismissAfter` sets
how long a toast stays in your outlet when it did not say (`.For`, `.UntilDismissed()`).

### In a test

A toast is on screen, so a test that visits the page sees it: `page.Shows("Saved")`. A test that runs no page
records them instead:

```csharp
using var toasts = Toast.Fake();

await Orders.Place(cart);

toasts.Shown("Order placed").Once();
toasts.Shown("Order placed").As(ToastLevel.Success);
toasts.Shown("Payment failed").Never();
```

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
