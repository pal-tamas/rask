namespace Rask.Printing.Tests;

/// <summary>A print that runs out of time says what it was waiting for, and a browser start is not a document's time.</summary>
public sealed class StuckPrintTests
{
    [Fact]
    public async Task A_print_that_times_out_names_the_step_and_the_request_left_unanswered()
    {
        // Long enough that a busy machine has opened the tab and asked for the stylesheet before it runs out.
        await using var printer = TestChromium.Printer(timeout: TimeSpan.FromSeconds(15));
        await printer.Print(PrintDocument.Of(PrintedPdf.Html("<p>Bemelegítés</p>")), TestContext.Current.CancellationToken);
        var html = PrintedPdf.Html("<link rel=\"stylesheet\" href=\"/css/never.css\"><p>Vár</p>");

        var late = await Assert.ThrowsAsync<TimeoutException>(() => printer.Print(
            new PrintDocument(html, "/", new PdfOptions(), new NeverAnswers()), TestContext.Current.CancellationToken));

        Assert.Contains("while loading the document (its load event)", late.Message, StringComparison.Ordinal);
        Assert.Contains("still unanswered: https://print.rask.invalid/css/never.css", late.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_browser_that_never_answers_is_reported_as_one_not_as_a_slow_document()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Rask.Pdf drives Chromium over a pipe only on Linux and macOS.");
        var silent = Path.Combine(Path.GetTempPath(), $"rask-silent-chromium-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(silent, "#!/bin/sh\nexec sleep 30\n", TestContext.Current.CancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(silent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var browser = ChromiumProcess.Start(silent, noSandbox: false);

        var silence = await Assert.ThrowsAsync<TimeoutException>(
            () => browser.Ready(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        Assert.Contains("Chromium started but did not answer within 1 seconds", silence.Message, StringComparison.Ordinal);
        await browser.DisposeAsync();
        File.Delete(silent);
    }

    private sealed class NeverAnswers : IPrintAssets
    {
        public async ValueTask<PrintAsset?> Find(string path, CancellationToken cancellation)
        {
            await Task.Delay(Timeout.Infinite, cancellation);
            return null;
        }
    }
}
