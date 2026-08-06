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
        // `rask dev` resolves the project before running, and CreateDefault anchors it at the real
        // current directory — so seed a project there for it to find.
        var fileSystem = new FakeFileSystem();
        var csproj = Path.Combine(Environment.CurrentDirectory, "App.csproj");
        fileSystem.Seed(csproj, """<Project Sdk="Microsoft.NET.Sdk.Web"></Project>""");
        var app = CliApplication.CreateDefault(console, runner, fileSystem);

        // 'rask dev -- --help' must launch the app and forward --help, not print rask's help.
        var exit = await app.RunAsync(["dev", "--", "--help"], CancellationToken.None);

        Assert.Equal(0, exit);
        // --non-interactive because the test console reports stdin as redirected: without a terminal,
        // watch's rude-edit prompt would have nobody to answer it and would block forever.
        Assert.Equal(
            ["watch", "--project", csproj, "--non-interactive", "run", "--", "--help"],
            runner.LastRun!.Arguments);
        Assert.DoesNotContain("Usage: rask dev", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Command_alias_g_resolves_to_generate()
    {
        var (console, app) = Build();

        // `rask g` with no artifact reaches GenerateCommand, which asks what to generate.
        var exit = await app.RunAsync(["g"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Contains("Specify a 'rask generate' action", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_command_fails_with_usage()
    {
        var (console, app) = Build();

        var exit = await app.RunAsync(["bogus"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
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

    [Theory]
    [InlineData("genrate", "generate")]
    [InlineData("deloy", "deploy")]
    [InlineData("nwe", "new")]
    public async Task A_mistyped_command_names_the_one_you_meant(string typed, string meant)
    {
        var (console, app) = Build();

        var exit = await app.RunAsync([typed], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Contains($"Unknown command '{typed}'. Did you mean '{meant}'?", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_mistyped_alias_suggests_the_command_it_stands_for()
    {
        var (console, app) = Build();

        // 'g' is generate's alias; the suggestion has to name the command, not the alias.
        var exit = await app.RunAsync(["gg"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Contains("Did you mean 'generate'?", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void No_command_may_claim_the_short_h()
    {
        // The router resolves -h to help before a command's own parser runs, so a command declaring it
        // (deploy's --host once did) would silently print help instead of running. Keep it reserved.
        var app = CliApplication.CreateDefault(new StringConsole(), new FakeProcessRunner(), new FakeFileSystem());

        foreach (var command in app.Commands)
        {
            var claimed = command.OptionSchema?.Declared.FirstOrDefault(o => o.ShortName == 'h');
            Assert.True(
                claimed is null,
                $"'rask {command.Name}' declares -h for --{claimed?.LongName}, which the router takes as --help.");
        }
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public async Task Either_help_token_shows_a_commands_help(string token)
    {
        var (console, app) = Build();

        var exit = await app.RunAsync(["deploy", token], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("Usage: rask deploy", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Help_after_a_passthrough_separator_belongs_to_the_app()
    {
        var (console, app) = Build();

        // `rask dev -- --help` asks the *app* for help; the CLI must not intercept it.
        var exit = await app.RunAsync(["dev", "--", "--help"], CancellationToken.None);

        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("Usage: rask dev", console.OutText, StringComparison.Ordinal);
    }
}
