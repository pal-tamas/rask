using System.IO.Compression;

namespace Rask.Workbooks;

/// <summary>
///     Writes an Excel workbook: a sheet is a row of headings, then rows of cells. A cell is a
///     text, a number, a moment (day and time) or a day; an empty one is left out, as Excel does.
///     The headings are Excel's own: in bold, staying in place while the rows scroll, each with the button that sorts
///     and filters by its column; and every column is as wide as the longest thing in it. An amount - a number
///     with a fraction to it - shows two to four decimals with its thousands apart, whatever it holds beneath.
///     Rows can be folded beneath the row before them - Excel's own grouping, with its plus and minus at the side.
/// </summary>
/// <remarks>
///     <code>
///     var file = Xlsx.Write("Invoices", ["Number", "Day", "Amount"], rows);
///     return Results.File(file, Xlsx.ContentType, "invoices.xlsx");
///     </code>
/// </remarks>
public static partial class Xlsx
{
    /// <summary>The media type of an .xlsx file, for a download's <c>Content-Type</c>.</summary>
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>A workbook of one sheet.</summary>
    public static byte[] Write(string sheet, IReadOnlyList<string> headings, IEnumerable<IReadOnlyList<object?>> rows, IReadOnlyList<object?>? total = null) =>
        Write(new Sheet(sheet, headings, rows, total));

    /// <summary>A workbook of the sheets, in the order given.</summary>
    public static byte[] Write(params Sheet[] sheets)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        SheetNames.Check(sheets);

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WorkbookParts.Write(zip, sheets);
        }

        return buffer.ToArray();
    }
}
