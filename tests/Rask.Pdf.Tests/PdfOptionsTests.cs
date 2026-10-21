using System.Text;

namespace Rask.Printing.Tests;

public sealed class PdfOptionsTests(SharedPrinter shared) : IClassFixture<SharedPrinter>
{
    [Fact]
    public async Task Every_page_carries_the_footer_and_its_number_out_of_the_total()
    {
        var document = PrintDocument.Of(
            PrintedPdf.Html(HtmlPrintTests.Table(rows: 150)),
            new PdfOptions { Footer = "Visszatérítés — 2026. III. negyedév" });

        var pdf = await shared.Printer.Print(document, TestContext.Current.CancellationToken);

        var pages = PrintedPdf.Pages(pdf);
        Assert.All(pages, page => Assert.Contains("Visszatérítés — 2026. III. negyedév", page.Text, StringComparison.Ordinal));
        Assert.Contains($"1 / {pages.Count}", pages[0].Text, StringComparison.Ordinal);
        Assert.Contains($"{pages.Count} / {pages.Count}", pages[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Page_numbers_switched_off_leave_the_foot_empty()
    {
        var document = PrintDocument.Of(PrintedPdf.Html("<p>Lábléc nélkül</p>"), new PdfOptions { PageNumbers = false });

        var pdf = await shared.Printer.Print(document, TestContext.Current.CancellationToken);

        Assert.Equal("Lábléc nélkül", Assert.Single(PrintedPdf.Pages(pdf)).Text);
    }

    [Fact]
    public async Task A_footer_is_printed_as_text_never_as_markup()
    {
        var document = PrintDocument.Of(PrintedPdf.Html("<p>Tartalom</p>"), new PdfOptions { Footer = "<b>Ügyfél</b> & társa" });

        var pdf = await shared.Printer.Print(document, TestContext.Current.CancellationToken);

        Assert.Contains("<b>Ügyfél</b> & társa", Assert.Single(PrintedPdf.Pages(pdf)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tagged_pdf_carries_the_structure_an_untagged_one_leaves_out()
    {
        var html = PrintedPdf.Html("<h1>Összesítő</h1>" + HtmlPrintTests.Table(rows: 20));

        var tagged = await shared.Printer.Print(PrintDocument.Of(html, new PdfOptions { Tagged = true }), TestContext.Current.CancellationToken);
        var untagged = await shared.Printer.Print(PrintDocument.Of(html), TestContext.Current.CancellationToken);

        Assert.Contains("/StructTreeRoot", Encoding.Latin1.GetString(tagged), StringComparison.Ordinal);
        Assert.DoesNotContain("/StructTreeRoot", Encoding.Latin1.GetString(untagged), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wider_margins_move_the_text_further_in_from_the_edge()
    {
        var html = PrintedPdf.Html("<p>Margó</p>");

        var narrow = await shared.Printer.Print(PrintDocument.Of(html, new PdfOptions { Margins = PdfMargins.Millimetres(5) }), TestContext.Current.CancellationToken);
        var wide = await shared.Printer.Print(PrintDocument.Of(html, new PdfOptions { Margins = PdfMargins.Millimetres(30) }), TestContext.Current.CancellationToken);

        Assert.Equal(25 / 25.4 * 72, PrintedPdf.FirstLetterLeft(wide) - PrintedPdf.FirstLetterLeft(narrow), tolerance: 1);
    }

    [Fact]
    public async Task A_document_longer_than_the_page_limit_fails_rather_than_come_out_cut()
    {
        await using var printer = new ChromiumPrinter(new ChromiumSettings
        {
            ChromiumPath = TestChromium.Path, NoSandbox = TestChromium.NoSandbox, MaxPages = 2,
        });

        var tooLong = await Assert.ThrowsAsync<InvalidOperationException>(() => printer.Print(
            PrintDocument.Of(PrintedPdf.Html(HtmlPrintTests.Table(rows: 150))), TestContext.Current.CancellationToken));

        Assert.Contains("more than 2 pages", tooLong.Message, StringComparison.Ordinal);
        Assert.Contains("Rask:Pdf:MaxPages", tooLong.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_document_exactly_at_the_page_limit_prints()
    {
        await using var printer = new ChromiumPrinter(new ChromiumSettings
        {
            ChromiumPath = TestChromium.Path, NoSandbox = TestChromium.NoSandbox, MaxPages = 1,
        });

        var pdf = await printer.Print(PrintDocument.Of(PrintedPdf.Html("<p>Egy oldal</p>")), TestContext.Current.CancellationToken);

        Assert.Single(PrintedPdf.Pages(pdf));
    }

    [Fact]
    public void Counting_pages_tells_a_page_from_the_page_tree()
    {
        var pdf = Encoding.ASCII.GetBytes("<< /Type /Pages /Count 2 >> << /Type /Page >> << /Type /Page /Parent 1 0 R >>");

        var pages = PdfPageCount.Of(pdf);

        Assert.Equal(2, pages);
    }
}
