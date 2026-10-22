using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Printing.Tests.Host;

/// <summary>
/// A measurement, not a check: how long a long table takes to print, per look and length, and where the time goes.
/// Opt-in — RASK_PDF_MEASURE=1 — so it never runs in CI. See docs/pdf.md, "Long documents", for the command.
/// </summary>
public sealed class PrintMeasurements
{
    private static readonly int[] Lengths = [1000, 5000, 10000];

    [Fact]
    public async Task Long_tables_print_and_report_where_their_time_went()
    {
        Assert.SkipUnless(
            string.Equals(Environment.GetEnvironmentVariable("RASK_PDF_MEASURE"), "1", StringComparison.Ordinal),
            "A measurement: set RASK_PDF_MEASURE=1 to run it.");
        Assert.SkipWhen(TestChromium.Installed is null, "No Chromium is installed here, so nothing printed.");
        await using var host = await PrintHost.Start(SiteDemoPdfTests.Fonts(), timeout: TimeSpan.FromMinutes(10));
        var printer = host.Services.GetRequiredService<ChromiumPrinter>();
        var report = new StringBuilder(
            "| rows | look | HTML | pages | PDF | total | render | to Chrome + load | draw, fonts, cut | printToPDF |\n"
            + "|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|\n");
        await host.As(PrintHost.Anonymous, () => Pdf.From(PrintTestApp.ThreePages()));

        foreach (var rows in Lengths)
        {
            foreach (var look in Enum.GetValues<LongTableLook>())
            {
                report.AppendLine(await Measure(host, printer, rows, look));
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
        if (Environment.GetEnvironmentVariable("RASK_PDF_MEASURE_OUT") is { Length: > 0 } file)
        {
            await File.WriteAllTextAsync(file, report.ToString(), TestContext.Current.CancellationToken);
        }
    }

    private static async Task<string> Measure(PrintHost host, ChromiumPrinter printer, int rows, LongTableLook look)
    {
        (int Bytes, PrintProgress Progress)? printed = null;
        printer.Printed = (document, progress) => printed = (Encoding.UTF8.GetByteCount(document.Html), progress);
        var clock = Stopwatch.StartNew();

        byte[] pdf;
        try
        {
            pdf = await host.As(PrintHost.Anonymous, () => Pdf.From(LongTables.Of(rows, look)));
        }
        catch (Exception e) when (e is InvalidOperationException or TimeoutException)
        {
            return string.Create(CultureInfo.InvariantCulture, $"| {rows:N0} | {look} | failed after {Ms(clock.Elapsed)}: {e.Message} |");
        }
        finally
        {
            printer.Printed = null;
        }

        var total = clock.Elapsed;
        var (bytes, progress) = printed!.Value;
        var printing = progress.Elapsed;
        var steps = progress.Steps.ToDictionary(step => step.Step, step => step.At, StringComparer.Ordinal);
        var load = steps["loading the document (its load event)"];
        var draw = steps["drawing it and waiting for its web fonts"];
        var print = steps["printing it"];
        return string.Create(
            CultureInfo.InvariantCulture,
            $"| {rows:N0} | {look} | {bytes / 1e6:0.0} MB | {PrintedPdf.Pages(pdf).Count} | {pdf.Length / 1e6:0.00} MB | {Ms(total)} "
            + $"| {Ms(total - printing)} | {Ms(draw - load)} | {Ms(print - draw)} | {Ms(printing - print)} |");
    }

    private static string Ms(TimeSpan time) => string.Create(CultureInfo.InvariantCulture, $"{time.TotalMilliseconds:N0} ms");
}
