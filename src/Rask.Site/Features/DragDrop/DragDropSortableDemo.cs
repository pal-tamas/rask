using Rask.Core.DragAndDrop;

namespace Rask.Site.Features;

// Single-list reorder. One drop zone ("list"); drop a fruit onto another to reorder.
public sealed partial class DragDropSortableDemo : Component
{
    private readonly List<string> _fruits =
    [
        "Apple", "Banana", "Cherry", "Date", "Elderberry"
    ];

    protected override Component? Render() => DragDrop.Body(SortableBody).OnDrop(ReorderFruit);

    private Component SortableBody(DragDropContext ctx) =>
        Ui.List.Class("dd-list").Id("dd-fruit-list")[_fruits.Select((fruit, index) => Li
            .Key(fruit)
            .Class(
                "flex items-center gap-2 dd-item",
                ctx.IsSource("list", index) ? "dd-dragging" : null,
                ctx.IsDropTarget("list", index) ? "dd-drop-target" : null)
            .Draggable()
            .OnDragStart(ctx.DragStart("list", index))
            .OnDragOver(ctx.DragOver("list", index))
            .OnDrop(ctx.Drop("list", index))
            .OnDragEnd(ctx.DragEnd)
            .Data("testid", $"fruit-{index}")[
                Ui.Icon.Name(Ui.IconName.Grip).Class("text-ui-muted"),
                Span.Class("font-semibold")[fruit]
            ])];

    // Direction-aware: dragging down lands after the target, dragging up lands before it.
    private void ReorderFruit(DragDropMove move) => move.ApplyTo(_fruits);
}
