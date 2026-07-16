namespace Rask.Cli.Tests;

public sealed class CommandHelpTests
{
    private static (StringConsole Console, CliApplication App) Build()
    {
        var console = new StringConsole();
        return (console, CliApplication.CreateDefault(console, new FakeProcessRunner(), new FakeFileSystem()));
    }

    [Fact]
    public async Task Command_help_lists_options_with_descriptions()
    {
        var (console, app) = Build();

        await app.RunAsync(["new", "--help"], CancellationToken.None);

        var text = console.OutText;
        Assert.Contains("Options:", text, StringComparison.Ordinal);
        Assert.Contains("-t, --template <name>", text, StringComparison.Ordinal);
        Assert.Contains("Template to scaffold", text, StringComparison.Ordinal);
        Assert.Contains("--auth", text, StringComparison.Ordinal);
        Assert.Contains("Add cookie authentication", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Command_help_shows_arguments_and_examples()
    {
        var (console, app) = Build();

        await app.RunAsync(["new", "--help"], CancellationToken.None);

        var text = console.OutText;
        Assert.Contains("Arguments:", text, StringComparison.Ordinal);
        Assert.Contains("Examples:", text, StringComparison.Ordinal);
        Assert.Contains("rask new Shop", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generate_help_surfaces_the_feature_only_flags()
    {
        var (console, app) = Build();

        // These flags were previously undiscoverable — not in usage, not in help.
        await app.RunAsync(["generate", "--help"], CancellationToken.None);

        var text = console.OutText;
        Assert.Contains("Feature options (rask generate feature)", text, StringComparison.Ordinal);
        foreach (var flag in new[] { "--bs", "--modal", "--soft-delete", "--concurrency", "--events", "--outbox", "--tests", "--no-restore" })
        {
            Assert.Contains(flag, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Help_output_is_uncolored_when_stdout_is_redirected()
    {
        var (console, app) = Build();

        await app.RunAsync(["generate", "--help"], CancellationToken.None);

        Assert.DoesNotContain('\x1b', console.OutText);
    }

    [Fact]
    public async Task Top_level_usage_lists_every_command()
    {
        var (console, app) = Build();

        await app.RunAsync([], CancellationToken.None);

        var text = console.OutText;
        foreach (var name in new[] { "new", "dev", "generate", "db", "deploy", "info" })
        {
            Assert.Contains(name, text, StringComparison.Ordinal);
        }

        Assert.Contains("--version", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parse_error_points_at_command_help()
    {
        var (console, app) = Build();

        var exit = await app.RunAsync(["new", "--nope"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("Run 'rask new --help' for details.", console.ErrorText, StringComparison.Ordinal);
    }
}
