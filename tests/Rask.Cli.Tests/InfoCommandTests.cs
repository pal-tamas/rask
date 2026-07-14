using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

public sealed class InfoCommandTests
{
    [Fact]
    public void FormatReport_lists_each_field()
    {
        var report = InfoCommand.FormatReport("1.2.3", "10.0.201", templatesInstalled: true, "macOS 26.5");

        Assert.Contains("Rask CLI", report, StringComparison.Ordinal);
        Assert.Contains("1.2.3", report, StringComparison.Ordinal);
        Assert.Contains(".NET SDK", report, StringComparison.Ordinal);
        Assert.Contains("10.0.201", report, StringComparison.Ordinal);
        Assert.Contains("installed", report, StringComparison.Ordinal);
        Assert.Contains("macOS 26.5", report, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatReport_reports_missing_sdk()
    {
        var report = InfoCommand.FormatReport("1.0.0", sdkVersion: null, templatesInstalled: false, "Linux");

        Assert.Contains("not found", report, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatReport_guides_when_templates_missing()
    {
        var report = InfoCommand.FormatReport("1.0.0", "10.0.201", templatesInstalled: false, "Linux");

        Assert.Contains("dotnet new install Rask.Templates", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Execute_probes_dotnet_and_prints_report()
    {
        var console = new StringConsole();
        var runner = new FakeProcessRunner
        {
            CaptureResult = new ProcessResult(0, "10.0.201", string.Empty),
        };
        var command = new InfoCommand(console, runner);

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("Rask CLI", console.OutText, StringComparison.Ordinal);
        Assert.Contains("dotnet", runner.Invocations[0].FileName, StringComparison.Ordinal);
    }
}
