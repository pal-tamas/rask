namespace Rask.Cli.Tests;

public sealed class CompletionCommandTests
{
    private static (StringConsole Console, CliApplication App) Build()
    {
        var console = new StringConsole();
        return (console, CliApplication.CreateDefault(console, new FakeProcessRunner(), new FakeFileSystem()));
    }

    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    public async Task Emits_a_script_that_mentions_commands_and_options(string shell)
    {
        var (console, app) = Build();

        var exit = await app.RunAsync(["completion", shell], CancellationToken.None);

        Assert.Equal(0, exit);
        var script = console.OutText;
        Assert.Contains("deploy", script, StringComparison.Ordinal); // a command name
        // Option names appear as `--template` (bash/zsh) or `-l template` (fish) — match the bare name.
        Assert.Contains("template", script, StringComparison.Ordinal); // an option from `new`'s schema
        Assert.Contains("outbox", script, StringComparison.Ordinal);   // a feature-only option
    }

    [Fact]
    public async Task Missing_shell_is_an_error()
    {
        var (console, app) = Build();

        var exit = await app.RunAsync(["completion"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Contains("Specify a 'rask completion' action: bash, zsh, fish.", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("Run 'rask completion --help' for details.", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_shell_is_an_error()
    {
        var (console, app) = Build();

        var exit = await app.RunAsync(["completion", "powershell"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Contains("Unknown 'rask completion' action 'powershell'.", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("Choose one of: bash, zsh, fish.", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Completion_appears_in_the_top_level_command_list()
    {
        var (console, app) = Build();

        await app.RunAsync([], CancellationToken.None);

        Assert.Contains("completion", console.OutText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    public async Task Subcommands_are_completable_too(string shell)
    {
        var (console, app) = Build();

        await app.RunAsync(["completion", shell], CancellationToken.None);

        var script = console.OutText;
        foreach (var verb in new[] { "backup", "restore", "rollback", "cache" })
        {
            Assert.Contains(verb, script, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    public async Task An_options_closed_set_completes_its_values(string shell)
    {
        var (console, app) = Build();

        await app.RunAsync(["completion", shell], CancellationToken.None);

        // The values of `new --template`, which only the schema knows.
        Assert.Contains("wasm-hosted", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bash_completes_values_per_command_not_globally()
    {
        var (console, app) = Build();

        await app.RunAsync(["completion", "bash"], CancellationToken.None);

        // `--template` names a closed set under `new`; the value completion has to sit inside the
        // command's own branch or every other command would offer template names too.
        var script = console.OutText;
        var newBranch = script.IndexOf("    new)", StringComparison.Ordinal);
        var templateChoices = script.IndexOf("\"$prev\" = \"--template\"", StringComparison.Ordinal);
        var deployBranch = script.IndexOf("    deploy)", StringComparison.Ordinal);

        Assert.True(
            newBranch >= 0 && templateChoices > newBranch,
            "the --template value list belongs to `new`'s branch");
        Assert.True(deployBranch > templateChoices, "and must not leak into `deploy`'s");
    }
}
