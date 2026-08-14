using Spectre.Console;
using Spectre.Console.Rendering;

namespace Rask.Cli.Tests;

public sealed class BrandingTests
{
    [Fact]
    public void Nothing_is_drawn_when_output_is_redirected()
    {
        // The glyphs are Unicode, not escape codes, so unlike color they would survive a pipe. A logo in
        // `rask --help | grep` or in a CI log is noise the tool never used to emit.
        var console = new StringConsole();

        Branding.Write(console, "the Rask framework command-line tool");

        Assert.Equal(string.Empty, console.OutText);
        Assert.False(Branding.CanDecorate(console));
    }

    [Fact]
    public void The_logo_and_tagline_are_drawn_on_a_terminal()
    {
        var console = new StringConsole { IsOutputRedirected = false };

        Branding.Write(console, "the Rask framework command-line tool");

        Assert.Contains(Branding.Mark, console.OutText, StringComparison.Ordinal);
        Assert.Contains("the Rask framework command-line tool", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public void Labels_drop_their_emoji_when_the_console_cannot_render_it()
    {
        Assert.Equal("Project", Branding.Label(new StringConsole(), "📦", "Project"));
        Assert.Equal("📦 Project", Branding.Label(new StringConsole { IsOutputRedirected = false }, "📦", "Project"));
    }

    [Fact]
    public void Grid_rows_carry_no_trailing_padding()
    {
        // A grid pads every cell to its column width, so short rows would otherwise end in spaces — which
        // this repo's own .editorconfig calls a defect, and which the hand-rolled columns never produced.
        var console = new StringConsole();
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(3));
        grid.AddColumn();
        grid.AddRow(new Text("db"), new Text("short"));
        grid.AddRow(new Text("generate"), new Text("a considerably longer description than the row above"));

        console.Ansi.Write(new RaggedRight(grid));

        foreach (var line in console.OutText.Split('\n'))
        {
            Assert.Equal(line.TrimEnd(), line);
        }

        Assert.Contains("short", console.OutText, StringComparison.Ordinal);
        Assert.Contains("a considerably longer description", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public void Ragged_right_keeps_the_gap_between_columns()
    {
        // Only the padding at the END of a line is dropped. Whitespace between two columns is the layout.
        var console = new StringConsole();
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(3));
        grid.AddColumn();
        grid.AddRow(new Text("name"), new Text("value"));

        console.Ansi.Write(new RaggedRight(grid));

        Assert.Equal("name   value" + Environment.NewLine, console.OutText);
    }

    [Fact]
    public void Ragged_right_changes_nothing_but_the_trailing_whitespace()
    {
        // The layout — column widths, where lines wrap — has to come out identical, or the wrapper is
        // doing more than it claims. Same grid, rendered both ways, compared line by line.
        static Grid Build()
        {
            var grid = new Grid();
            grid.AddColumn(new GridColumn().NoWrap().PadRight(3));
            grid.AddColumn();
            grid.AddRow(new Text("db"), new Text("short"));
            grid.AddRow(new Text("generate"), new Text("a considerably longer description than the row above"));
            return grid;
        }

        var plain = new StringConsole();
        var ragged = new StringConsole();
        plain.Ansi.Write(Build());
        ragged.Ansi.Write(new RaggedRight(Build()));

        Assert.Equal(
            plain.OutText.Split('\n').Select(l => l.TrimEnd()),
            ragged.OutText.Split('\n').Select(l => l.TrimEnd()));
    }
}
