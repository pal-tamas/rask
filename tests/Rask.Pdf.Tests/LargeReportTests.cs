using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Rask.Printing.Tests;

public sealed class LargeReportTests
{
    [Fact]
    public async Task A_five_thousand_row_report_prints_with_its_heading_on_the_first_and_last_page()
    {
        await using var printer = TestChromium.Printer(timeout: TimeSpan.FromMinutes(2));
        var html = PrintedPdf.Html(Report(rows: 5000));
        await printer.Print(PrintedPdf.Html("<p>Bemelegítés</p>"), PdfPage.A4, TestContext.Current.CancellationToken);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var clock = Stopwatch.StartNew();

        var pdf = await printer.Print(html, PdfPage.A4.Landscape, TestContext.Current.CancellationToken);

        clock.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var pages = PrintedPdf.Pages(pdf);
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"5000 rows: {html.Length:N0} chars of HTML -> {pages.Count} pages, {pdf.Length:N0} bytes in {clock.ElapsedMilliseconds} ms, {allocated:N0} bytes allocated"));
        Assert.True(pages.Count > 50, $"The report filled {pages.Count} pages.");
        Assert.Contains("Számlaszám", pages[0].Text, StringComparison.Ordinal);
        Assert.Contains("Számlaszám", pages[^1].Text, StringComparison.Ordinal);
        Assert.Contains("SZ-05000", pages[^1].Text, StringComparison.Ordinal);
    }

    private static string Report(int rows)
    {
        var html = new StringBuilder(
            "<h1>Gázolaj-visszatérítés — 2026. III. negyedév</h1><table><thead><tr><th>Dátum</th><th>Számlaszám</th>"
            + "<th>Rendszám</th><th>Kút</th><th>Liter</th><th>Nettó</th><th>Visszatérítés</th></tr></thead><tbody>");
        var day = new DateOnly(2026, 7, 1);
        for (var row = 1; row <= rows; row++)
        {
            html.Append(
                CultureInfo.InvariantCulture,
                $"<tr><td>{day.AddDays(row % 92):yyyy.MM.dd}</td><td>SZ-{row:D5}</td><td>ŐŰ-{row % 400:D3}</td>"
                + $"<td>Győr, Fehérvári út {row % 120}</td><td>{row % 90 + 10},5</td><td>{row * 731 % 250000:N0} Ft</td>"
                + $"<td>{row * 37 % 12000:N0} Ft</td></tr>");
        }

        return html.Append("</tbody></table>").ToString();
    }
}
