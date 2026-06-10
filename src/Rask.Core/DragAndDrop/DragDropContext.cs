using Rask.Core.Components;

namespace Rask.Core.DragAndDrop;

// State handed to the DragDrop Body delegate on every render. The consumer reads the
// current-drag state (SourceZone/IsDropTarget/...) to style its own markup, and wires the
// returned delegates onto the elements it draws:
//
//   DragStart(zone, index) -> Element.OnDragStart   (begins a drag from that item)
//   DragOver(zone, index)  -> Element.OnDragOver     (optional: live drop-target highlight)
//   Drop(zone, index)      -> Element.OnDrop         (commits the move, fires OnDrop callback)
//   DragEnd                -> Element.OnDragEnd       (clears the drag if it ended without a drop)
//
// The delegates close over the DragDrop instance (stable across renders — it's cached by
// position), so a handler created in one render still mutates the live primitive when it fires
// a frame later.
public sealed class DragDropContext
{
    private readonly DragDrop _owner;

    internal DragDropContext(DragDrop owner) => _owner = owner;

    // True while a drag is in progress (between DragStart and the next Drop/DragEnd).
    public bool IsDragging => _owner.SourceZoneInternal is not null;

    // The zone/index the active drag started from, or null/-1 when nothing is being dragged.
    public string? SourceZone => _owner.SourceZoneInternal;
    public int SourceIndex => _owner.SourceIndexInternal;

    // The zone/index currently hovered, populated only by DragOver handlers. Drives a
    // server-rendered drop-target highlight; null/-1 when no DragOver is wired or hovered.
    public string? TargetZone => _owner.TargetZoneInternal;
    public int TargetIndex => _owner.TargetIndexInternal;

    public Action DragEnd => _owner.EndDrag;

    // Convenience for the common "highlight the slot under the cursor" case.
    public bool IsDropTarget(string zone, int index) =>
        string.Equals(_owner.TargetZoneInternal, zone, StringComparison.Ordinal)
        && _owner.TargetIndexInternal == index;

    // Convenience for "is this item the one being dragged" styling.
    public bool IsSource(string zone, int index) =>
        string.Equals(_owner.SourceZoneInternal, zone, StringComparison.Ordinal)
        && _owner.SourceIndexInternal == index;

    public Action DragStart(string zone, int index) => () => _owner.BeginDrag(zone, index);

    public Action DragOver(string zone, int index) => () => _owner.HoverTarget(zone, index);

    // Returns an Action when only the sync OnDrop is set, or a Func<Task> when OnDropAsync is
    // set — the Element.OnDrop slot is typed as Delegate? and the dispatcher routes either.
    public Delegate Drop(string zone, int index) =>
        _owner.HasAsyncDrop
            ? (Func<Task>)(() => _owner.CommitDropAsync(zone, index))
            : (Action)(() => _owner.CommitDrop(zone, index));
}
