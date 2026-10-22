using System.Globalization;
using System.Text;

namespace Rask.Printing.Tests;

public sealed class HtmlPrintTests(SharedPrinter shared) : IClassFixture<SharedPrinter>
{
    private const double A4Long = 297 / 25.4 * 72;
    private const double A4Short = 210 / 25.4 * 72;

    [Fact]
    public async Task Hungarian_text_comes_back_out_of_the_pdf_as_it_was_written()
    {
        var html = PrintedPdf.Html("<h1>Árvíztűrő tükörfúrógép</h1><p>őű ŐŰ</p>");

        var pdf = await shared.Printer.Print(PrintDocument.Of(html), TestContext.Current.CancellationToken);

        var text = Assert.Single(PrintedPdf.Pages(pdf)).Text;
        Assert.Contains("Árvíztűrő tükörfúrógép", text, StringComparison.Ordinal);
        Assert.Contains("őű ŐŰ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A4_landscape_paper_comes_out_wider_than_it_is_tall()
    {
        var html = PrintedPdf.Html("<p>Fekvő</p>");

        var pdf = await shared.Printer.Print(PrintDocument.Of(html, new PdfOptions { Page = PdfPage.A4.Landscape }), TestContext.Current.CancellationToken);

        var page = Assert.Single(PrintedPdf.Pages(pdf));
        Assert.Equal(A4Long, page.Width, tolerance: 1);
        Assert.Equal(A4Short, page.Height, tolerance: 1);
    }

    [Fact]
    public async Task A4_paper_comes_out_upright_unless_it_is_turned()
    {
        var html = PrintedPdf.Html("<p>Álló</p>");

        var pdf = await shared.Printer.Print(PrintDocument.Of(html), TestContext.Current.CancellationToken);

        var page = Assert.Single(PrintedPdf.Pages(pdf));
        Assert.Equal(A4Short, page.Width, tolerance: 1);
        Assert.Equal(A4Long, page.Height, tolerance: 1);
    }

    [Fact]
    public async Task A_table_over_three_pages_repeats_its_heading_on_every_page()
    {
        var html = PrintedPdf.Html(Table(rows: 150));

        var pdf = await shared.Printer.Print(PrintDocument.Of(html), TestContext.Current.CancellationToken);

        var pages = PrintedPdf.Pages(pdf);
        Assert.True(pages.Count >= 3, $"The table filled {pages.Count} pages.");
        Assert.All(pages, page => Assert.Contains("Számlaszám Rendszám Összeg", page.Text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_document_that_reaches_for_the_network_prints_without_it()
    {
        var html = PrintedPdf.Html(
            "<link rel=\"stylesheet\" href=\"https://example.com/site.css\">"
            + "<img src=\"https://example.com/logo.png\" alt=\"\"><p>Nyomtatva hálózat nélkül</p>");

        var pdf = await shared.Printer.Print(PrintDocument.Of(html), TestContext.Current.CancellationToken);

        Assert.Contains("Nyomtatva hálózat nélkül", Assert.Single(PrintedPdf.Pages(pdf)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_script_in_the_document_does_not_run()
    {
        var html = PrintedPdf.Html("<p id=\"p\">Eredeti</p><script>document.getElementById('p').textContent = 'Átírva';</script>");

        var pdf = await shared.Printer.Print(PrintDocument.Of(html), TestContext.Current.CancellationToken);

        var text = Assert.Single(PrintedPdf.Pages(pdf)).Text;
        Assert.Contains("Eredeti", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Átírva", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_that_insists_on_being_shown_is_still_held_until_it_has_loaded()
    {
        var html = Encoding.UTF8.GetString(PrintHold.Utf8(PrintedPdf.Html("<style>:root { display: block !important; }</style><p>Visszatartva</p>")))
            .Replace(PrintHold.Id, "never-released", StringComparison.Ordinal);

        var pdf = await shared.Printer.Print(PrintDocument.Of(html), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Visszatartva", Assert.Single(PrintedPdf.Pages(pdf)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_that_insists_on_being_shown_prints_once_it_has_loaded()
    {
        var html = PrintedPdf.Html("<style>:root { display: block !important; }</style><p>Kiadva</p>");

        var pdf = await shared.Printer.Print(PrintDocument.Of(html), TestContext.Current.CancellationToken);

        Assert.Contains("Kiadva", Assert.Single(PrintedPdf.Pages(pdf)).Text, StringComparison.Ordinal);
    }

    internal static string Table(int rows)
    {
        var html = new StringBuilder("<table><thead><tr><th>Számlaszám</th><th>Rendszám</th><th>Összeg</th></tr></thead><tbody>");
        for (var row = 1; row <= rows; row++)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td>SZ-{row:D5}</td><td>ABC-{row % 1000:D3}</td><td>{row * 1250:N0} Ft</td></tr>");
        }

        return html.Append("</tbody></table>").ToString();
    }
}
