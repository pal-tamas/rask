using Rask.Cli.Commands;
using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

public sealed class NewCommandTests
{
    private const string Installed = "These templates matched: rask-server, rask-wasm…";

    [Fact]
    public void BuildDotnetNewArguments_composes_name_output_and_flags()
    {
        TemplateCatalog.TryGet("server", out var server);

        var args = NewCommand.BuildDotnetNewArguments(server, "MyApp", "out/dir", ["auth", "docker"]);

        Assert.Equal(
            ["new", "rask-server", "--name", "MyApp", "--output", "out/dir", "--auth", "--docker"],
            args);
    }

    [Fact]
    public void BuildDotnetNewArguments_omits_output_when_absent()
    {
        TemplateCatalog.TryGet("wasm", out var wasm);

        var args = NewCommand.BuildDotnetNewArguments(wasm, "Spa", output: null, flags: []);

        Assert.Equal(["new", "rask-wasm", "--name", "Spa"], args);
    }

    [Fact]
    public async Task Runs_dotnet_new_with_the_resolved_template_when_templates_installed()
    {
        var (console, runner, command) = Build();
        runner.CaptureResult = new ProcessResult(0, Installed, string.Empty);

        var exit = await command.ExecuteAsync(["MyApp", "--template", "server", "--auth"], CancellationToken.None);

        Assert.Equal(0, exit);
        // No install step: only the create ran.
        Assert.DoesNotContain(runner.Invocations, i => !i.Captured && i.Arguments.Contains("install"));
        Assert.Equal(["new", "rask-server", "--name", "MyApp", "--auth"], runner.LastRun!.Arguments);
        Assert.Empty(console.ErrorText);
    }

    [Fact]
    public async Task Installs_templates_first_when_missing()
    {
        var (_, runner, command) = Build();
        runner.CaptureResult = new ProcessResult(0, "No templates installed.", string.Empty);

        var exit = await command.ExecuteAsync(["MyApp"], CancellationToken.None);

        Assert.Equal(0, exit);
        var runs = runner.Invocations.Where(i => !i.Captured).ToArray();
        Assert.Equal(["new", "install", "Rask.Templates"], runs[0].Arguments);
        Assert.Equal(["new", "rask-server", "--name", "MyApp"], runs[1].Arguments);
    }

    [Fact]
    public async Task Missing_name_fails_without_running_dotnet()
    {
        var (console, runner, command) = Build();

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("name is required", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_template_fails()
    {
        var (console, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--template", "svelte"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("Unknown template 'svelte'", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_flag_for_template_fails_with_guidance()
    {
        var (console, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--template", "wasm", "--cqrs"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("does not support: --cqrs", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_option_fails()
    {
        var (console, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--frobnicate"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("--frobnicate", console.ErrorText, StringComparison.Ordinal);
    }

    private static (StringConsole Console, FakeProcessRunner Runner, NewCommand Command) Build()
    {
        var console = new StringConsole();
        var runner = new FakeProcessRunner();
        return (console, runner, new NewCommand(console, runner));
    }
}
