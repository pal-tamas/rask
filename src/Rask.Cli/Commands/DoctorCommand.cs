using System.Runtime.InteropServices;
using Rask.Cli.Scaffolding;
using Spectre.Console;

namespace Rask.Cli.Commands;

/// <summary>
///     <c>rask doctor</c> — check the environment before a command hits it.
/// </summary>
/// <remarks>
///     Every probe here already existed, each reachable from exactly the one command that needed it:
///     <c>EfToolProbe</c> only from <c>rask db</c>, <c>DockerProbe</c> only from <c>rask deploy</c>,
///     <c>ProjectLocator</c>/<c>DevTarget</c> from whichever command was about to use them. So the way to
///     find out whether your machine could run a thing was to run it and watch where it stopped — halfway
///     through, having already done some of the work (#599).
///     <para>
///         Read-only by design: it reports, it never installs or fixes. A doctor that quietly installed
///         the tooling it found missing would be doing the thing you ran it to avoid.
///     </para>
/// </remarks>
internal sealed class DoctorCommand(
    IConsole console,
    IFileSystem fileSystem,
    IProcessRunner process,
    string workingDirectory) : CliCommand(console)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IProcessRunner _process = process;
    private readonly string _workingDirectory = workingDirectory;

    public override string Name => "doctor";

    public override string Summary => "Check the environment and this project before a command needs them.";

    public override string Usage => "rask doctor [--json]";

    public override IReadOnlyList<string> Examples => ["rask doctor", "rask doctor --json"];

    public override ArgumentSchema? OptionSchema => CreateSchema();

    private static ArgumentSchema CreateSchema() => new ArgumentSchema().WithJson();

    public override async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var parsed = CreateSchema().Parse(args);
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors);
        }

        if (parsed.Positionals.Count > 0)
        {
            return Fail($"'{parsed.Positionals[0]}' isn't an option of `rask doctor`.");
        }

        var checks = new List<DoctorCheck>();
        checks.AddRange(await EnvironmentChecksAsync(cancellationToken).ConfigureAwait(false));
        checks.AddRange(ProjectChecks());

        // Warnings are not failures. Docker missing is fatal to `rask deploy` and irrelevant to everyone
        // else, so it cannot decide this command's exit code — only a genuinely broken thing does.
        var failed = checks.Count(c => c.Status == DoctorStatus.Fail);

        if (parsed.HasFlag("json"))
        {
            JsonOutput.Write(
                Console,
                new DoctorReport(failed == 0, checks),
                CliJsonContext.Default.DoctorReport);
            return failed == 0 ? 0 : 1;
        }

        // status | name | detail, with each fix on its own row under the detail it belongs to — the grid
        // keeps that hanging indent aligned without the caller counting spaces.
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
        grid.AddColumn(new GridColumn().NoWrap().PadRight(3));
        grid.AddColumn();

        foreach (var check in checks)
        {
            var (mark, style) = check.Status switch
            {
                DoctorStatus.Ok => ("ok", ConsoleStyle.Success),
                DoctorStatus.Warn => ("warn", ConsoleStyle.Warning),
                _ => ("fail", ConsoleStyle.Error),
            };

            grid.AddRow(
                new Text(mark, ConsoleStyling.Of(style)),
                new Text(check.Name, ConsoleStyling.Of(style)),
                new Text(check.Detail, ConsoleStyling.Of(style)));

            if (check.Fix is { Length: > 0 } fix)
            {
                grid.AddRow(Text.Empty, Text.Empty, new Text(fix, ConsoleStyling.Of(ConsoleStyle.Dim)));
            }
        }

        Console.Ansi.Write(new RaggedRight(new Padder(grid, new Padding(2, 0, 0, 0))));

        Console.Out.WriteLine();
        Console.WriteLine(
            failed == 0
                ? "  Nothing here will stop a command from starting."
                : $"  {failed} check(s) would stop a command from starting.",
            failed == 0 ? ConsoleStyle.Success : ConsoleStyle.Error);

        return failed == 0 ? 0 : 1;
    }

    private async Task<IReadOnlyList<DoctorCheck>> EnvironmentChecksAsync(CancellationToken cancellationToken)
    {
        var sdk = await CaptureAsync(["--version"], cancellationToken).ConfigureAwait(false);
        var checks = new List<DoctorCheck>
        {
            new("rask", DoctorStatus.Ok, CliMetadata.Version, null),
            new("os", DoctorStatus.Ok, RuntimeInformation.OSDescription, null),
            sdk is null
                // The one environment check that IS fatal to everything: every command shells out to it.
                ? new DoctorCheck("dotnet sdk", DoctorStatus.Fail, "not found", "Install .NET from https://dot.net.")
                : new DoctorCheck("dotnet sdk", DoctorStatus.Ok, sdk, null),
        };

        var efInstalled = await EfToolProbe.IsInstalledAsync(_process, cancellationToken).ConfigureAwait(false);
        checks.Add(efInstalled
            ? new DoctorCheck("dotnet-ef", DoctorStatus.Ok, "installed", null)
            // A warning, not a failure: `rask db` installs it on first use. Worth saying because that
            // install is the surprise pause in an otherwise instant command.
            : new DoctorCheck(
                "dotnet-ef", DoctorStatus.Warn, "not installed",
                "`rask db` will install it on first use, or: dotnet tool install -g dotnet-ef"));

        var docker = await CaptureAsync(["--version"], cancellationToken, "docker").ConfigureAwait(false);
        checks.Add(docker is null
            ? new DoctorCheck(
                "docker", DoctorStatus.Warn, "not found",
                "Only `rask deploy` needs it — https://docs.docker.com/get-docker/")
            : new DoctorCheck("docker", DoctorStatus.Ok, docker, null));

        return checks;
    }

    private IReadOnlyList<DoctorCheck> ProjectChecks()
    {
        var checks = new List<DoctorCheck>();
        var project = ProjectLocator.Locate(_fileSystem, _workingDirectory);

        if (project is null)
        {
            // Not a failure. `rask new` is meant to be run outside a project, and so is `rask doctor`
            // itself when you are checking a fresh machine.
            checks.Add(new DoctorCheck(
                "project", DoctorStatus.Warn,
                ProjectLocator.DescribeMissing(_fileSystem, _workingDirectory),
                "Run this inside a project to check it too."));
            return checks;
        }

        checks.Add(new DoctorCheck("project", DoctorStatus.Ok, project.ProjectDirectory, null));
        checks.Add(new DoctorCheck("database", DoctorStatus.Ok, project.Database.ShortName, null));

        var target = DevTarget.Detect(_fileSystem, _workingDirectory, null);
        checks.Add(target is null
            ? new DoctorCheck(
                "rask dev", DoctorStatus.Warn, "no runnable project found",
                "Pass --project, or run from the project directory.")
            : new DoctorCheck("rask dev", DoctorStatus.Ok, $"{target.Name} ({target.Kind})", null));

        // The version this CLI would pin into a new project, so a mismatch with what the project already
        // references is visible before a package is added that disagrees with the rest.
        checks.Add(new DoctorCheck(
            "rask packages", DoctorStatus.Ok, NewCommand.ResolvePackageVersion(CliMetadata.Version), null));

        // The bug this command exists to make visible: the config loader catches JsonException and returns
        // defaults, so a typo'd file looked exactly like no file and the remembered host vanished with
        // nothing said anywhere. (`.rask/generate.json` went with the feature scaffolder that wrote it.)
        if (DeployConfig.DescribeProblem(_fileSystem, _workingDirectory) is { } deployProblem)
        {
            checks.Add(new DoctorCheck(
                ".rask/deploy.json", DoctorStatus.Fail, deployProblem,
                "Until it parses, its remembered settings are silently ignored."));
        }

        return checks;
    }

    private async Task<string?> CaptureAsync(
        IReadOnlyList<string> arguments, CancellationToken cancellationToken, string executable = "dotnet")
    {
        try
        {
            var result = await _process.CaptureAsync(executable, arguments, null, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return null;
            }

            var trimmed = result.StandardOutput.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A missing executable throws rather than exiting non-zero, and "docker isn't installed" is
            // one of the answers this command exists to give — not a reason for it to fall over.
            return null;
        }
    }
}
