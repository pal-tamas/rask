using System.Globalization;

namespace Rask.Workbooks;

/// <summary>
///     A sheet's cells as read, by their 1-based row and column, and its merged ranges: what its headings and its rows
///     are taken from - and taken again, from other rows, by <see cref="Xlsx.Sheet.HeadingsOn" />.
/// </summary>
internal sealed class Grid(ReadLimits limits)
{
    private readonly Dictionary<int, List<object?>> cells = [];
    private readonly List<(int Top, int Left, int Bottom, int Right)> merges = [];
    private long slots;
    private int width;
    private int lastRow;

    public static InvalidDataException TooManyCells(ReadLimits limits) => new(string.Create(
        CultureInfo.InvariantCulture,
        $"The sheet holds more than {limits.MaxCells:N0} cells. Xlsx.Read stops there, so a file built to fill the memory cannot."));

    public void Set(int row, int column, object? value)
    {
        if (!cells.TryGetValue(row, out var line))
        {
            cells[row] = line = [];
        }

        if (line.Count < column)
        {
            Take(column - line.Count);
            line.AddRange(Enumerable.Repeat<object?>(null, column - line.Count));
        }

        line[column - 1] = value;
        if (value is not null)
        {
            width = Math.Max(width, column);
            lastRow = Math.Max(lastRow, row);
        }
    }

    public void Merge((int Top, int Left, int Bottom, int Right) range)
    {
        Take(1);
        merges.Add(range);
    }

    /// <summary>
    ///     A heading for every column, from the heading rows: a merged cell's value spreads over its whole range, and the
    ///     rows' parts are joined by a space, each trimmed - an empty part left out, and a cell merged down over several
    ///     heading rows counted once.
    /// </summary>
    public IReadOnlyList<string> Headings(int[] rows)
    {
        var spread = Spread(rows);
        var headings = new string[width];
        for (var column = 1; column <= width; column++)
        {
            var parts = new List<string>(rows.Length);
            (int, int)? previous = null;
            foreach (var row in rows)
            {
                var source = spread.GetValueOrDefault((row, column), (row, column));
                if (source == previous)
                {
                    continue;
                }

                previous = source;
                var part = CellText.Of(At(source.Item1, source.Item2)).Trim();
                if (part.Length > 0)
                {
                    parts.Add(part);
                }
            }

            headings[column - 1] = string.Join(' ', parts);
        }

        return headings;
    }

    /// <summary>Every row from the first, to the last that holds a value: as wide as the sheet, a row with nothing in it all empty.</summary>
    public IReadOnlyList<object?[]> Rows(int first)
    {
        var count = Math.Max(0, lastRow - first + 1);
        if ((long)count * width > limits.MaxCells)
        {
            throw TooManyCells(limits);
        }

        var rows = new object?[count][];
        for (var at = 0; at < count; at++)
        {
            var row = new object?[width];
            if (cells.TryGetValue(first + at, out var line))
            {
                line.CopyTo(0, row, 0, Math.Min(line.Count, width));
            }

            rows[at] = row;
        }

        return rows;
    }

    private object? At(int row, int column) =>
        cells.TryGetValue(row, out var line) && column <= line.Count ? line[column - 1] : null;

    // each cell of a heading row inside a merged range, to the range's first cell, which alone holds the value
    private Dictionary<(int Row, int Column), (int Row, int Column)> Spread(int[] rows)
    {
        var spread = new Dictionary<(int Row, int Column), (int Row, int Column)>();
        long work = 0;
        foreach (var (top, left, bottom, right) in merges)
        {
            // a file of overlapping ranges over the heading rows would otherwise cost merges x columns
            work += 1 + ((long)rows.Count(row => row >= top && row <= bottom) * Math.Max(0, Math.Min(right, width) - left + 1));
            if (work > limits.MaxCells)
            {
                throw TooManyCells(limits);
            }

            foreach (var row in rows.Where(row => row >= top && row <= bottom))
            {
                for (var column = left; column <= Math.Min(right, width); column++)
                {
                    spread[(row, column)] = (top, left);
                }
            }
        }

        return spread;
    }

    private void Take(long count)
    {
        slots += count;
        if (slots > limits.MaxCells)
        {
            throw TooManyCells(limits);
        }
    }
}
