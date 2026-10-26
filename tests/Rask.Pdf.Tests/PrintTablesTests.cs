using System.Globalization;
using System.Text;

namespace Rask.Printing.Tests;

public sealed class PrintTablesTests(SharedPrinter shared) : IClassFixture<SharedPrinter>
{
    [Fact]
    public async Task A_table_longer_than_ten_pages_is_cut_at_its_pages_and_prints_the_pages_it_would_have_whole()
    {
        var cut = Table(rows: 1500, columns: "");
        var whole = Table(rows: 1500, columns: "<colgroup></colgroup>");

        var (cutPdf, cutTables) = await Print(cut);
        var (wholePdf, wholeTables) = await Print(whole);

        Assert.Equal(1, cutTables);
        Assert.Equal(0, wholeTables);
        var cutPages = PrintedPdf.Pages(cutPdf);
        Assert.True(cutPages.Count > PrintTables.LongerThanPages, $"The table filled {cutPages.Count} pages.");
        Assert.Equal(PrintedPdf.Pages(wholePdf).Select(page => page.Text), cutPages.Select(page => page.Text));
    }

    [Fact]
    public async Task A_cut_table_prints_every_row_once_in_order_under_its_heading_on_every_page()
    {
        var html = Table(rows: 1500, columns: "");

        var (pdf, _) = await Print(html);

        var pages = PrintedPdf.Pages(pdf);
        Assert.All(pages, page => Assert.Contains("Számlaszám Rendszám Összeg", page.Text, StringComparison.Ordinal));
        var printed = pages.SelectMany(page => page.Text.Split(' ').Where(word => word.StartsWith("SZ-", StringComparison.Ordinal))).ToList();
        Assert.Equal(Enumerable.Range(1, 1500).Select(Invoice), printed);
    }

    [Fact]
    public async Task A_table_wider_than_the_page_is_cut_where_the_shrunk_pages_break()
    {
        var cut = Table(rows: 1500, columns: "", wide: true);
        var whole = Table(rows: 1500, columns: "<colgroup></colgroup>", wide: true);

        var (cutPdf, cutTables) = await Print(cut);
        var (wholePdf, _) = await Print(whole);

        Assert.Equal(1, cutTables);
        Assert.Equal(PrintedPdf.Pages(wholePdf).Select(page => page.Text), PrintedPdf.Pages(cutPdf).Select(page => page.Text));
    }

    [Fact]
    public async Task A_table_with_a_row_spanning_rows_prints_whole()
    {
        var html = Table(rows: 1500, columns: "").Replace("<td>SZ-00007</td>", "<td rowspan=\"2\">SZ-00007</td>", StringComparison.Ordinal);

        var (pdf, tables) = await Print(html);

        Assert.Equal(0, tables);
        Assert.Contains("SZ-01500", PrintedPdf.Pages(pdf)[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Chromium_printing_failed_is_told_as_its_likely_cause_and_what_to_do()
    {
        var refusal = new InvalidOperationException("Chromium refused Page.printToPDF: Printing failed");

        var named = HtmlPrint.Named(refusal);

        Assert.Contains("ran out of room", named.Message, StringComparison.Ordinal);
        Assert.Contains("Print it in parts", named.Message, StringComparison.Ordinal);
        Assert.Same(refusal, named.InnerException);
    }

    [Fact]
    public void A_page_is_laid_out_inside_its_margins_and_the_foot_in_css_pixels()
    {
        var upright = new PdfOptions();
        var turned = new PdfOptions { Page = PdfPage.A4.Landscape, PageNumbers = false };

        var (width, height) = PrintLayout.Content(upright);
        var (turnedWidth, turnedHeight) = PrintLayout.Content(turned);

        Assert.Equal((210 - 20) / 25.4 * 96, width, tolerance: 0.01);
        Assert.Equal((297 - 10 - 12) / 25.4 * 96, height, tolerance: 0.01);
        Assert.Equal((297 - 20) / 25.4 * 96, turnedWidth, tolerance: 0.01);
        Assert.Equal((210 - 20) / 25.4 * 96, turnedHeight, tolerance: 0.01);
    }

    private async Task<(byte[] Pdf, int TablesCut)> Print(string html)
    {
        var cut = -1;
        shared.Printer.Printed = (_, progress) => cut = progress.TablesCut;
        try
        {
            var pdf = await shared.Printer.Print(PrintDocument.Of(PrintedPdf.Html(html)), TestContext.Current.CancellationToken);
            return (pdf, cut);
        }
        finally
        {
            shared.Printer.Printed = null;
        }
    }

    // An empty <colgroup> changes nothing on the page, and keeps the table from being cut: the same table, printed whole.
    private static string Table(int rows, string columns, bool wide = false)
    {
        var html = new StringBuilder(
            $"<h1>Visszatérítés</h1><table>{columns}<thead><tr><th>Számlaszám</th><th>Rendszám</th><th>Összeg</th></tr></thead><tbody>");
        for (var row = 1; row <= rows; row++)
        {
            var place = wide ? $"Győr, Fehérvári út {row % 120}, a kút a sarkon, a mosó mögött" : $"ABC-{row % 1000:D3}";
            html.Append(CultureInfo.InvariantCulture, $"<tr><td>{Invoice(row)}</td><td>{place}</td><td>{row * 1250:N0} Ft</td></tr>");
        }

        html.Append("</tbody></table><p>Vége.</p>");
        return wide ? "<style>td { white-space: nowrap; }</style>" + html : html.ToString();
    }

    private static string Invoice(int row) => string.Create(CultureInfo.InvariantCulture, $"SZ-{row:D5}");
}
