namespace Rask.Workbooks;

/// <summary>
///     A sheet's columns by heading, for <c>row["Litres"]</c>: compared ordinally without regard to case, as Excel
///     compares a table's column names, with the whitespace around either side trimmed.
/// </summary>
internal sealed class HeadingIndex
{
    private readonly IReadOnlyList<string> headings;
    private readonly Dictionary<string, int> columns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<int>> twice = new(StringComparer.OrdinalIgnoreCase);

    public HeadingIndex(IReadOnlyList<string> headings)
    {
        this.headings = headings;
        for (var column = 0; column < headings.Count; column++)
        {
            var heading = headings[column]?.Trim();
            if (!string.IsNullOrEmpty(heading) && !columns.TryAdd(heading, column))
            {
                if (!twice.TryGetValue(heading, out var shared))
                {
                    shared = [columns[heading]];
                    twice.Add(heading, shared);
                }

                shared.Add(column);
            }
        }
    }

    public int Column(string heading)
    {
        ArgumentNullException.ThrowIfNull(heading);
        var key = heading.Trim();
        if (!columns.TryGetValue(key, out var column))
        {
            throw new KeyNotFoundException(
                $"No column is headed '{key}'. The headings are: {string.Join(", ", headings.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => $"'{h}'"))}. "
                + "If they stand lower on the sheet, read them from there: Xlsx.Read(file).HeadingsOn(3, 4).");
        }

        return twice.TryGetValue(key, out var shared) ? throw Ambiguous(key, shared) : column;
    }

    // Columns A and B both headed by one title merged across them is the usual cause: a title row read as the headings.
    private static InvalidOperationException Ambiguous(string heading, List<int> shared) =>
        new($"Columns {string.Join(", ", shared.Select(WorksheetWriter.ColumnName))} are all headed '{heading}', so row[\"{heading}\"] "
            + $"cannot choose one. Read the cell by its place, as row[{shared[0]}] for column {WorksheetWriter.ColumnName(shared[0])}; "
            + "or, if that heading is a title merged across them, read the headings from the rows below it: Xlsx.Read(file).HeadingsOn(2).");
}
