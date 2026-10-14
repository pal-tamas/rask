using System.Collections;

namespace Rask.Workbooks;

public static partial class Xlsx
{
    /// <summary>A moment shown as <c>yyyy.mm.dd hh:mm</c>; what it holds is the clock on the wall.</summary>
    public readonly struct Moment(DateTime wallClock)
    {
        /// <summary>The clock on the wall.</summary>
        public DateTime WallClock { get; } = wallClock;
    }

    /// <summary>A moment shown as its day only, <c>yyyy.mm.dd</c>; the cell still holds the time of day.</summary>
    public readonly struct Day(DateTime wallClock)
    {
        /// <summary>The clock on the wall; the day is what shows.</summary>
        public DateTime WallClock { get; } = wallClock;
    }

    /// <summary>A cell in bold: what a row that sums others up is written in.</summary>
    public readonly struct Strong(object? value)
    {
        /// <summary>What is written in bold.</summary>
        public object? Value { get; } = value;
    }

    /// <summary>
    ///     A cell Excel works out itself, in bold - a sum that follows what is filtered: the formula as Excel's own
    ///     language writes it (<c>SUBTOTAL(9,D3:D12)</c>), and what it comes to now, for whatever opens the file without working it out.
    /// </summary>
    public readonly struct Formula(string text, decimal now, bool whole = false)
    {
        /// <summary>The formula as Excel writes it, without the leading <c>=</c>.</summary>
        public string Text { get; } = text;

        /// <summary>What it comes to now.</summary>
        public decimal Now { get; } = now;

        /// <summary>Whether it is shown as a whole number rather than an amount.</summary>
        public bool Whole { get; } = whole;
    }

    /// <summary>In the row that sums a sheet up: the sum of its column, as a formula that follows what a filter leaves.</summary>
    public readonly struct Sum;

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
    public sealed class Sheet(string name, IReadOnlyList<string> headings, IEnumerable<IReadOnlyList<object?>> rows, IReadOnlyList<object?>? total = null)
    {
        /// <summary>The name on the sheet's tab: at most 31 characters, none of <c>[ ] : * ? / \</c>.</summary>
        public string Name { get; } = name;

        /// <summary>The heading row.</summary>
        public IReadOnlyList<string> Headings { get; } = headings;

        /// <summary>The rows below the headings.</summary>
        public IEnumerable<IReadOnlyList<object?>> Rows { get; } = rows;

        /// <summary>The last row, which sums the others up.</summary>
        public IReadOnlyList<object?>? Total { get; } = total;
    }
}
