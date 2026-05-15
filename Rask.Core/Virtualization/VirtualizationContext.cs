using System.Diagnostics.CodeAnalysis;
using Rask.Core.Live;

namespace Rask.Core.Virtualization;

// Typed projection of the type-erased VirtualizationState. Built by the Virtualize<T>
// factory's closure before handing off to the user's render fragment. Lets users iterate
// VisibleItems with strong typing and wire OnScroll into a scrollable element.
public sealed class VirtualizationContext<T>
{
    [UnconditionalSuppressMessage("Trimming", "IL2091",
        Justification = "T flows through here only as a static cast (object? -> T?), not reflection.")]
    public VirtualizationContext(VirtualizationState state)
    {
        State = state;
        var items = new VirtualItem<T>[state.VisibleValues.Count];
        for (var i = 0; i < items.Length; i++)
        {
            var value = state.VisibleValues[i];
            var isPlaceholder = state.VisiblePlaceholders[i];
            items[i] = new VirtualItem<T>(
                state.VisibleIndices[i],
                isPlaceholder ? default : (T?)value,
                isPlaceholder);
        }

        VisibleItems = items;
    }

    public VirtualizationState State { get; }
    public IReadOnlyList<VirtualItem<T>> VisibleItems { get; }
    public int StartIndex => State.StartIndex;
    public int TotalCount => State.TotalCount;
    public int ItemSize => State.ItemSize;
    public int OffsetBefore => State.OffsetBefore;
    public int OffsetAfter => State.OffsetAfter;
    public Action<ScrollEvent> OnScroll => State.OnScroll;
}
