namespace Rask.Core.Virtualization;

/// <summary>
///     Where a scroll box of rows is, as the browser last said, and the rows it shows from there: the arithmetic under
///     <see cref="Components.VirtualizeModel" /> and under every kit list that windows its rows (<c>Ui.Select</c>,
///     <c>Ui.Pillbox</c>).
/// </summary>
/// <remarks>
///     <para>
///         Two ways to count. The kit's rows are all one height, so <see cref="Window" /> finds a row from its place
///         alone. A <see cref="Components.VirtualizeModel" /> that draws the context's spacers is MEASURED: the browser
///         says where its rows stand (<see cref="VirtualReport" />), and <see cref="Shown" /> reads the rows in view off
///         that, with <see cref="RowHeights" />' running average for the rows no render has drawn.
///     </para>
///     <para>
///         Held by the component whose render draws the rows, so the scroll is heard by that component's own handler
///         and every row it draws keeps that component as its owner.
///     </para>
/// </remarks>
internal sealed class VirtualScroll
{
    // Taller than any scroll box a real screen can show; the overscan still applies on top.
    private const int MaxClientHeight = 16_384;

    private const int MaxScrollTop = int.MaxValue / 2;

    private int _scrollTop;

    private int _clientHeight;

    // How near the end of its content the box may be and still be at it: a zoomed page's scrollTop is fractional.
    private const double EndSlack = 2;

    /// <summary>How far the box is scrolled, in pixels.</summary>
    internal int ScrollTop => _scrollTop;

    /// <summary>What the browser last measured of a measured list's rows; null for a box that holds none.</summary>
    internal VirtualReport? Report { get; private set; }

    /// <summary>Whether the box was scrolled to the end of its content when it last said where it is.</summary>
    internal bool AtEnd { get; private set; } = true;

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

        Report = box.Virtual;
        AtEnd = box.ScrollHeight - box.ClientHeight - box.ScrollTop < EndSlack;
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

    /// <summary>
    ///     The rows a measured list shows in the box now: the first one the box's top cuts and one past the last its
    ///     bottom cuts. Inside the drawn window a row is found where the browser measured the rows to stand; above and
    ///     below it, where the spacers were drawn to put it.
    /// </summary>
    /// <param name="total">How many rows there are.</param>
    /// <param name="average">The height a row is taken to be now.</param>
    /// <param name="initialClientHeight">The box's height to assume before the browser has said it.</param>
    /// <param name="drawn">The window the last render drew.</param>
    /// <param name="drawnAverage">The row height that render drew its spacers at.</param>
    /// <returns>The first row in view and one past the last.</returns>
    internal (int First, int Last) Shown(int total, double average, int initialClientHeight, VirtualWindow drawn, double drawnAverage)
    {
        var height = Height((int)Math.Ceiling(average), initialClientHeight);
        var top = Math.Max(0, _scrollTop - (Report?.ListTop ?? 0));
        var of = Report is { } report && report.Of(drawn) && drawn.Count > 0 && report.Separation > 0 ? Report : null;
        var separation = of?.Separation;

        // A row the browser says an edge cuts is that row; past the drawn rows, a row is where the spacers put it.
        var first = Drawn(of?.TopRow, drawn) is { } cut
            ? cut
            : Row(Math.Floor(IndexAt(top, average, drawn, drawnAverage, separation)), total);
        var last = Drawn(of?.BottomRow, drawn) is { } cutBelow
            ? cutBelow + 1
            : Row(Math.Ceiling(IndexAt(top + height, average, drawn, drawnAverage, separation)), total);
        return (Math.Min(first, total), Math.Clamp(last, Math.Min(first, total), total));
    }

    /// <summary>How many rows of <paramref name="average" /> height the box holds, rounded up.</summary>
    /// <param name="average">The height a row is taken to be.</param>
    /// <param name="initialClientHeight">The box's height to assume before the browser has said it.</param>
    /// <returns>The rows in a box's height.</returns>
    internal int RowsTall(double average, int initialClientHeight) =>
        (int)Math.Ceiling(Height((int)Math.Ceiling(average), initialClientHeight) / average);

    /// <summary>The window around the rows <paramref name="first" /> to <paramref name="last" />: those, and <paramref name="overscan" /> more either side.</summary>
    /// <param name="first">The first row in view.</param>
    /// <param name="last">One past the last row in view.</param>
    /// <param name="overscan">How many rows past either edge are drawn as well.</param>
    /// <param name="total">How many rows there are.</param>
    /// <param name="average">The height a row not drawn is taken to be: the spacers are that many of them tall.</param>
    /// <returns>The window, with the room the rows outside it take.</returns>
    internal static VirtualWindow Around(int first, int last, int overscan, int total, double average)
    {
        total = Math.Max(0, total);
        first = Math.Clamp(first, 0, total);
        last = Math.Clamp(last, first, total);
        var start = (int)Math.Max(0, (long)first - Math.Max(0, overscan));
        var end = (int)Math.Min(total, (long)last + Math.Max(0, overscan));
        return new VirtualWindow(start, end - start, Pixels(start * average), Pixels((total - end) * average));
    }

    // Where the row at `y` pixels down the list is, counted in rows (a fraction: how far into it).
    private static double IndexAt(double y, double average, VirtualWindow drawn, double drawnAverage, double? separation)
    {
        if (separation is not { } rows)
        {
            return y / average;
        }

        var above = drawn.OffsetBefore;
        if (y < above)
        {
            return Math.Min(drawn.Start, y / drawnAverage);
        }

        if (y < above + rows)
        {
            return drawn.Start + ((y - above) / rows * drawn.Count);
        }

        return drawn.End + ((y - above - rows) / drawnAverage);
    }

    private static int Row(double index, int total) => (int)Math.Clamp(index, 0, Math.Max(0, total));

    // A row the browser named, when it is one of the drawn ones.
    private static int? Drawn(int? row, VirtualWindow drawn) =>
        row is { } at && at >= drawn.Start && at < drawn.End ? at : null;

    private static int Pixels(double px) => (int)Math.Clamp(Math.Round(px), 0, MaxScrollTop);
}
