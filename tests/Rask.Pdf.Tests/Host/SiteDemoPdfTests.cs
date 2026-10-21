using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Rask.Printing.Tests.Host;

/// <summary>
/// rask.sh is static files and cannot print, so the PDF its demo downloads is printed here, from the very report the
/// site shows. Set RASK_PDF_SITE_DEMO_WRITE=1 to rewrite the committed file; RASK_PDF_SHOTS=dir also writes the
/// portrait and landscape prints there, for a person to look at.
/// </summary>
public sealed partial class SiteDemoPdfTests
{
    private const string Footer = "Gázolaj-visszatérítés — 2026. III. negyedév";

    [Fact]
    public async Task The_pdf_rask_sh_serves_is_still_the_report_the_site_shows()
    {
        Assert.SkipWhen(TestChromium.Installed is null, "No Chromium is installed here, so nothing printed.");
        await using var host = await PrintHost.Start(Fonts());

        var printed = await host.As(PrintHost.Anonymous, () => Pdf.From(PrintTestApp.SiteReport(), new PdfOptions { Footer = Footer }));

        await Keep(printed, host);
        var served = PrintedPdf.Pages(await File.ReadAllBytesAsync(SitePdf(), TestContext.Current.CancellationToken));
        var table = served.Where(page => Invoice().IsMatch(page.Text)).ToList();
        Assert.True(table.Count >= 3, $"The served report's table runs over {table.Count} pages; the demo shows one over three.");
        Assert.All(table, page => Assert.Contains("Számlaszám", page.Text, StringComparison.Ordinal));
        Assert.All(served, page => Assert.Contains(Footer, page.Text, StringComparison.Ordinal));
        Assert.Equal(Invoices(PrintedPdf.Pages(printed)), Invoices(served));
    }

    // Every invoice number, in order: the rows themselves, whatever page a font's metrics put them on.
    private static string[] Invoices(IEnumerable<(double Width, double Height, string Text)> pages) =>
        [.. pages.SelectMany(page => Invoice().Matches(page.Text).Select(match => match.Value))];

    private static async Task Keep(byte[] printed, PrintHost host)
    {
        if (Environment.GetEnvironmentVariable("RASK_PDF_SITE_DEMO_WRITE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SitePdf())!);
            await File.WriteAllBytesAsync(SitePdf(), printed, TestContext.Current.CancellationToken);
        }

        if (Environment.GetEnvironmentVariable("RASK_PDF_SHOTS") is { Length: > 0 } shots)
        {
            Directory.CreateDirectory(shots);
            await File.WriteAllBytesAsync(Path.Combine(shots, "portrait.pdf"), printed, TestContext.Current.CancellationToken);
            var landscape = await host.As(PrintHost.Anonymous, () => Pdf.From(
                PrintTestApp.SiteReport(), new PdfOptions { Page = PdfPage.A4.Landscape, Footer = Footer }));
            await File.WriteAllBytesAsync(Path.Combine(shots, "landscape.pdf"), landscape, TestContext.Current.CancellationToken);
        }
    }

    private static Dictionary<string, byte[]> Fonts() => new(StringComparer.Ordinal)
    {
        ["/fonts/inter-latin.woff2"] = File.ReadAllBytes(Path.Combine(Repo(), "src", "Rask.Ui", "Fonts", "inter-latin.woff2")),
        ["/fonts/inter-latin-ext.woff2"] = File.ReadAllBytes(Path.Combine(Repo(), "src", "Rask.Ui", "Fonts", "inter-latin-ext.woff2")),
    };

    private static string SitePdf() =>
        Path.Combine(Repo(), "src", "Rask.Site", "wwwroot", "demos", "pdf", "visszaterites.pdf");

    private static string Repo([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));

    [GeneratedRegex(@"SZ-\d{5}-\w{3}")]
    private static partial Regex Invoice();
}
