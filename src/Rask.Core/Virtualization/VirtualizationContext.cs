using System.Diagnostics.CodeAnalysis;
using Rask.Core.Live;

namespace Rask.Core.Virtualization;

// Typed projection of the type-erased VirtualizationState. Built by the VirtualizeModel<T>
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
    public Action<Event> OnScroll => State.OnScroll;

    /// <summary>
    ///     The spacer above the rows, a <c>&lt;div&gt;</c> as tall as the rows not drawn above them. Drawing it and
    ///     <see cref="Below" /> around the rows is what MEASURES them: rows of any height scroll true, and
    ///     <c>InitialItemIndex</c>, <c>Anchor</c> and <c>Scroller</c> work. Rows inside a <c>Tbody</c> take
    ///     <see cref="AboveRow" /> instead.
    /// </summary>
    public Component Above => State.Spacer(above: true, row: false);

    /// <summary>The spacer below the rows, a <c>&lt;div&gt;</c>: the partner of <see cref="Above" />.</summary>
    public Component Below => State.Spacer(above: false, row: false);

    /// <summary>The spacer above the rows of a table, a <c>&lt;tr&gt;</c> for inside its <c>Tbody</c>: <see cref="Above" /> in a table.</summary>
    public Component AboveRow => State.Spacer(above: true, row: true);

    /// <summary>The spacer below the rows of a table, a <c>&lt;tr&gt;</c>: the partner of <see cref="AboveRow" />.</summary>
    public Component BelowRow => State.Spacer(above: false, row: true);
}
