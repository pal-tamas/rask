using Rask.Core.DragAndDrop;

namespace Rask.Core.Components;

// Headless drag-and-drop: renders no DOM of its own, takes a Body delegate, and hands it a
// DragDropContext describing the in-flight drag. The consumer draws its own draggable items and
// drop zones, wiring the context's DragStart/DragOver/Drop/DragEnd delegates onto them and
// reading IsDropTarget/SourceZone/... to style feedback. When an item is dropped, DragDrop fires
// OnDrop/OnDropAsync with a DragDropMove({FromZone,FromIndex} -> {ToZone,ToIndex}); the consumer
// owns the backing collections and performs the actual move.
//
// Zones are arbitrary string keys: a single-list reorder uses one zone, a Kanban board uses one
// per column. See /drag-drop in the showcase for both patterns.
//
// Invoked through the generated factory `Components.DragDrop(...)`; the render delegate is the
// `Body` parameter (named so to avoid colliding with Component.Render(), same as VirtualizeModel).
public sealed class DragDrop : Component
{
    // The render fragment. Called with a fresh DragDropContext every render; returns the user's
    // chosen root Component for the drag region. Named "Body" (not "Render") to avoid colliding
    // with Component.Render().
    public Func<DragDropContext, Component>? Body { get; set; }

    // Fired once when an item is dropped onto a zone. Set exactly one of OnDrop / OnDropAsync.
    public Callback<DragDropMove>? OnDrop { get; set; }
    public CallbackAsync<DragDropMove>? OnDropAsync { get; set; }

    // DragDrop reads mutable internal drag state (source / hover target) that the framework can't
    // observe through props, so every render must re-execute — same reasoning as VirtualizeModel.
    protected override bool BypassRenderCache => true;

    internal string? SourceZoneInternal { get; private set; }

    internal int SourceIndexInternal { get; private set; } = -1;

    internal string? TargetZoneInternal { get; private set; }

    internal int TargetIndexInternal { get; private set; } = -1;

    internal void BeginDrag(string zone, int index)
    {
        SourceZoneInternal = zone;
        SourceIndexInternal = index;
        TargetZoneInternal = null;
        TargetIndexInternal = -1;
    }

    internal void HoverTarget(string zone, int index)
    {
        // Ignore hover updates when no drag is active (a stray dragover after a drop).
        if (SourceZoneInternal is null)
        {
            return;
        }

        TargetZoneInternal = zone;
        TargetIndexInternal = index;
    }

    internal void EndDrag()
    {
        SourceZoneInternal = null;
        SourceIndexInternal = -1;
        TargetZoneInternal = null;
        TargetIndexInternal = -1;
    }

    // Routes a drop to whichever handler the consumer set — async if OnDropAsync is present,
    // otherwise the sync OnDrop (which completes synchronously inside the returned Task). The
    // DragDropContext always wires this to Element.OnDropAsync, so both consumer styles work.
    internal Task CommitDropAsync(string zone, int index)
    {
        if (TryTakeMove(zone, index) is not { } move)
        {
            return Task.CompletedTask;
        }

        if (OnDropAsync is { } handler)
        {
            return handler(move);
        }

        OnDrop?.Invoke(move);
        return Task.CompletedTask;
    }

    // Snapshots the source, clears all drag state, and returns the move — or null if no drag was
    // active (a drop fired without a matching dragstart). Clearing before invoking the callback
    // keeps the state consistent if the handler triggers a synchronous re-render.
    private DragDropMove? TryTakeMove(string zone, int index)
    {
        var sourceZone = SourceZoneInternal;
        var sourceIndex = SourceIndexInternal;
        EndDrag();
        return sourceZone is null ? null : new DragDropMove(sourceZone, sourceIndex, zone, index);
    }

    protected override Component? Render()
    {
        if (Body is null)
        {
            throw new InvalidOperationException("DragDrop: Body (render) delegate is required.");
        }

        return Body(new DragDropContext(this));
    }
}
