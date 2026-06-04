namespace Rask.Core.DragAndDrop;

// The committed result of a drag: an item left (FromZone, FromIndex) and was dropped onto
// (ToZone, ToIndex). Zones are arbitrary string keys the consumer assigns to each drop region
// (a single-list reorder uses one zone; a Kanban board uses one per column). The consumer owns
// the backing collections and performs the actual move in its OnDrop handler — DragDrop only
// tracks the transient drag and reports where it landed.
public sealed record DragDropMove(string FromZone, int FromIndex, string ToZone, int ToIndex)
{
    // Applies the move to the consumer's backing lists: removes the dragged item from `from` at
    // FromIndex and inserts it into `to` at the dropped slot. Pass the same list as both args for
    // a same-zone reorder, or use the single-list overload. Direction-aware by construction —
    // dragging down lands the item *after* the target, dragging up lands it *before* — so either
    // end of the list is reachable. After RemoveAt(FromIndex) the target's index is To-1 when
    // FromIndex < To (everything past the source shifted left) and unchanged otherwise; in both
    // cases inserting at the clamped ToIndex is correct. The clamp also covers a "drop at end"
    // slot (ToIndex == count) and an empty target list.
    public void ApplyTo<T>(IList<T> from, IList<T> to)
    {
        if (from is null)
        {
            throw new ArgumentNullException(nameof(from));
        }

        if (to is null)
        {
            throw new ArgumentNullException(nameof(to));
        }

        if (FromIndex < 0 || FromIndex >= from.Count)
        {
            return;
        }

        // Same-list drop onto the source's own slot is a no-op; skip the remove/insert (and the
        // re-render it would trigger).
        if (ReferenceEquals(from, to) && FromIndex == ToIndex)
        {
            return;
        }

        var item = from[FromIndex];
        from.RemoveAt(FromIndex);
        to.Insert(Math.Clamp(ToIndex, 0, to.Count), item);
    }

    // Same-zone reorder: moves the dragged item within a single list.
    public void ApplyTo<T>(IList<T> list) => ApplyTo(list, list);
}
