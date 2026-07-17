namespace Rask.Cli.Tests;

public sealed class ConsoleStylingTests
{
    [Fact]
    public void Paint_wraps_text_in_the_style_and_reset_codes()
    {
        var painted = ConsoleStyling.Paint("done", ConsoleStyle.Success);

        Assert.Equal("\x1b[32mdone\x1b[0m", painted);
    }

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
    public async Task Spinner_writes_nothing_when_output_is_redirected()
    {
        // Redirected (piped/CI/tests) → the spinner must be a silent no-op: no frames, no carriage returns.
        var console = new StringConsole();

        var spinner = Spinner.Start(console, "Working…");
        await Task.Delay(50);
        await spinner.DisposeAsync();

        Assert.Equal(string.Empty, console.OutText);
        Assert.DoesNotContain('\r', console.OutText);
    }
}
