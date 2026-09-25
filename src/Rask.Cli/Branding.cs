using Spectre.Console;

namespace Rask.Cli;

/// <summary>
/// The tool's face: the bolt from the framework's icon and the word <c>rask</c>, in Rask's own purple, on
/// one line.
/// <para>
/// It is shown <b>only on a terminal</b>. The bolt is Unicode, not an escape code, so unlike color it
/// would survive a pipe — and <c>rask --help | grep</c>, a CI log, and a captured test all want the text
/// the tool has always printed, not a picture. <see cref="Write"/> is a no-op off a terminal.
/// </para>
/// <para>
/// It used to be a two-row half-block wordmark (<c>█▀▄ ▄▀█ █▀ █▄▀</c>). Block art is drawn from glyphs
/// whose weights the terminal picks independently, so <c>█▄▀</c> and <c>█ █</c> land at different
/// thicknesses in most monospace fonts and the word reads as uneven rather than as a logo — and it cost
/// two lines plus a blank one every time the tool spoke. One line, one mark, the actual word.
/// </para>
/// </summary>
internal static class Branding
{
    /// <summary>The bolt from the Rask icon, at one character.</summary>
    public const string Mark = "⚡";

    /// <summary>The wordmark: the tool's own name, which is also what you type.</summary>
    public const string Wordmark = "rask";

    /// <summary>
    /// True when the terminal can render the mark and the emoji this file uses. A console still on a
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

        // One row, two columns: the mark and the name, then the tagline. A Grid rather than a single
        // string because the two carry different styles, and NoWrap so a narrow terminal truncates the
        // tagline rather than folding the wordmark onto a second line.
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
        grid.AddColumn(new GridColumn().NoWrap());

        grid.AddRow(
            new Text(Mark + " " + Wordmark, ConsoleStyling.Of(ConsoleStyle.Brand)),
            new Text(tagline, ConsoleStyling.Of(ConsoleStyle.Dim)));

        console.Ansi.WriteLine();
        console.Ansi.Write(new RaggedRight(new Padder(grid, new Padding(1, 0, 0, 1))));
    }

}
