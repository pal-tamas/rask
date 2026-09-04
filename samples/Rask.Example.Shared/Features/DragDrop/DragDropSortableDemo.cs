using Rask.Core.DragAndDrop;

namespace Rask.Example.Shared.Features;

// Single-list reorder. One drop zone ("list"); drop a fruit onto another to reorder.
public sealed partial class DragDropSortableDemo : Component
{
    private readonly List<string> _fruits =
    [
        "Apple", "Banana", "Cherry", "Date", "Elderberry"
    ];

    protected override Component? Render() => DragDrop.Body(SortableBody).OnDrop(ReorderFruit);

    private Component SortableBody(DragDropContext ctx)
    {
        var rows = new List<Component>(_fruits.Count);
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

            rows.Add(Li
                .Key(fruit)
                .Class(cls)
                .Draggable(true)
                .OnDragStart(ctx.DragStart("list", index))
                .OnDragOver(ctx.DragOver("list", index))
                .OnDropAsync(ctx.Drop("list", index))
                .OnDragEnd(ctx.DragEnd)
                .Data(new Dictionary<string, string?> { ["testid"] = $"fruit-{index}" })[
                UiIcon.Name(UiIconName.Grip).Class("text-ui-muted"),
                Span.Class("font-semibold")[fruit]
            ]);
        }

        return Ul.Class($"{Tw.ListGroup} dd-list").Id("dd-fruit-list")[rows];
    }

    // Direction-aware: dragging down lands after the target, dragging up lands before it.
    private void ReorderFruit(DragDropMove move) => move.ApplyTo(_fruits);
}
