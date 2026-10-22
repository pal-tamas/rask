using System.Globalization;

namespace Rask.Printing;

/// <summary>
/// Cuts a long table into a table per printed page before it prints. Chromium's print layout of one table pays for
/// every row on every page it runs to: a 10,000-row kit table (590 A4 pages) ran its renderer past 13 GB and the print
/// failed, a plain 5,000-row one took 10 GB. Cut where Chromium breaks the pages anyway — each piece with the table's
/// own columns, heading and footing — it prints the same pages, in a third of the memory. See <c>PrintTables.js</c>.
/// </summary>
internal static class PrintTables
{
    /// <summary>A table up to this many pages long prints whole: what it costs is not worth a change to the document.</summary>
    internal const int LongerThanPages = 10;

    private static readonly string Script = Read();

    /// <summary>
    /// The expression that cuts every table longer than <see cref="LongerThanPages"/> pages and returns how many it cut;
    /// one it could not cut is left whole, and a page the script trips on prints with what was cut, answering -1.
    /// </summary>
    /// <param name="pageHeight">A page's content height, in the CSS pixels the document is laid out in.</param>
    public static string Cut(double pageHeight) => string.Create(
        CultureInfo.InvariantCulture,
        $"(() => {{ try {{ return ({Script})({pageHeight:0.###}, {LongerThanPages}); }} catch {{ return -1; }} }})()");

    private static string Read()
    {
        using var stream = typeof(PrintTables).Assembly.GetManifestResourceStream("Rask.Printing.PrintTables.js")
            ?? throw new InvalidOperationException("Rask.Pdf was built without PrintTables.js.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
