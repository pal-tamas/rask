using Rask.Core.DragAndDrop;
using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("drag-drop")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class DragDropPage : Component
{
    private sealed record Card(int Id, string Title);

    // Demo A — single-list reorder. One drop zone ("list").
    private readonly List<string> _fruits =
    [
        "Apple", "Banana", "Cherry", "Date", "Elderberry"
    ];

    // Demo B — Kanban board. One drop zone per column.
    private static readonly string[] _columns = ["todo", "doing", "done"];

    private static readonly Dictionary<string, string> _columnLabels = new()
    {
        ["todo"] = "To do",
        ["doing"] = "In progress",
        ["done"] = "Done"
    };

    private readonly Dictionary<string, List<Card>> _board = new()
    {
        ["todo"] = [new(1, "Sketch the API"), new(2, "Write the primitive"), new(3, "Add the events")],
        ["doing"] = [new(4, "Wire the client JS")],
        ["done"] = [new(5, "Read the codebase")]
    };

    protected override RenderResult Head => Title()["Drag & drop — Rask"];

    protected override RenderResult Render() =>
        [
            PageHeader.Render(
                "Headless drag & drop",
                "DragDrop is a headless primitive: it owns no DOM and tracks only the in-flight drag, handing your Body delegate a DragDropContext. You draw the items and drop zones, wire the context's DragStart/DragOver/Drop/DragEnd onto them, and move your own data when OnDrop reports where the drag landed. Zones are arbitrary string keys — one zone is a sortable list, several are a Kanban board."),

            SortableDemo(),
            KanbanDemo(),

            CodeSample(
                """
                // Headless: DragDrop renders no DOM. You draw the items, wire the
                // context's delegates, and move your own data when OnDrop fires.
                DragDrop(
                    Body: ctx => Ul()[
                        _fruits.Select((fruit, i) => Li(
                            Key: fruit,
                            Draggable: true,
                            Class: ctx.IsDropTarget("list", i) ? "drop-target" : null,
                            OnDragStart: ctx.DragStart("list", i),
                            OnDragOver: ctx.DragOver("list", i),  // optional live highlight
                            OnDrop: ctx.Drop("list", i),
                            OnDragEnd: ctx.DragEnd)[fruit])
                    ],
                    OnDrop: m => Reorder(m.FromIndex, m.ToIndex));

                // A Kanban board is the same primitive with one zone per column —
                // OnDrop reports (FromZone, FromIndex) -> (ToZone, ToIndex) so a single
                // handler moves the card across lists.
                """,
                Notes:
                "Drag handlers are parameterless (like OnClick) — the dragged item's identity rides the handler closure, not the event payload, so no custom wire type is needed. Items carry a stable Key: so reorders ship trusted keyed structural diff ops that preserve survivors' DOM state.")
        ];

    // ----- Demo A: sortable list ---------------------------------------------

    private Component SortableDemo() =>
        Div(Class: "card shadow-sm border-0 mb-4")[
            Div(Class: "card-body")[
                H5(Class: "fw-semibold mb-1")["Sortable list"],
                P(Class: "text-secondary small")["Drag a fruit and drop it onto another to reorder. One drop zone."],
                DragDrop(
                    Body: SortableBody,
                    OnDrop: ReorderFruit)
            ]
        ];

    private Component SortableBody(DragDropContext ctx)
    {
        var rows = new List<Child>(_fruits.Count);
        for (var i = 0; i < _fruits.Count; i++)
        {
            var fruit = _fruits[i];
            var index = i;
            var cls = "list-group-item d-flex align-items-center gap-2 dd-item";
            if (ctx.IsSource("list", index))
            {
                cls += " dd-dragging";
            }

            if (ctx.IsDropTarget("list", index))
            {
                cls += " dd-drop-target";
            }

            rows.Add(Li(
                Key: fruit,
                Class: cls,
                Draggable: true,
                OnDragStart: ctx.DragStart("list", index),
                OnDragOver: ctx.DragOver("list", index),
                OnDrop: ctx.Drop("list", index),
                OnDragEnd: ctx.DragEnd,
                Data: new Dictionary<string, string?> { ["testid"] = $"fruit-{index}" })[
                I(Class: "bi bi-grip-vertical text-secondary"),
                Span(Class: "fw-semibold")[fruit]
            ]);
        }

        return Ul(Class: "list-group dd-list", Id: "dd-fruit-list")[rows];
    }

    private void ReorderFruit(DragDropMove move)
    {
        if (move.FromIndex < 0 || move.FromIndex >= _fruits.Count || move.FromIndex == move.ToIndex)
        {
            return;
        }

        var item = _fruits[move.FromIndex];
        _fruits.RemoveAt(move.FromIndex);
        var insertAt = Math.Clamp(move.ToIndex, 0, _fruits.Count);
        // The removal shifted everything after FromIndex down by one; correct the target.
        if (move.FromIndex < insertAt)
        {
            insertAt--;
        }

        _fruits.Insert(insertAt, item);
    }

    // ----- Demo B: Kanban board ----------------------------------------------

    private Component KanbanDemo() =>
        Div(Class: "card shadow-sm border-0 mb-4")[
            Div(Class: "card-body")[
                H5(Class: "fw-semibold mb-1")["Kanban board"],
                P(Class: "text-secondary small")["Drag cards between columns, or reorder within one. Each column is a drop zone."],
                DragDrop(
                    Body: KanbanBody,
                    OnDrop: MoveCard)
            ]
        ];

    private Component KanbanBody(DragDropContext ctx)
    {
        var cols = new List<Child>(_columns.Length);
        foreach (var zone in _columns)
        {
            var cards = _board[zone];
            var cardChildren = new List<Child>(cards.Count + 1);
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                var index = i;
                var cls = "card dd-card";
                if (ctx.IsSource(zone, index))
                {
                    cls += " dd-dragging";
                }

                if (ctx.IsDropTarget(zone, index))
                {
                    cls += " dd-drop-target";
                }

                cardChildren.Add(Div(
                    Key: card.Id,
                    Class: cls,
                    Draggable: true,
                    OnDragStart: ctx.DragStart(zone, index),
                    OnDragOver: ctx.DragOver(zone, index),
                    OnDrop: ctx.Drop(zone, index),
                    OnDragEnd: ctx.DragEnd,
                    Data: new Dictionary<string, string?> { ["testid"] = $"card-{card.Id}" })[
                    Div(Class: "card-body p-2 d-flex align-items-center gap-2")[
                        I(Class: "bi bi-grip-vertical text-secondary"),
                        Span()[card.Title]
                    ]
                ]);
            }

            // A tail drop region so a card can land at the end of (or into an empty) column.
            var dropAtEnd = cards.Count;
            var zoneIsTarget = string.Equals(ctx.TargetZone, zone, StringComparison.Ordinal)
                               && ctx.TargetIndex == dropAtEnd;
            cardChildren.Add(Div(
                Key: $"{zone}-end",
                Class: zoneIsTarget ? "dd-column-tail dd-drop-target" : "dd-column-tail",
                OnDragOver: ctx.DragOver(zone, dropAtEnd),
                OnDrop: ctx.Drop(zone, dropAtEnd),
                Data: new Dictionary<string, string?> { ["testid"] = $"col-{zone}-end" }));

            cols.Add(Div(Key: zone, Class: "col")[
                Div(Class: "dd-column h-100")[
                    Div(Class: "dd-column-header d-flex justify-content-between align-items-center")[
                        Span(Class: "fw-semibold")[_columnLabels[zone]],
                        Span(Class: "badge bg-secondary rounded-pill")[cards.Count.ToString()]
                    ],
                    Div(
                        Class: "dd-column-body",
                        Data: new Dictionary<string, string?> { ["testid"] = $"col-{zone}" })[cardChildren]
                ]
            ]);
        }

        return Div(Class: "row g-3 dd-board")[cols];
    }

    private void MoveCard(DragDropMove move)
    {
        if (!_board.TryGetValue(move.FromZone, out var from) || !_board.TryGetValue(move.ToZone, out var to))
        {
            return;
        }

        if (move.FromIndex < 0 || move.FromIndex >= from.Count)
        {
            return;
        }

        var card = from[move.FromIndex];
        from.RemoveAt(move.FromIndex);

        var insertAt = Math.Clamp(move.ToIndex, 0, to.Count);
        // Same-column move: the removal shifted later cards down, so correct the target.
        if (ReferenceEquals(from, to) && move.FromIndex < insertAt)
        {
            insertAt--;
        }

        to.Insert(insertAt, card);
    }
}
