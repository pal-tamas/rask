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
}
