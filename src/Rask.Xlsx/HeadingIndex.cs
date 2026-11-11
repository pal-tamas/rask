namespace Rask.Workbooks;

/// <summary>
///     A sheet's columns by heading, for <c>row["Litres"]</c>: compared ordinally without regard to case, as Excel
///     compares a table's column names, with the whitespace around either side trimmed.
/// </summary>
internal sealed class HeadingIndex
{
    private readonly IReadOnlyList<string> headings;
    private readonly Dictionary<string, int> columns = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> twice = new(StringComparer.OrdinalIgnoreCase);

    public HeadingIndex(IReadOnlyList<string> headings)
    {
        this.headings = headings;
        for (var column = 0; column < headings.Count; column++)
        {
            var heading = headings[column]?.Trim();
            if (!string.IsNullOrEmpty(heading) && !columns.TryAdd(heading, column))
            {
                twice.Add(heading);
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

        return twice.Contains(key)
            ? throw new InvalidOperationException($"More than one column is headed '{key}'; read the cell by its place instead, as row[{column}] for the first.")
            : column;
    }
}
