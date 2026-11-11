namespace Rask.Workbooks;

/// <summary>A cell's place as Excel writes it (<c>B5</c>), as 1-based row and column numbers within Excel's own bounds.</summary>
internal static class CellReference
{
    public const int MaxRows = 1_048_576;
    public const int MaxColumns = 16_384;

    /// <summary>A place (<c>B5</c>), or a column alone when the row is left out (<c>B</c> - as a range may write it).</summary>
    public static (int Row, int Column) Parse(ReadOnlySpan<char> reference)
    {
        var column = 0;
        var at = 0;
        while (at < reference.Length && char.IsAsciiLetter(reference[at]))
        {
            column = (column * 26) + (char.ToUpperInvariant(reference[at]) - 'A' + 1);
            if (column > MaxColumns)
            {
                throw Malformed(reference);
            }

            at++;
        }

        var row = 0;
        for (; at < reference.Length; at++)
        {
            if (!char.IsAsciiDigit(reference[at]))
            {
                throw Malformed(reference);
            }

            row = (row * 10) + (reference[at] - '0');
            if (row > MaxRows)
            {
                throw Malformed(reference);
            }
        }

        return column > 0 ? (row, column) : throw Malformed(reference);
    }

    /// <summary>A range (<c>A3:C4</c>), its corners in order; a single cell is a range of one.</summary>
    public static (int Top, int Left, int Bottom, int Right) Range(string range)
    {
        var colon = range.IndexOf(':', StringComparison.Ordinal);
        var first = Parse(colon < 0 ? range : range.AsSpan(0, colon));
        var last = colon < 0 ? first : Parse(range.AsSpan(colon + 1));
        return (Math.Min(first.Row, last.Row), Math.Min(first.Column, last.Column), Math.Max(first.Row, last.Row), Math.Max(first.Column, last.Column));
    }

    private static InvalidDataException Malformed(ReadOnlySpan<char> reference) =>
        Package.Malformed($"'{reference}' is not a cell Excel has (columns A to XFD, rows 1 to {MaxRows})");
}
