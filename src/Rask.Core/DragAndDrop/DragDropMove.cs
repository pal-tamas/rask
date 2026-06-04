namespace Rask.Core.DragAndDrop;

// The committed result of a drag: an item left (FromZone, FromIndex) and was dropped onto
// (ToZone, ToIndex). Zones are arbitrary string keys the consumer assigns to each drop region
// (a single-list reorder uses one zone; a Kanban board uses one per column). The consumer owns
// the backing collections and performs the actual move in its OnDrop handler — DragDrop only
// tracks the transient drag and reports where it landed.
public sealed record DragDropMove(string FromZone, int FromIndex, string ToZone, int ToIndex);
