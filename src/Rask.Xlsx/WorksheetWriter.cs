using System.Globalization;

namespace Rask.Workbooks;

/// <summary>
///     One sheet's XML, written straight into the archive entry. The rows are held (the columns' widths come before
///     the first of them and need every row), but the sheet itself is never built as one string.
/// </summary>
internal static class WorksheetWriter
{
    internal const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    internal const string Office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const int MaxWidth = 80;
    private const int MaxColumns = 16384;

    public static void Write(TextWriter writer, Xlsx.Sheet sheet)
    {
        if (sheet.Headings.Count > MaxColumns)
        {
            throw new ArgumentException($"Sheet '{sheet.Name}' has {sheet.Headings.Count} columns; Excel allows {MaxColumns}.", nameof(sheet));
        }

        var rows = sheet.Rows.ToList();
        var last = $"{ColumnName(sheet.Headings.Count - 1)}{rows.Count + 1}";

        // the row that sums a fold up stands above it, as on the page; the headings stay while the rows scroll
        writer.Write($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"{Main}\"><sheetPr><outlinePr summaryBelow=\"0\"/></sheetPr>");
        writer.Write("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        writer.Write("<sheetFormatPr defaultRowHeight=\"15\" outlineLevelRow=\"1\"/><cols>");
        for (var column = 0; column < sheet.Headings.Count; column++)
        {
            writer.Write(string.Create(CultureInfo.InvariantCulture, $"<col min=\"{column + 1}\" max=\"{column + 1}\" width=\"{Width(sheet, rows, column)}\" customWidth=\"1\"/>"));
        }

        writer.Write("</cols><sheetData>");
        WriteRows(writer, sheet.Headings, rows, sheet.Total);
        writer.Write($"</sheetData><autoFilter ref=\"A1:{last}\"/></worksheet>");
    }

    // the heading's button takes room beside its words
    private static int Width(Xlsx.Sheet sheet, List<IReadOnlyList<object?>> rows, int column)
    {
        var widest = column < (sheet.Total?.Count ?? 0) ? CellWriter.Wide(sheet.Total![column]) : 0;
        foreach (var row in rows)
        {
            if (column < row.Count)
            {
                widest = Math.Max(widest, CellWriter.Wide(row[column]));
            }
        }

        return Math.Min(MaxWidth, Math.Max(CellWriter.Wide(sheet.Headings[column]) + 3, widest + 2));
    }

    private static void WriteRows(TextWriter writer, IReadOnlyList<string> headings, List<IReadOnlyList<object?>> rows, IReadOnlyList<object?>? total)
    {
        var count = 0;
        void Row(IReadOnlyList<object?> values, bool folded, bool foldsOthers)
        {
            count++;
            writer.Write(string.Create(CultureInfo.InvariantCulture, $"<row r=\"{count}\"{(folded ? " outlineLevel=\"1\" hidden=\"1\"" : "")}{(foldsOthers ? " collapsed=\"1\"" : "")}>"));
            for (var column = 0; column < values.Count; column++)
            {
                writer.Write(CellWriter.Cell(values[column], $"{ColumnName(column)}{count}"));
            }

            writer.Write("</row>");
        }

        // a row knows it folds others only by the one after it
        IReadOnlyList<object?> waiting = [.. headings.Select(heading => (object?)new Xlsx.Strong(heading))];
        foreach (var row in rows)
        {
            Row(waiting, waiting is Xlsx.Folded, row is Xlsx.Folded && waiting is not Xlsx.Folded);
            waiting = row;
        }

        Row(waiting, waiting is Xlsx.Folded, foldsOthers: false);
        if (total is not null)
        {
            // a column's sum is over its amounts in the rows above; a row that sums others up holds formulas, and is not counted twice
            Row([.. total.Select((cell, column) => cell is Xlsx.Sum
                ? new Xlsx.Formula($"SUBTOTAL(9,{ColumnName(column)}2:{ColumnName(column)}{rows.Count + 1})", rows.Sum(row => column < row.Count && row[column] is decimal amount ? amount : 0))
                : cell)], folded: false, foldsOthers: false);
        }
    }

    /// <summary>A, B ... Z, AA, AB ... as Excel names the columns.</summary>
    internal static string ColumnName(int zeroBased)
    {
        var name = new System.Text.StringBuilder(3);
        for (var n = zeroBased + 1; n > 0; n = (n - 1) / 26)
        {
            name.Insert(0, (char)('A' + ((n - 1) % 26)));
        }

        return name.ToString();
    }
}
