using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     <c>rask doctor</c> (#599): the probes the CLI already owned, reachable before a command needs them.
/// </summary>
public class DoctorCommandTests
{
    private const string ProjectDir = "/proj";

    /// <param name="sdk">
    ///     What <c>dotnet --version</c> reports. Empty means "not found", which is a genuine failure —
    ///     every command shells out to the SDK — so the default here is a realistic version rather than
    ///     the fake's empty one, or every test would be asserting against a broken machine.
    /// </param>
    private static (StringConsole Console, FakeFileSystem Fs, DoctorCommand Command) Build(string sdk = "10.0.302")
    {
        var console = new StringConsole();
        var fs = new FakeFileSystem();
        fs.Seed($"{ProjectDir}/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        var process = new FakeProcessRunner { CaptureResult = new ProcessResult(0, sdk, string.Empty) };
        return (console, fs, new DoctorCommand(console, fs, process, ProjectDir));
    }

    [Fact]
    public async Task A_missing_dotnet_sdk_is_a_failure()
    {
        // The one environment check that is fatal to everything, since every command shells out to it.
        var (console, _, command) = Build(sdk: string.Empty);

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("not found", console.OutText, StringComparison.Ordinal);
        Assert.Contains("https://dot.net", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_corrupt_deploy_config_is_a_failure_rather_than_a_shrug()
    {
        // The concrete bug this command exists to surface. DeployConfig.Load catches JsonException and
        // returns defaults, so a typo'd file looked exactly like no file at all — the remembered host
        // silently vanished and nothing anywhere said so.
        var (console, fs, command) = Build();
        fs.Seed($"{ProjectDir}/.rask/deploy.json", "{ \"host\": \"box\", oops }");

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("deploy.json", console.OutText, StringComparison.Ordinal);
        Assert.Contains("isn't valid JSON", console.OutText, StringComparison.Ordinal);
        Assert.Contains("silently ignored", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_corrupt_generate_config_is_reported_too()
    {
        var (console, fs, command) = Build();
        fs.Seed($"{ProjectDir}/.rask/generate.json", "not json at all");

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("generate.json", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_healthy_project_passes()
    {
        var (console, fs, command) = Build();
        fs.Seed($"{ProjectDir}/.rask/deploy.json", "{ \"host\": \"box\" }");

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("Nothing here will stop a command", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_optional_tooling_warns_without_failing()
    {
        // Docker is fatal to `rask deploy` and irrelevant to everyone else, so it must not decide this
        // command's exit code. A doctor that failed on every machine without Docker would be ignored.
        var (console, _, command) = Build();

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("would stop a command", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_reports_the_same_verdict()
    {
        var (console, fs, command) = Build();
        fs.Seed($"{ProjectDir}/.rask/deploy.json", "{ oops }");

        var exit = await command.ExecuteAsync(["--json"], CancellationToken.None);

        Assert.Equal(1, exit);
        using var parsed = System.Text.Json.JsonDocument.Parse(console.OutText);
        Assert.False(parsed.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains(
            parsed.RootElement.GetProperty("checks").EnumerateArray(),
            c => c.GetProperty("status").GetString() == "Fail");
    }

    [Fact]
    public async Task It_rejects_arguments_it_does_not_understand()
    {
        var (_, _, command) = Build();

        Assert.Equal(CliCommand.UsageExitCode, await command.ExecuteAsync(["--nope"], CancellationToken.None));
        Assert.Equal(CliCommand.UsageExitCode, await command.ExecuteAsync(["extra"], CancellationToken.None));
    }

    [Fact]
    public void The_config_loaders_report_a_problem_instead_of_swallowing_it()
    {
        // Load() still falls back to defaults — a corrupt file must not wedge a deploy — but it no longer
        // does so in silence, and DescribeProblem is what doctor reads.
        var fs = new FakeFileSystem();
        fs.Seed($"{ProjectDir}/.rask/deploy.json", "{ oops }");
        var console = new StringConsole();

        var config = DeployConfig.Load(fs, ProjectDir, console);

        Assert.Null(config.Host);                                     // still falls back
        Assert.Contains("Ignoring", console.ErrorText, StringComparison.Ordinal);   // but says so
        Assert.NotNull(DeployConfig.DescribeProblem(fs, ProjectDir));
    }

    [Fact]
    public void A_readable_config_has_no_problem_to_describe()
    {
        var fs = new FakeFileSystem();
        fs.Seed($"{ProjectDir}/.rask/deploy.json", "{ \"host\": \"box\" }");

        Assert.Null(DeployConfig.DescribeProblem(fs, ProjectDir));
        Assert.Null(DeployConfig.DescribeProblem(new FakeFileSystem(), ProjectDir));  // absent is fine
    }
}
