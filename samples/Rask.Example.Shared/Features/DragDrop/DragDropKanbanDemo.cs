using Rask.Core.DragAndDrop;

namespace Rask.Example.Shared.Features;

// Multi-column board. One drop zone per column; drag cards across columns or reorder within one.
public sealed class DragDropKanbanDemo : Component
{
    private static readonly string[] _columns = ["todo", "doing", "done"];

    private static readonly Dictionary<string, string> _columnLabels = new()
    {
        ["todo"] = "To do",
        ["doing"] = "In progress",
        ["done"] = "Done"
    };

    private readonly Dictionary<string, List<Card>> _board = new()
    {
        ["todo"] =
            [new Card(1, "Sketch the API"), new Card(2, "Write the primitive"), new Card(3, "Add the events")],
        ["doing"] = [new Card(4, "Wire the client JS")],
        ["done"] = [new Card(5, "Read the codebase")]
    };

    protected override RenderResult Render() => DragDrop(KanbanBody, MoveCard);

    private Component KanbanBody(DragDropContext ctx)
    {
        var cols = new List<Child>(_columns.Length);
        foreach (var zone in _columns)
        {
            var cards = _board[zone];
            var cardChildren = new List<Child>(cards.Count);
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

            // The whole column body is the drop-at-end zone, so a card can land in empty space, at
            // the tail of a column, or into an empty column. Cards inside carry their own per-index
            // drop handlers; the client's e.target.closest(...) resolves the innermost match, so
            // hovering a card targets that card and hovering empty space targets the column end.
            var dropAtEnd = cards.Count;
            var bodyCls = "dd-column-body";
            if (ctx.IsDropTarget(zone, dropAtEnd))
            {
                bodyCls += " dd-drop-target";
            }

            cols.Add(Div(Key: zone, Class: "col")[
                Div(Class: "dd-column h-100")[
                    Div(Class: "dd-column-header d-flex justify-content-between align-items-center")[
                        Span(Class: "fw-semibold")[_columnLabels[zone]],
                        Span(Class: "badge bg-secondary rounded-pill")[cards.Count.ToString()]
                    ],
                    Div(
                        Class: bodyCls,
                        OnDragOver: ctx.DragOver(zone, dropAtEnd),
                        OnDrop: ctx.Drop(zone, dropAtEnd),
                        Data: new Dictionary<string, string?> { ["testid"] = $"col-{zone}" })[cardChildren]
                ]
            ]);
        }

        return Div(Class: "row g-3 dd-board")[cols];
    }

    private void MoveCard(DragDropMove move)
    {
        if (_board.TryGetValue(move.FromZone, out var from) && _board.TryGetValue(move.ToZone, out var to))
        {
            move.ApplyTo(from, to);
        }
    }

    private sealed record Card(int Id, string Title);
}
