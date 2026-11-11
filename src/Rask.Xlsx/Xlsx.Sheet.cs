namespace Rask.Workbooks;

public static partial class Xlsx
{
    /// <summary>
    ///     One sheet of a workbook: its name, its heading row, its rows - and a last row that sums them all up, which no
    ///     filter hides. It is what <see cref="Write(Sheet[])" /> writes and what <see cref="Read(byte[])" /> reads.
    /// </summary>
    public sealed class Sheet(string name, IReadOnlyList<string> headings, IEnumerable<IReadOnlyList<object?>> rows, IReadOnlyList<object?>? total = null)
    {
        private readonly Grid? grid;
        private HeadingIndex? index;

        internal Sheet(string name, Grid grid, int[] headingRows)
            : this(name, grid.Headings(headingRows), grid.Rows(headingRows[^1] + 1)) => this.grid = grid;

        /// <summary>The name on the sheet's tab: at most 31 characters, none of <c>[ ] : * ? / \</c>.</summary>
        public string Name { get; } = name;

        /// <summary>The heading row.</summary>
        public IReadOnlyList<string> Headings { get; } = headings;

        /// <summary>The rows below the headings, each a list of its cells that also finds a cell by its heading: <c>row["Litres"]</c>.</summary>
        public IEnumerable<Row> Rows => Written.Select(cells => new Row(cells, Index));

        /// <summary>The last row, which sums the others up.</summary>
        public IReadOnlyList<object?>? Total { get; } = total;

        /// <summary>The rows as they were given, a <see cref="Folded" /> one still folded: what the writer writes.</summary>
        internal IEnumerable<IReadOnlyList<object?>> Written { get; } = rows;

        private HeadingIndex Index => index ??= new HeadingIndex(Headings);

        /// <summary>
        ///     The sheet read again with its headings on those rows (1-based, as Excel numbers them) and its rows from the
        ///     one after the last of them: <c>Xlsx.Read(file).HeadingsOn(3, 4)</c>. A merged heading cell stands over every
        ///     column it spans, and a column's heading is its parts from top to bottom joined by a space - each trimmed, an
        ///     empty one left out, a cell merged down over several of the rows taken once - so a group "Fuel" over "Litres"
        ///     is <c>"Fuel Litres"</c>.
        /// </summary>
        public Sheet HeadingsOn(params int[] rows)
        {
            ArgumentNullException.ThrowIfNull(rows);
            if (grid is null)
            {
                throw new InvalidOperationException(
                    $"Sheet '{Name}' was made in code, not read: give it its headings in new Xlsx.Sheet(name, headings, rows). HeadingsOn re-reads a sheet from Xlsx.Read(file).");
            }

            if (rows.Length == 0)
            {
                throw new ArgumentException("Name the rows the headings stand on: HeadingsOn(3, 4).", nameof(rows));
            }

            for (var at = 0; at < rows.Length; at++)
            {
                if (rows[at] is < 1 or > CellReference.MaxRows)
                {
                    throw new ArgumentOutOfRangeException(nameof(rows), rows[at], $"A row is numbered as Excel numbers it, from 1 to {CellReference.MaxRows}.");
                }

                if (at > 0 && rows[at] <= rows[at - 1])
                {
                    throw new ArgumentException($"Name the heading rows from top to bottom, each once: HeadingsOn({string.Join(", ", rows.Order().Distinct())}).", nameof(rows));
                }
            }

            return new Sheet(Name, grid, rows);
        }
    }
}
