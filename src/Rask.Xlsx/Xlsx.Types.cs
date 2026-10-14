using System.Collections;

namespace Rask.Workbooks;

public static partial class Xlsx
{
    /// <summary>A moment shown as <c>yyyy.mm.dd hh:mm</c>; what it holds is the clock on the wall.</summary>
    public readonly record struct Moment(DateTime WallClock);

    /// <summary>A moment shown as its day only, <c>yyyy.mm.dd</c>; the cell still holds the time of day.</summary>
    public readonly record struct Day(DateTime WallClock);

    /// <summary>A cell in bold: what a row that sums others up is written in.</summary>
    public readonly record struct Strong(object? Value);

    /// <summary>
    ///     A cell Excel works out itself, in bold - a sum that follows what is filtered: the formula as Excel's own
    ///     language writes it (<c>SUBTOTAL(9,D3:D12)</c>), and what it comes to now, for whatever opens the file without working it out.
    /// </summary>
    public readonly record struct Formula(string Text, decimal Now, bool Whole = false);

    /// <summary>In the row that sums a sheet up: the sum of its column, as a formula that follows what a filter leaves.</summary>
    public readonly record struct Sum;

    /// <summary>
    ///     A row folded beneath the last row that is none: Excel shows it when that row's plus is pressed
    ///     (or the 2 at the sheet's corner, for all of them), and hides it again at the minus.
    /// </summary>
    public sealed class Folded(IReadOnlyList<object?> cells) : IReadOnlyList<object?>
    {
        /// <inheritdoc />
        public object? this[int index] => cells[index];

        /// <inheritdoc />
        public int Count => cells.Count;

        /// <inheritdoc />
        public IEnumerator<object?> GetEnumerator() => cells.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>One sheet of a workbook: its name, its heading row, its rows - and a last row that sums them all up, which no filter hides.</summary>
    public sealed record Sheet(string Name, IReadOnlyList<string> Headings, IEnumerable<IReadOnlyList<object?>> Rows, IReadOnlyList<object?>? Total = null);
}
