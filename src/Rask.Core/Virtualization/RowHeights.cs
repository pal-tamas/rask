namespace Rask.Core.Virtualization;

/// <summary>
///     The height a virtual list's rows are taken to be: the page's estimate until the browser has measured some, then
///     one running average of every window measured — .NET's <c>Virtualize</c>, measured the same way (the room between
///     the spacers over the rows drawn there).
/// </summary>
internal sealed class RowHeights
{
    // No row is taken to be shorter: a window is the box's height over this, so a tiny average would draw the list.
    private const double MinRow = 1;

    private double _total;

    private int _count;

    private int _estimate;

    /// <summary>How many rows have been measured since the last reset.</summary>
    internal int Measured => _count;

    /// <summary>The height a row is taken to be, in pixels.</summary>
    internal double Average => _count > 0 ? Math.Max(MinRow, _total / _count) : Math.Max(MinRow, _estimate);

    /// <summary>Starts from the page's estimate, forgetting what was measured whenever the estimate changes.</summary>
    /// <param name="estimate">The <c>ItemSize</c> the page gave.</param>
    internal void From(int estimate)
    {
        if (estimate != _estimate)
        {
            _estimate = estimate;
            Reset();
        }
    }

    /// <summary>Adds a window the browser measured: <paramref name="rows" /> rows, <paramref name="separation" /> pixels together.</summary>
    /// <param name="separation">The room between the spacers.</param>
    /// <param name="rows">How many rows stood there.</param>
    /// <returns>Whether the measurement counted: it does not when it holds no rows or no room.</returns>
    internal bool Add(double separation, int rows)
    {
        if (rows <= 0 || !(separation > 0) || !double.IsFinite(separation))
        {
            return false;
        }

        _total += separation;
        _count += rows;
        return true;
    }

    /// <summary>Forgets every measurement: the rows are new.</summary>
    internal void Reset()
    {
        _total = 0;
        _count = 0;
    }
}
