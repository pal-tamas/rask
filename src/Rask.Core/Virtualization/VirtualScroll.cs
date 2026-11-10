namespace Rask.Core.Virtualization;

/// <summary>
///     Where a scroll box of fixed-height rows is, as the browser last said, and the rows it shows from there: the
///     arithmetic under <see cref="Components.VirtualizeModel" /> and under every kit list that windows its rows
///     (<c>Ui.Select</c>, <c>Ui.Pillbox</c>).
/// </summary>
/// <remarks>
///     Held by the component whose render draws the rows, so the scroll is heard by that component's own handler and
///     every row it draws keeps that component as its owner.
/// </remarks>
internal sealed class VirtualScroll
{
    // Taller than any scroll box a real screen can show; the overscan still applies on top.
    private const int MaxClientHeight = 16_384;

    private const int MaxScrollTop = int.MaxValue / 2;

    private int _scrollTop;

    private int _clientHeight;

    /// <summary>How far the box is scrolled, in pixels.</summary>
    internal int ScrollTop => _scrollTop;

    /// <summary>
    ///     Reads where the box is from a scroll it raised: the box travels with it as <c>e.Target</c>, as MDN's
    ///     JavaScript reads it (<c>e.target.scrollTop</c>).
    /// </summary>
    /// <param name="e">The scroll event.</param>
    internal void Read(Event e)
    {
        if (e.Target is not { } box)
        {
            return;
        }

        // Both numbers are the client's word. An unbounded height would make one frame render — or fetch — the whole
        // list this exists to window, and an unbounded offset would overflow the sum.
        _scrollTop = (int)Math.Clamp(box.ScrollTop, 0, MaxScrollTop);
        if (box.ClientHeight > 0)
        {
            _clientHeight = Math.Min(box.ClientHeight, MaxClientHeight);
        }
    }

    /// <summary>Back to the top: the rows are new, and the box the browser scrolls goes there too.</summary>
    internal void Reset() => _scrollTop = 0;

    /// <summary>Scrolls so row <paramref name="index" /> is in the middle of the box, as far as the rows allow.</summary>
    /// <param name="index">The row.</param>
    /// <param name="itemSize">Each row's height in pixels.</param>
    /// <param name="initialClientHeight">The box's height to assume before the browser has said it.</param>
    internal void Reveal(int index, int itemSize, int initialClientHeight)
    {
        var height = Height(itemSize, initialClientHeight);
        _scrollTop = Math.Clamp((index * itemSize) - ((height - itemSize) / 2), 0, MaxScrollTop);
    }

    /// <summary>The rows the box shows now, with <paramref name="overscan" /> more on either side.</summary>
    /// <param name="itemSize">Each row's height in pixels.</param>
    /// <param name="overscan">How many rows past either edge are drawn as well.</param>
    /// <param name="total">How many rows there are.</param>
    /// <param name="initialClientHeight">The box's height to assume before the browser has said it.</param>
    /// <returns>The first row drawn, how many, and the room left above and below them.</returns>
    internal VirtualWindow Window(int itemSize, int overscan, int total, int initialClientHeight)
    {
        var height = Height(itemSize, initialClientHeight);
        overscan = Math.Max(0, overscan);
        var start = Math.Max(0, (_scrollTop / itemSize) - overscan);
        var end = Math.Min(total, ((_scrollTop + height + itemSize - 1) / itemSize) + overscan);
        if (end < start)
        {
            end = start;
        }

        return new VirtualWindow(start, end - start, start * itemSize, Math.Max(0, total - end) * itemSize);
    }

    /// <summary>The box's height: as the browser said it, or <paramref name="initialClientHeight" /> until it has.</summary>
    /// <param name="itemSize">Each row's height in pixels: the box is never shorter than one.</param>
    /// <param name="initialClientHeight">The box's height to assume before the browser has said it.</param>
    /// <returns>The height in pixels.</returns>
    internal int Height(int itemSize, int initialClientHeight) =>
        _clientHeight > 0 ? _clientHeight : Math.Max(initialClientHeight, itemSize);
}
