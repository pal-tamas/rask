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
        Assert.Contains("generate", script, StringComparison.Ordinal); // a command name
        // Option names appear as `--template` (bash/zsh) or `-l template` (fish) — match the bare name.
        Assert.Contains("template", script, StringComparison.Ordinal); // an option from `new`'s schema
        Assert.Contains("outbox", script, StringComparison.Ordinal);   // a feature-only option
    }

    [Fact]
    public async Task Missing_shell_is_an_error()
    {
        var (console, app) = Build();

        var exit = await app.RunAsync(["completion"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("Specify a shell", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_shell_is_an_error()
    {
        var (console, app) = Build();

        var exit = await app.RunAsync(["completion", "powershell"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("Specify a shell", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Completion_appears_in_the_top_level_command_list()
    {
        var (console, app) = Build();

        await app.RunAsync([], CancellationToken.None);

        Assert.Contains("completion", console.OutText, StringComparison.Ordinal);
    }
}
