using System.Diagnostics;

namespace Rask.Printing.Tests;

public sealed class ChromiumLifecycleTests
{
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task A_crashed_browser_is_replaced_for_the_next_print()
    {
        await using var printer = TestChromium.Printer();
        await printer.Print(PrintDocument.Of(PrintedPdf.Html("<p>Első</p>")), TestContext.Current.CancellationToken);
        var crashed = printer.Browser!;
        crashed.Kill();
        await Until(() => crashed.IsGone);

        var pdf = await printer.Print(PrintDocument.Of(PrintedPdf.Html("<p>Második</p>")), TestContext.Current.CancellationToken);

        Assert.Contains("Második", Assert.Single(PrintedPdf.Pages(pdf)).Text, StringComparison.Ordinal);
        Assert.NotSame(crashed, printer.Browser);
        Assert.False(Directory.Exists(crashed.Profile));
    }

    [Fact]
    public async Task Concurrent_prints_never_run_more_at_once_than_the_bound()
    {
        await using var printer = TestChromium.Printer(maxConcurrentPrints: 2);
        var html = PrintedPdf.Html(HtmlPrintTests.Table(rows: 300));

        var pdfs = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => printer.Print(PrintDocument.Of(html), TestContext.Current.CancellationToken)));

        Assert.All(pdfs, pdf => Assert.True(PrintedPdf.Pages(pdf).Count > 1));
        Assert.InRange(printer.MostAtOnce, 1, 2);
    }

    [Fact]
    public async Task A_document_that_outlasts_the_timeout_fails_naming_the_setting()
    {
        await using var printer = TestChromium.Printer(timeout: TimeSpan.FromMilliseconds(1));

        var late = await Assert.ThrowsAsync<TimeoutException>(
            () => printer.Print(PrintDocument.Of(PrintedPdf.Html("<p>Késő</p>")), TestContext.Current.CancellationToken));

        Assert.Contains("Rask:Pdf:Timeout", late.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disposing_the_printer_ends_its_browser_and_deletes_its_profile()
    {
        var printer = TestChromium.Printer();
        await printer.Print(PrintDocument.Of(PrintedPdf.Html("<p>Vége</p>")), TestContext.Current.CancellationToken);
        var browser = printer.Browser!;

        await printer.DisposeAsync();

        Assert.True(browser.IsGone);
        Assert.False(Directory.Exists(browser.Profile));
    }

    [Fact]
    public async Task A_browser_whose_pipe_closes_ends_by_itself()
    {
        var browser = ChromiumProcess.Start(TestChromium.Path, TestChromium.NoSandbox);
        await browser.Connection.Send("Browser.getVersion", cancellation: TestContext.Current.CancellationToken);

        await browser.Connection.DisposeAsync();

        await Until(() => browser.HasExited);
        await browser.DisposeAsync();
    }

    private static async Task Until(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < ExitWait, $"Still waiting after {ExitWait.TotalSeconds} seconds.");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}
