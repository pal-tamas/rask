using Rask.Pdf.Tests;

namespace Rask.Printing.Tests.Host;

/// <summary>Pdf.From numbers the pages in the caller's language, read back out of the printed file.</summary>
public sealed class PageNumberLanguageTests
{
    [Fact]
    public async Task A_hungarian_caller_reads_one_of_three_pages_in_hungarian_at_the_foot()
    {
        Assert.SkipWhen(TestChromium.Installed is null, "No Chromium is installed here, so nothing printed.");
        await using var host = await PrintHost.Start();

        var pdf = await PrintCulture.In("hu-HU", () => host.As(PrintHost.Anonymous, () => Pdf.From(ThreePages())));

        var pages = PrintedPdf.Pages(pdf);
        Assert.Equal(3, pages.Count);
        Assert.Contains("1 / 3 oldal", pages[0].Text, StringComparison.Ordinal);
        Assert.Contains("3 / 3 oldal", pages[2].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_english_caller_reads_one_of_three_pages_with_no_word_at_the_foot()
    {
        Assert.SkipWhen(TestChromium.Installed is null, "No Chromium is installed here, so nothing printed.");
        await using var host = await PrintHost.Start();

        var pdf = await PrintCulture.In("en-US", () => host.As(PrintHost.Anonymous, () => Pdf.From(ThreePages())));

        var pages = PrintedPdf.Pages(pdf);
        Assert.Equal(3, pages.Count);
        Assert.Contains("1 / 3", pages[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("oldal", pages[0].Text, StringComparison.Ordinal);
    }

    private static Component ThreePages() => PrintTestApp.ThreePages();
}
