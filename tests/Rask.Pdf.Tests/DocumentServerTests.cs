using System.Collections.Concurrent;
using System.Text;

namespace Rask.Printing.Tests;

public sealed class DocumentServerTests(SharedPrinter shared) : IClassFixture<SharedPrinter>
{
    // A font the default sans-serif is not: if it is in the PDF, it came through the app's own files.
    private static readonly (string File, string Name)[] Fonts =
    [
        ("/System/Library/Fonts/Supplemental/Courier New.ttf", "CourierNew"),
        ("/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf", "DejaVuSansMono"),
        ("/usr/share/fonts/truetype/liberation/LiberationMono-Regular.ttf", "LiberationMono"),
    ];

    [Fact]
    public async Task The_stylesheet_and_web_font_come_from_the_apps_own_files()
    {
        var (file, name) = InstalledMonospace();
        Assert.SkipWhen(file is null, "No monospaced TrueType font is installed here to serve as the web font.");
        var assets = new Assets(new()
        {
            ["/css/app.css"] = new(Encoding.UTF8.GetBytes(
                "@font-face { font-family: Riport; src: url(/fonts/riport.ttf); } p { font-family: Riport; }"), "text/css"),
            ["/fonts/riport.ttf"] = new(await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken), "font/ttf"),
        });
        var html = "<!doctype html><html><head><meta charset=\"utf-8\"><link rel=\"stylesheet\" href=\"../css/app.css\"></head>"
            + "<body><p>Betűtípus</p></body></html>";

        var pdf = await shared.Printer.Print(new PrintDocument(html, "/reports/7", new PdfOptions { PageNumbers = false }, assets), TestContext.Current.CancellationToken);

        Assert.Contains(PrintedPdf.Fonts(pdf), font => font.Contains(name, StringComparison.Ordinal));
        Assert.Contains("/css/app.css", assets.Asked);
        Assert.Contains("/fonts/riport.ttf", assets.Asked);
    }

    [Fact]
    public async Task A_file_the_app_does_not_have_is_not_found_and_the_page_still_prints()
    {
        var assets = new Assets([]);
        var html = PrintedPdf.Html("<link rel=\"stylesheet\" href=\"/missing.css\"><p>Hiányzó stíluslap</p>");

        var pdf = await shared.Printer.Print(new PrintDocument(html, "/", new PdfOptions(), assets), TestContext.Current.CancellationToken);

        Assert.Contains("Hiányzó stíluslap", Assert.Single(PrintedPdf.Pages(pdf)).Text, StringComparison.Ordinal);
        Assert.Contains("/missing.css", assets.Asked);
    }

    [Fact]
    public async Task Another_hosts_files_are_never_asked_of_the_app()
    {
        var assets = new Assets([]);
        var html = PrintedPdf.Html("<link rel=\"stylesheet\" href=\"https://cdn.example.com/app.css\"><p>Idegen</p>");

        await shared.Printer.Print(new PrintDocument(html, "/", new PdfOptions(), assets), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(assets.Asked, path => path.Contains("app.css", StringComparison.Ordinal));
    }

    /// <summary>A monospaced TrueType font installed here and the name it is embedded under, or nulls.</summary>
    internal static (string? File, string Name) InstalledMonospace() =>
        Fonts.FirstOrDefault(font => File.Exists(font.File));

    private sealed class Assets(Dictionary<string, PrintAsset> files) : IPrintAssets
    {
        private readonly ConcurrentQueue<string> _asked = new();

        public IReadOnlyCollection<string> Asked => _asked;

        public ValueTask<PrintAsset?> Find(string path, CancellationToken cancellation)
        {
            _asked.Enqueue(path);
            return ValueTask.FromResult(files.GetValueOrDefault(path));
        }
    }
}
