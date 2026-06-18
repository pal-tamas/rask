using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("drag-drop")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class DragDropPage : Component
{
    protected override RenderResult Head => Title()["Drag & drop — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Headless drag & drop",
            "DragDrop is a headless primitive: it owns no DOM and tracks only the in-flight drag, handing your Body delegate a DragDropContext. You draw the items and drop zones, wire the context's DragStart/DragOver/Drop/DragEnd onto them, and move your own data when OnDrop reports where the drag landed. Zones are arbitrary string keys — one zone is a sortable list, several are a Kanban board."),

        H2(Class: "h4 mt-4 mb-3")["Sortable list"],
        CodeSample(
            ["DragDropSortableDemo.cs", "DragDropSortableDemo.css"],
            Result: DragDropSortableDemo(),
            Notes:
            "Drag a fruit and drop it onto another to reorder — one drop zone. Drag handlers are parameterless (like OnClick): the dragged item's identity rides the handler closure, not the event payload, so no custom wire type is needed. DragDropMove.ApplyTo handles the direction-aware insert math (down-after, up-before). Items carry a stable Key: so reorders ship trusted keyed structural diff ops that preserve survivors' DOM state."),

        H2(Class: "h4 mt-5 mb-3")["Kanban board"],
        CodeSample(
            ["DragDropKanbanDemo.cs", "DragDropKanbanDemo.css"],
            Result: DragDropKanbanDemo(),
            Notes:
            "The same primitive with one zone per column — drag cards between columns or reorder within one. A single OnDrop moves the card across lists via ApplyTo(from, to). Each column body is its own drop-at-end zone, so a card can land in empty space, at a column's tail, or into an empty column.")
    ];
}
