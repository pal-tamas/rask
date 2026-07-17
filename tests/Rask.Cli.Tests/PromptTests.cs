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
    public void Select_accepts_a_number()
    {
        var console = new StringConsole { InputLines = ["2"] };
        var options = new[] { ("server", "Server"), ("wasm", "WASM") };

        Assert.Equal("wasm", new Prompt(console).Select("Template", options, "server"));
    }

    [Fact]
    public void Select_accepts_the_value_by_name()
    {
        var console = new StringConsole { InputLines = ["wasm"] };
        var options = new[] { ("server", "Server"), ("wasm", "WASM") };

        Assert.Equal("wasm", new Prompt(console).Select("Template", options, "server"));
    }

    [Fact]
    public void Select_falls_back_to_the_default_on_empty()
    {
        var console = new StringConsole { InputLines = [""] };
        var options = new[] { ("server", "Server"), ("wasm", "WASM") };

        Assert.Equal("server", new Prompt(console).Select("Template", options, "server"));
    }
}
