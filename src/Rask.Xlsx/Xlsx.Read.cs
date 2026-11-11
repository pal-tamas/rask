namespace Rask.Workbooks;

public static partial class Xlsx
{
    /// <summary>
    ///     The first sheet of a workbook, its headings on row 1 and its rows below them. A cell comes back as the writer's
    ///     own types: a text as a <see cref="string" />, a number as a <see cref="decimal" />, <c>TRUE</c>/<c>FALSE</c> as a
    ///     <see cref="bool" />, a number shown as a date as a <see cref="Day" /> (or a <see cref="Moment" /> when it shows
    ///     a time), an empty cell as <see langword="null" />, a formula as what Excel last worked it out to.
    /// </summary>
    /// <remarks>
    ///     <code>
    ///     foreach (var row in Xlsx.Read(file).Rows)
    ///         litres += (decimal)row["Litres"]!;
    ///     </code>
    ///     A file that is not a workbook, or is broken, throws an <see cref="InvalidDataException" /> that says why.
    /// </remarks>
    public static Sheet Read(byte[] file) => Read(file, null, ReadLimits.Default);

    /// <summary>
    ///     The sheet of that name - compared as Excel compares them, without regard to case - with its headings on row 1.
    ///     A name the workbook has no sheet of throws an <see cref="ArgumentException" /> that lists the ones it has.
    /// </summary>
    public static Sheet Read(byte[] file, string sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return Read(file, sheet, ReadLimits.Default);
    }

    internal static Sheet Read(byte[] file, string? sheet, ReadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(file);
        using var package = new Package(file, limits);
        var workbook = Workbook.Open(package);
        var (name, part) = workbook.Sheet(sheet);
        var styles = workbook.Styles is { } stylesPart && package.Has(stylesPart) ? package.Read(stylesPart, CellStyles.Read) : CellStyles.None;
        var shared = workbook.SharedStrings is { } stringsPart && package.Has(stringsPart)
            ? package.Read(stringsPart, reader => SharedStrings.Read(reader, limits))
            : [];
        var decoder = new CellDecoder(shared, styles, workbook.Date1904);
        var grid = package.Read(part, reader => WorksheetReader.Read(reader, decoder, limits));
        return new Sheet(name, grid, [1]);
    }
}
