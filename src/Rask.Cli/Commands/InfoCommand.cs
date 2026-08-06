using System.Runtime.InteropServices;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask info</c> — a quick environment report: the CLI version, the .NET SDK version, and the OS.
/// Useful first thing when diagnosing a machine.
/// </summary>
internal sealed class InfoCommand(IConsole console, IProcessRunner process) : CliCommand(console)
{
    private readonly IProcessRunner _process = process;

    public override string Name => "info";

    public override string Summary => "Show Rask CLI, .NET SDK, and OS environment information.";

    public override string Usage => "rask info [--json]";

    public override IReadOnlyList<string> Examples => ["rask info", "rask info --json"];

    public override ArgumentSchema? OptionSchema => CreateSchema();

    private static ArgumentSchema CreateSchema() => new ArgumentSchema().WithJson();

    public override async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        // This once ignored its arguments entirely, so `rask info --json` "succeeded" while printing the
        // plain report — the comment that used to sit here is now the feature.
        var parsed = CreateSchema().Parse(args);
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors);
        }

        if (parsed.Positionals.Count > 0)
        {
            return Fail($"'{parsed.Positionals[0]}' isn't an option of `rask info`.");
        }

        var sdkVersion = await CaptureSingleLineAsync(["--version"], cancellationToken).ConfigureAwait(false);

        if (parsed.HasFlag("json"))
        {
            JsonOutput.Write(
                Console,
                new InfoReport(CliMetadata.Version, sdkVersion, RuntimeInformation.OSDescription),
                CliJsonContext.Default.InfoReport);
            return 0;
        }

        Console.Out.WriteLine(FormatReport(CliMetadata.Version, sdkVersion, RuntimeInformation.OSDescription));
        return 0;
    }

    /// <summary>Render the report. Pure, so tests assert on the exact lines without spawning a process.</summary>
    internal static string FormatReport(string cliVersion, string? sdkVersion, string osDescription)
    {
        var lines = new[]
        {
            ("Rask CLI", cliVersion),
            (".NET SDK", string.IsNullOrWhiteSpace(sdkVersion) ? "not found" : sdkVersion),
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
