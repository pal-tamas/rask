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
// `Body` parameter (named so to avoid colliding with Component.Render(), same as Virtualize).
public sealed class DragDrop : Component
{
    private string? _sourceZone;
    private int _sourceIndex = -1;
    private string? _targetZone;
    private int _targetIndex = -1;

    // The render fragment. Called with a fresh DragDropContext every render; returns the user's
    // chosen root Component for the drag region. Named "Body" (not "Render") to avoid colliding
    // with Component.Render().
    public Func<DragDropContext, Component>? Body { get; set; }

    // Fired once when an item is dropped onto a zone. Set exactly one of OnDrop / OnDropAsync.
    public Action<DragDropMove>? OnDrop { get; set; }
    public Func<DragDropMove, Task>? OnDropAsync { get; set; }

    // DragDrop reads mutable internal drag state (source / hover target) that the framework can't
    // observe through props, so every render must re-execute — same reasoning as Virtualize.
    protected override bool BypassRenderCache => true;

    internal string? SourceZoneInternal => _sourceZone;
    internal int SourceIndexInternal => _sourceIndex;
    internal string? TargetZoneInternal => _targetZone;
    internal int TargetIndexInternal => _targetIndex;
    internal bool HasAsyncDrop => OnDropAsync is not null;

    internal void BeginDrag(string zone, int index)
    {
        _sourceZone = zone;
        _sourceIndex = index;
        _targetZone = null;
        _targetIndex = -1;
    }

    internal void HoverTarget(string zone, int index)
    {
        // Ignore hover updates when no drag is active (a stray dragover after a drop).
        if (_sourceZone is null)
        {
            return;
        }

        _targetZone = zone;
        _targetIndex = index;
    }

    internal void EndDrag()
    {
        _sourceZone = null;
        _sourceIndex = -1;
        _targetZone = null;
        _targetIndex = -1;
    }

    internal void CommitDrop(string zone, int index)
    {
        if (TryTakeMove(zone, index) is { } move)
        {
            OnDrop?.Invoke(move);
        }
    }

    internal async Task CommitDropAsync(string zone, int index)
    {
        if (TryTakeMove(zone, index) is { } move && OnDropAsync is { } handler)
        {
            await handler(move).ConfigureAwait(false);
        }
    }

    // Snapshots the source, clears all drag state, and returns the move — or null if no drag was
    // active (a drop fired without a matching dragstart). Clearing before invoking the callback
    // keeps the state consistent if the handler triggers a synchronous re-render.
    private DragDropMove? TryTakeMove(string zone, int index)
    {
        var sourceZone = _sourceZone;
        var sourceIndex = _sourceIndex;
        EndDrag();
        return sourceZone is null ? null : new DragDropMove(sourceZone, sourceIndex, zone, index);
    }

    protected override RenderResult Render()
    {
        if (Body is null)
        {
            throw new InvalidOperationException("DragDrop: Body (render) delegate is required.");
        }

        return Body(new DragDropContext(this));
    }
}
