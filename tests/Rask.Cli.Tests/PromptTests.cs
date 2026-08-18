namespace Rask.Cli.Tests;

public sealed class PromptTests
{
    [Fact]
    public void Interactive_reflects_stdin_redirection()
    {
        var console = new StringConsole();
        Assert.False(new Prompt(console).Interactive); // default: redirected

        console.InputLines = ["x"]; // flips to a terminal
        Assert.True(new Prompt(console).Interactive);
    }

    [Fact]
    public void Ask_returns_the_typed_answer()
    {
        var console = new StringConsole { InputLines = ["Shop"] };

        Assert.Equal("Shop", new Prompt(console).Ask("Project name"));
    }

    [Fact]
    public void Ask_accepts_the_default_on_an_empty_answer()
    {
        var console = new StringConsole { InputLines = [""] };

        Assert.Equal("server", new Prompt(console).Ask("Template", "server"));
    }

    [Fact]
    public void Ask_reasks_until_non_empty_when_required()
    {
        var console = new StringConsole { InputLines = ["", "  ", "Shop"] };

        Assert.Equal("Shop", new Prompt(console).Ask("Project name"));
    }

    [Fact]
    public void Ask_returns_empty_on_end_of_input_rather_than_looping()
    {
        var console = new StringConsole { InputLines = [] };

        Assert.Equal(string.Empty, new Prompt(console).Ask("Project name"));
    }

    [Theory]
    [InlineData("y", true)]
    [InlineData("yes", true)]
    [InlineData("n", false)]
    [InlineData("no", false)]
    public void Confirm_parses_yes_and_no(string answer, bool expected)
    {
        var console = new StringConsole { InputLines = [answer] };

        Assert.Equal(expected, new Prompt(console).Confirm("Add auth?", @default: false));
    }

    [Fact]
    public void Confirm_uses_the_default_on_empty_or_eof()
    {
        Assert.True(new Prompt(new StringConsole { InputLines = [""] }).Confirm("Add auth?", @default: true));
        Assert.False(new Prompt(new StringConsole { InputLines = [] }).Confirm("Add auth?", @default: false));
    }

    [Fact]
    public void Select_moves_through_the_options_with_the_arrow_keys()
    {
        var console = new StringConsole { InputKeys = [ConsoleKey.DownArrow, ConsoleKey.Enter] };
        var options = new[] { ("server", "Server"), ("wasm", "WASM") };

        Assert.Equal("wasm", new Prompt(console).Select("Template", options, "server"));
    }

    [Fact]
    public void Select_takes_the_default_when_enter_is_pressed_straight_away()
    {
        var console = new StringConsole { InputKeys = [ConsoleKey.Enter] };
        var options = new[] { ("server", "Server"), ("wasm", "WASM") };

        Assert.Equal("server", new Prompt(console).Select("Template", options, "server"));
    }

    [Fact]
    public void Select_starts_on_the_default_even_when_it_is_not_listed_first()
    {
        // Pressing enter has to mean the same thing as omitting the flag, whatever order the catalog is in.
        var console = new StringConsole { InputKeys = [ConsoleKey.Enter] };
        var options = new[] { ("wasm", "WebAssembly"), ("server", "Server"), ("native", "Native") };

        Assert.Equal("server", new Prompt(console).Select("Template", options, "server"));
    }

    [Fact]
    public void Select_falls_back_to_the_default_when_the_input_ends()
    {
        // A list prompt that runs out of keys must yield the default rather than throw or spin.
        var console = new StringConsole { InputKeys = [] };
        var options = new[] { ("server", "Server"), ("wasm", "WASM") };

        Assert.Equal("server", new Prompt(console).Select("Template", options, "server"));
    }

    [Fact]
    public void Select_returns_the_default_without_asking_when_there_is_no_terminal()
    {
        var options = new[] { ("server", "Server"), ("wasm", "WASM") };

        Assert.Equal("server", new Prompt(new StringConsole()).Select("Template", options, "server"));
    }

    [Fact]
    public void MultiSelect_toggles_with_space_and_returns_them_in_the_offered_order()
    {
        // Toggle the second, move up, toggle the first, accept — the result must still read first-then-second.
        var console = new StringConsole
        {
            InputKeys =
            [
                ConsoleKey.DownArrow, ConsoleKey.Spacebar,
                ConsoleKey.UpArrow, ConsoleKey.Spacebar,
                ConsoleKey.Enter,
            ],
        };
        var options = new[] { ("data", "--data"), ("auth", "--auth"), ("docker", "--docker") };

        Assert.Equal(["data", "auth"], new Prompt(console).MultiSelect("Batteries", options));
    }

    [Fact]
    public void MultiSelect_selecting_nothing_is_allowed()
    {
        var console = new StringConsole { InputKeys = [ConsoleKey.Enter] };
        var options = new[] { ("data", "--data"), ("auth", "--auth") };

        Assert.Empty(new Prompt(console).MultiSelect("Batteries", options));
    }

    [Fact]
    public void MultiSelect_returns_nothing_when_there_is_no_terminal()
    {
        var options = new[] { ("data", "--data"), ("auth", "--auth") };

        Assert.Empty(new Prompt(new StringConsole()).MultiSelect("Batteries", options));
    }
}
