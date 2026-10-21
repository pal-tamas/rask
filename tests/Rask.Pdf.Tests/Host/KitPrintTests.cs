namespace Rask.Printing.Tests.Host;

/// <summary>
/// A kit page printed through Pdf.From: the kit's print defaults leave its chrome and controls off, and run the table on with its heading on every page. RASK_PDF_SHOTS=dir also writes it there.
/// </summary>
public sealed class KitPrintTests
{
    [Fact]
    public async Task A_kit_page_prints_its_content_without_its_chrome_or_controls()
    {
        Assert.SkipWhen(TestChromium.Installed is null, "No Chromium is installed here, so nothing printed.");
        await using var host = await PrintHost.Start(SiteDemoPdfTests.Fonts());

        var pdf = await host.As(PrintHost.Anonymous, () => Pdf.From(PrintTestApp.KitPage()));

        await Keep(pdf);
        var text = string.Join(' ', PrintedPdf.Pages(pdf).Select(page => page.Text));
        Assert.Contains("Visszatérítések", text, StringComparison.Ordinal);
        Assert.Contains("Admin", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Mentés gomb", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Oldalsáv menüpont", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Fejléc felirat", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Showing", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_kit_table_runs_on_with_its_heading_on_every_page_and_its_long_cells_wrapped()
    {
        Assert.SkipWhen(TestChromium.Installed is null, "No Chromium is installed here, so nothing printed.");
        await using var host = await PrintHost.Start();

        var pdf = await host.As(PrintHost.Anonymous, () => Pdf.From(PrintTestApp.KitPage()));

        var pages = PrintedPdf.Pages(pdf);
        Assert.True(pages.Count >= 3, $"The table filled {pages.Count} pages.");
        Assert.All(pages, page => Assert.Contains("Számlaszám Megjegyzés Összeg", page.Text, StringComparison.Ordinal));
        Assert.Contains("SZ-00090", pages[^1].Text, StringComparison.Ordinal);
        Assert.Contains("kifér a hasábba.", pages[^1].Text, StringComparison.Ordinal);
    }

    private static async Task Keep(byte[] pdf)
    {
        if (Environment.GetEnvironmentVariable("RASK_PDF_SHOTS") is { Length: > 0 } shots)
        {
            Directory.CreateDirectory(shots);
            await File.WriteAllBytesAsync(Path.Combine(shots, "kit.pdf"), pdf, TestContext.Current.CancellationToken);
        }
    }
}
