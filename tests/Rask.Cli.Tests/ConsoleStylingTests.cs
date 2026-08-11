namespace Rask.Cli.Tests;

public sealed class ConsoleStylingTests
{
    [Fact]
    public void Color_is_disabled_when_the_stream_is_redirected()
    {
        Assert.False(ConsoleStyling.ColorEnabled(redirected: true));
    }

    [Fact]
    public void Styled_writes_stay_plain_when_output_is_redirected()
    {
        // StringConsole reports both streams as redirected, so nothing should be colored.
        var console = new StringConsole();

        console.WriteLine("hello", ConsoleStyle.Success);
        console.WriteErrorLine("boom", ConsoleStyle.Error);

        Assert.Equal("hello" + Environment.NewLine, console.OutText);
        Assert.Equal("boom" + Environment.NewLine, console.ErrorText);
        Assert.DoesNotContain('\x1b', console.OutText);
        Assert.DoesNotContain('\x1b', console.ErrorText);
    }

    [Fact]
    public void Styled_writes_emit_color_when_the_stream_is_a_terminal()
    {
        // The other half of the contract above: on a real terminal the escape codes must actually appear.
        // Set before anything reads Ansi — the renderer is pinned to the profile on first use.
        var console = new StringConsole { IsOutputRedirected = false };

        console.WriteLine("done", ConsoleStyle.Success);

        Assert.Contains("\x1b[", console.OutText, StringComparison.Ordinal);
        Assert.Contains("done", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public void NO_COLOR_removes_decorations_too_not_just_colors()
    {
        // Spectre's no-color mode drops colors but keeps bold/dim, which are SGR all the same. The CLI
        // documents NO_COLOR as plain text, so a terminal run under it must carry no escape sequences —
        // while keeping the ANSI cursor control the spinner and the list prompts need.
        var previous = Environment.GetEnvironmentVariable("NO_COLOR");
        Environment.SetEnvironmentVariable("NO_COLOR", "1");
        try
        {
            var console = new StringConsole { IsOutputRedirected = false };

            console.WriteLine("heading", ConsoleStyle.Heading);
            console.WriteLine("hint", ConsoleStyle.Dim);
            console.WriteLine("done", ConsoleStyle.Success);

            Assert.DoesNotContain('\x1b', console.OutText);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", previous);
        }
    }

    [Fact]
    public void Long_output_is_not_wrapped_when_the_stream_is_redirected()
    {
        // A piped run must not be reflowed to a guessed terminal width — `rask deploy status | cat` and
        // captured test output would otherwise depend on the machine that ran them.
        var console = new StringConsole();
        var line = string.Join(' ', Enumerable.Repeat("wordsthatcouldwrap", 40));

        console.WriteLine(line, ConsoleStyle.Dim);

        Assert.Equal(line + Environment.NewLine, console.OutText);
    }

    [Fact]
    public async Task Activity_writes_nothing_when_output_is_redirected()
    {
        // Redirected (piped/CI/tests) → progress must be a silent no-op: no frames, no carriage returns.
        var console = new StringConsole();

        var result = await Activity.RunAsync(console, "Working…", async () =>
        {
            await Task.Delay(50);
            return 42;
        });

        Assert.Equal(42, result);
        Assert.Equal(string.Empty, console.OutText);
        Assert.DoesNotContain('\r', console.OutText);
    }
}
