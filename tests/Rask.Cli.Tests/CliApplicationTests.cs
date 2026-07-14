namespace Rask.Cli.Tests;

public sealed class CliApplicationTests
{
    private static (StringConsole Console, CliApplication App) Build()
    {
        var console = new StringConsole();
        return (console, CliApplication.CreateDefault(console, new FakeProcessRunner(), new FakeFileSystem()));
    }

    [Fact]
    public async Task No_args_prints_usage_and_succeeds()
    {
        var (console, app) = Build();

        var exit = await app.RunAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("Usage: rask <command>", console.OutText, StringComparison.Ordinal);
        Assert.Contains("new", console.OutText, StringComparison.Ordinal);
        Assert.Contains("dev", console.OutText, StringComparison.Ordinal);
        Assert.Contains("info", console.OutText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    [InlineData("version")]
    public async Task Version_prints_the_tool_version(string token)
    {
        var (console, app) = Build();

        var exit = await app.RunAsync([token], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(CliMetadata.Version, console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Help_for_a_command_prints_its_usage()
    {
        var (console, app) = Build();

        var exit = await app.RunAsync(["help", "new"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("rask new <name>", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Command_help_flag_prints_usage_without_executing()
    {
        var console = new StringConsole();
        var runner = new FakeProcessRunner();
        var app = CliApplication.CreateDefault(console, runner, new FakeFileSystem());

        var exit = await app.RunAsync(["new", "--help"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("rask new <name>", console.OutText, StringComparison.Ordinal);
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task Passthrough_help_after_separator_reaches_the_command_not_cli_help()
    {
        var console = new StringConsole();
        var runner = new FakeProcessRunner();
        var app = CliApplication.CreateDefault(console, runner, new FakeFileSystem());

        // 'rask dev -- --help' must launch the app and forward --help, not print rask's help.
        var exit = await app.RunAsync(["dev", "--", "--help"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(["watch", "run", "--", "--help"], runner.LastRun!.Arguments);
        Assert.DoesNotContain("Usage: rask dev", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Command_alias_g_resolves_to_generate()
    {
        var (console, app) = Build();

        // `rask g` with no artifact reaches GenerateCommand, which asks what to generate.
        var exit = await app.RunAsync(["g"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("Specify what to generate", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_command_fails_with_usage()
    {
        var (console, app) = Build();

        var exit = await app.RunAsync(["bogus"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("Unknown command 'bogus'", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatches_to_a_command()
    {
        var (console, app) = Build();

        var exit = await app.RunAsync(["info"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("Rask CLI", console.OutText, StringComparison.Ordinal);
    }
}
