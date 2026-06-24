using Rask.Core.Live;

namespace Rask.Core.Virtualization;

// Type-erased state the VirtualizeModel component passes to a non-generic Render delegate.
// The typed VirtualizeModel<T> factory wraps the user's Func<VirtualizationContext<T>, Component>
// in a closure that re-projects this payload into a typed VirtualizationContext<T> before
// invoking the user code. Public so the Render property's signature stays accessible from
// outside the assembly, but downstream callers should never assemble one of these by hand —
// always go through VirtualizeModel<T>(...).
public sealed class VirtualizationState
{
    public VirtualizationState(
        IReadOnlyList<object?> visibleValues,
        IReadOnlyList<int> visibleIndices,
        IReadOnlyList<bool> visiblePlaceholders,
        int startIndex,
        int totalCount,
        int itemSize,
        int offsetBefore,
        int offsetAfter,
        Callback<ScrollEvent> onScroll)
    {
        VisibleValues = visibleValues;
        VisibleIndices = visibleIndices;
        VisiblePlaceholders = visiblePlaceholders;
        StartIndex = startIndex;
        TotalCount = totalCount;
        ItemSize = itemSize;
        OffsetBefore = offsetBefore;
        OffsetAfter = offsetAfter;
        OnScroll = onScroll;
    }

    public IReadOnlyList<object?> VisibleValues { get; }
    public IReadOnlyList<int> VisibleIndices { get; }
    public IReadOnlyList<bool> VisiblePlaceholders { get; }
    public int StartIndex { get; }
    public int TotalCount { get; }
    public int ItemSize { get; }
    public int OffsetBefore { get; }
    public int OffsetAfter { get; }
    public Callback<ScrollEvent> OnScroll { get; }
}
