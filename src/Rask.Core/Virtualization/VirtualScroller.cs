using Rask.Core.Components;

namespace Rask.Core.Virtualization;

/// <summary>
///     Scrolls a virtual list to a row from code: make one in a field, hand it to <c>Virtualize.Items(…, Scroller: …)</c>,
///     and <c>await _scroller.ScrollTo(index)</c> from a handler.
/// </summary>
/// <remarks>
///     <code>
///     private readonly VirtualScroller _scroller = VirtualScroller.New();
///     …
///     Virtualize.Items(ctx => …, rows, Scroller: _scroller)
///     …
///     Button.OnClick(() => _scroller.ScrollTo(500))["Row 500"]
///     </code>
///     Needs the context's spacers (<c>ctx.Above</c>, <c>ctx.Below</c>): the browser finds the row between them.
/// </remarks>
public sealed class VirtualScroller
{
    private VirtualizeModel? _list;

    private VirtualScroller()
    {
    }

    /// <summary>Makes a scroller. Keep it in a field, so every render hands the list the same one.</summary>
    /// <returns>A scroller no list holds yet.</returns>
    public static VirtualScroller New() => new();

    /// <summary>
    ///     Scrolls the list until row <paramref name="index" /> stands at the top of its box (clamped to the rows there
    ///     are), and finishes once it is in view. The latest call wins: an earlier one still on its way finishes when this
    ///     one takes over.
    /// </summary>
    /// <param name="index">The row, counted from 0.</param>
    /// <returns>A task that completes once the row is in view.</returns>
    /// <exception cref="InvalidOperationException">No list has been drawn with this scroller yet.</exception>
    public Task ScrollTo(int index) =>
        _list?.ScrollTo(index)
        ?? throw new InvalidOperationException(
            "This VirtualScroller is not on a list yet. Hand it to Virtualize.Items(…, Scroller: _scroller) and call ScrollTo "
            + "once the list has rendered — from a handler, not while the page is first drawn. To open the list at a row, "
            + "pass InitialItemIndex instead.");

    internal void Hold(VirtualizeModel list) => _list = list;
}
