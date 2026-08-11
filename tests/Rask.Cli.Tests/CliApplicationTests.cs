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

    [Fact]
    public void A_short_name_means_the_same_option_on_every_command()
    {
        // #601. -o was --output on four commands and a boolean --open on dev, so `rask dev -o ./path`
        // parsed happily and dropped the path into the positionals; -p was --project on three and
        // --plural on generate, so the same keystrokes set a different thing depending on where you
        // were. Both fail silently, which is what makes them worth a test rather than a doc note.
        var app = CliApplication.CreateDefault(new StringConsole(), new FakeProcessRunner(), new FakeFileSystem());

        var claims = new Dictionary<char, (string Long, bool IsFlag, string Command)>();
        foreach (var command in app.Commands)
        {
            foreach (var option in command.OptionSchema?.Declared ?? [])
            {
                if (option.ShortName is not { } shortName)
                {
                    continue;
                }

                if (!claims.TryGetValue(shortName, out var existing))
                {
                    claims[shortName] = (option.LongName, option.IsFlag, command.Name);
                    continue;
                }

                Assert.True(
                    existing.Long == option.LongName,
                    $"-{shortName} is --{existing.Long} on 'rask {existing.Command}' but --{option.LongName} "
                    + $"on 'rask {command.Name}'. A short name has to mean one thing across the CLI; give "
                    + "one of them a different letter, or no short name at all.");

                // Belt and braces: the same long name declared as a flag on one command and a value on
                // another is the -o failure exactly, and would slip past the check above.
                Assert.True(
                    existing.IsFlag == option.IsFlag,
                    $"-{shortName} (--{option.LongName}) takes a value on one of 'rask {existing.Command}' / "
                    + $"'rask {command.Name}' and is a boolean on the other, so the wrong one silently "
                    + "swallows its argument as a positional.");
            }
        }
    }

    [Fact]
    public void Every_project_scoped_command_offers_an_escape_hatch()
    {
        // Every command that resolves a project from the working directory needs a way to say which one,
        // because resolution fails whenever a directory holds more than one .csproj (#601).
        var app = CliApplication.CreateDefault(new StringConsole(), new FakeProcessRunner(), new FakeFileSystem());

        foreach (var name in new[] { "dev", "db", "deploy" })
        {
            var command = app.Commands.Single(c => c.Name == name);
            var project = command.OptionSchema?.Declared.FirstOrDefault(o => o.LongName == "project");

            Assert.True(project is not null, $"'rask {name}' has no --project to fall back on.");
            Assert.Equal('p', project!.ShortName);
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
