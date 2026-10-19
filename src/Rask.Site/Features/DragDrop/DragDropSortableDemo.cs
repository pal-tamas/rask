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
        Ui.Table.Class("dd-list").Id("dd-fruit-list")[Ui.TableRows[_fruits.Select((fruit, index) => Ui.TableRow
            .Key(fruit)
            .Class(
                "dd-item",
                ctx.IsSource("list", index) ? "dd-dragging" : null,
                ctx.IsDropTarget("list", index) ? "dd-drop-target" : null)
            .Draggable()
            .OnDragStart(ctx.DragStart("list", index))
            .OnDragOver(ctx.DragOver("list", index))
            .OnDrop(ctx.Drop("list", index))
            .OnDragEnd(ctx.DragEnd)
            .Data("testid", $"fruit-{index}")[
                Ui.TableCell.Class("w-8")[Ui.Icon.Name(Ui.IconName.EllipsisVertical).Variant(Ui.IconVariant.Mini)],
                Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[fruit]
            ])]];

    // Direction-aware: dragging down lands after the target, dragging up lands before it.
    private void ReorderFruit(DragDropMove move) => move.ApplyTo(_fruits);
}
