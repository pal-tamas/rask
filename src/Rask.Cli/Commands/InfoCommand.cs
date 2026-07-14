using System.Runtime.InteropServices;
using Rask.Cli.Templates;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask info</c> — a quick environment report: the CLI version, the .NET SDK version, whether the
/// Rask templates are installed, and the OS. Useful first thing when diagnosing a machine.
/// </summary>
internal sealed class InfoCommand(IConsole console, IProcessRunner process) : CliCommand(console)
{
    private readonly IProcessRunner _process = process;

    public override string Name => "info";

    public override string Summary => "Show Rask CLI, .NET SDK, and template environment information.";

    public override string Usage => "rask info";

    public override async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var sdkVersion = await CaptureSingleLineAsync(["--version"], cancellationToken).ConfigureAwait(false);
        var templatesInstalled = await TemplateProbe.AreInstalledAsync(_process, cancellationToken).ConfigureAwait(false);

        Console.Out.WriteLine(FormatReport(CliMetadata.Version, sdkVersion, templatesInstalled, RuntimeInformation.OSDescription));
        return 0;
    }

    /// <summary>Render the report. Pure, so tests assert on the exact lines without spawning a process.</summary>
    internal static string FormatReport(string cliVersion, string? sdkVersion, bool templatesInstalled, string osDescription)
    {
        var templates = templatesInstalled
            ? "installed"
            : "not installed (run: dotnet new install Rask.Templates)";

        var lines = new[]
        {
            ("Rask CLI", cliVersion),
            (".NET SDK", string.IsNullOrWhiteSpace(sdkVersion) ? "not found" : sdkVersion),
            ("Rask templates", templates),
            ("OS", osDescription),
        };

        var width = lines.Max(line => line.Item1.Length);
        return string.Join(Environment.NewLine, lines.Select(line => $"  {line.Item1.PadRight(width)}   {line.Item2}"));
    }

    private async Task<string?> CaptureSingleLineAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await _process.CaptureAsync("dotnet", arguments, null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return null;
        }

        var trimmed = result.StandardOutput.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
