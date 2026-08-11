using Spectre.Console;
using Spectre.Console.Rendering;

namespace Rask.Cli;

/// <summary>
/// The tool's face: a small block-glyph wordmark in Rask's own purple, next to the bolt from the
/// framework's icon.
/// <para>
/// It is shown <b>only on a terminal</b>. The glyphs are Unicode, not escape codes, so unlike color they
/// would survive a pipe — and <c>rask --help | grep</c>, a CI log, and a captured test all want the text
/// the tool has always printed, not a picture. <see cref="Write"/> is a no-op off a terminal.
/// </para>
/// </summary>
internal static class Branding
{
    /// <summary>The bolt from the Rask icon, at one character.</summary>
    public const string Mark = "⚡";

    // "rask" in half-block glyphs: two rows, so it reads as a wordmark without taking a screen.
    private static readonly string[] Wordmark =
    [
        "█▀▄ ▄▀█ █▀ █▄▀",
        "█▀▄ █▀█ ▄█ █ █",
    ];

    /// <summary>
    /// True when the terminal can render the block glyphs and emoji this file uses. A console still on a
    /// legacy code page (a default Windows console, a <c>LANG=C</c> shell) turns them into mojibake, which
    /// is worse than not having them — so everything decorative is asked to justify itself against this.
    /// </summary>
    public static bool CanDecorate(IConsole console) =>
        !console.IsOutputRedirected && console.Ansi.Profile.Capabilities.Unicode;

    /// <summary>
    /// <paramref name="emoji"/> followed by <paramref name="text"/>, or just the text on a console that
    /// can't render the emoji.
    /// </summary>
    public static string Label(IConsole console, string emoji, string text) =>
        CanDecorate(console) ? $"{emoji} {text}" : text;

    /// <summary>
    /// Write the logo, followed by <paramref name="tagline"/> in dim text, when <paramref name="console"/>
    /// is a terminal that can draw it. Otherwise it writes nothing at all, so piped output is unchanged.
    /// </summary>
    public static void Write(IConsole console, string tagline)
    {
        if (!CanDecorate(console))
        {
            return;
        }

        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
        grid.AddColumn(new GridColumn().NoWrap());

        var brand = ConsoleStyling.Of(ConsoleStyle.Brand);
        grid.AddRow(new Text(Mark + " " + Wordmark[0], brand), Text.Empty);
        grid.AddRow(new Text("  " + Wordmark[1], brand), new Text(tagline, ConsoleStyling.Of(ConsoleStyle.Dim)));

        console.Ansi.WriteLine();
        console.Ansi.Write(new RaggedRight(new Padder(grid, new Padding(1, 0, 0, 1))));
    }

}
