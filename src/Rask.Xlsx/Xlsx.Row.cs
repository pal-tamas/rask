using System.Collections;

namespace Rask.Workbooks;

public static partial class Xlsx
{
    /// <summary>
    ///     A row of a <see cref="Sheet" />: its cells by place, as any list, and by heading - <c>row["Litres"]</c>.
    ///     A heading is matched without regard to case and with the whitespace around it trimmed.
    /// </summary>
    public sealed class Row : IReadOnlyList<object?>
    {
        private readonly IReadOnlyList<object?> cells;
        private readonly HeadingIndex index;

        internal Row(IReadOnlyList<object?> cells, HeadingIndex index)
        {
            this.cells = cells;
            this.index = index;
        }

        /// <inheritdoc />
        public int Count => cells.Count;

        /// <inheritdoc />
        public object? this[int index] => cells[index];

        /// <summary>
        ///     The cell under that heading; <see langword="null" /> when the cell is empty. A heading the sheet does not have
        ///     throws a <see cref="KeyNotFoundException" /> that lists the ones it does.
        /// </summary>
        public object? this[string heading]
        {
            get
            {
                var column = index.Column(heading);
                return column < cells.Count ? cells[column] : null;
            }
        }

        /// <inheritdoc />
        public IEnumerator<object?> GetEnumerator() => cells.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
