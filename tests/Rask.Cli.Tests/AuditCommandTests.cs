using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

// `rask audit` has the app audit itself — `dotnet run -- rask audit` — and passes its exit code on.
public sealed class AuditCommandTests
{
    private const string ProjectDir = "/proj";

    [Fact]
    public void The_app_is_run_with_the_audit_command_and_each_named_aggregate()
    {
        var args = AuditCommand.BuildArguments(ProjectDir, ["Driver", "Vehicle"], json: true, uniqueCandidates: true);

        Assert.Equal(
            ["run", "--project", ProjectDir, "--verbosity", "quiet", "--", "rask", "audit", "--aggregate", "Driver", "--aggregate", "Vehicle", "--unique-candidates", "--json"],
            args);
    }

    [Fact]
    public async Task The_audits_exit_code_is_the_commands_so_a_release_step_stops_on_a_broken_rule()
    {
        var (command, runner, _) = CreateWithProject();
        runner.RunExitCode = 1;

        var exit = await command.ExecuteAsync(["--aggregate", "Driver"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Equal(["run", "--project", ProjectDir, "--verbosity", "quiet", "--", "rask", "audit", "--aggregate", "Driver"], runner.LastRun!.Arguments);
    }

    [Fact]
    public async Task A_dry_run_prints_what_would_run_and_reads_nothing()
    {
        var (command, runner, console) = CreateWithProject();

        var exit = await command.ExecuteAsync(["--dry-run"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("dotnet run --project /proj --verbosity quiet -- rask audit", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bare_word_is_refused_with_the_option_that_names_an_aggregate()
    {
        var (command, runner, console) = CreateWithProject();

        var exit = await command.ExecuteAsync(["Driver"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("--aggregate Driver", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Outside_a_project_it_says_so_and_runs_nothing()
    {
        var console = new StringConsole();
        var runner = new FakeProcessRunner();
        var command = new AuditCommand(console, new FakeFileSystem(), runner, "/nowhere");

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
    }

    private static (AuditCommand Command, FakeProcessRunner Runner, StringConsole Console) CreateWithProject()
    {
        var console = new StringConsole();
        var runner = new FakeProcessRunner();
        var fileSystem = new FakeFileSystem();
        fileSystem.Seed("/proj/App.csproj", "<Project></Project>");
        return (new AuditCommand(console, fileSystem, runner, ProjectDir), runner, console);
    }
}
