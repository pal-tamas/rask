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

        CodeSample(
            EmbeddedSource.Read("DragDropDemo.cs"),
            Css: EmbeddedSource.Read("DragDropDemo.css"),
            Result: DragDropDemo(),
            Notes:
            "Drag handlers are parameterless (like OnClick) — the dragged item's identity rides the handler closure, not the event payload, so no custom wire type is needed. DragDropMove.ApplyTo handles the direction-aware insert math (down-after, up-before) for both same-list and cross-list moves. Items carry a stable Key: so reorders ship trusted keyed structural diff ops that preserve survivors' DOM state.")
    ];
}
