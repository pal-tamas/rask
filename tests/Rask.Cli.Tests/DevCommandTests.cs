using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

public sealed class DevCommandTests
{
    [Fact]
    public void Default_uses_dotnet_watch_run()
    {
        var args = DevCommand.BuildDotnetArguments(project: null, noHotReload: false, passthrough: []);

        Assert.Equal(["watch", "run"], args);
    }

    [Fact]
    public void Project_is_passed_to_watch()
    {
        var args = DevCommand.BuildDotnetArguments("src/App/App.csproj", noHotReload: false, passthrough: []);

        Assert.Equal(["watch", "--project", "src/App/App.csproj", "run"], args);
    }

    [Fact]
    public void No_hot_reload_uses_plain_run()
    {
        var args = DevCommand.BuildDotnetArguments("App.csproj", noHotReload: true, passthrough: []);

        Assert.Equal(["run", "--project", "App.csproj"], args);
    }

    [Fact]
    public void Passthrough_is_appended_after_separator()
    {
        var args = DevCommand.BuildDotnetArguments(project: null, noHotReload: false, passthrough: ["--urls", "http://localhost:1234"]);

        Assert.Equal(["watch", "run", "--", "--urls", "http://localhost:1234"], args);
    }

    [Fact]
    public async Task Execute_forwards_to_dotnet()
    {
        var console = new StringConsole();
        var runner = new FakeProcessRunner { RunExitCode = 0 };
        var command = new DevCommand(console, runner);

        var exit = await command.ExecuteAsync(["--project", "App.csproj", "--", "--urls", "http://localhost:5005"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal("dotnet", runner.LastRun!.FileName);
        Assert.Equal(["watch", "--project", "App.csproj", "run", "--", "--urls", "http://localhost:5005"], runner.LastRun.Arguments);
    }

    [Fact]
    public void Passthrough_help_is_forwarded_to_the_app_not_swallowed()
    {
        var args = DevCommand.BuildDotnetArguments(project: null, noHotReload: false, passthrough: ["--help"]);

        Assert.Equal(["watch", "run", "--", "--help"], args);
    }

    [Fact]
    public async Task Unknown_option_fails_without_running()
    {
        var console = new StringConsole();
        var runner = new FakeProcessRunner();
        var command = new DevCommand(console, runner);

        var exit = await command.ExecuteAsync(["--bogus"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
    }
}
