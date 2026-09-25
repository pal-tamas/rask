using Spectre.Console;

namespace Rask.Cli;

/// <summary>
/// Builds the Spectre renderers the CLI writes through, with the capabilities pinned rather than left
/// to detection, so a piped run and a captured test run render identically everywhere the tool runs.
/// </summary>
internal static class AnsiConsoleFactory
{
    /// <summary>
    /// The column count used when the stream is not a terminal.
    /// <para>
    /// A piped stream has no width, so the right behaviour is to not reflow at all: whatever the code
    /// wrote is what the consumer reads. Spectre word-wraps every rendered string at
    /// <c>Profile.Width</c>, and a redirected profile otherwise defaults to 80 — which silently folds
    /// long lines. That is not cosmetic: a diagnostic broken across two lines stops matching the grep
    /// (or the test assertion) that was looking for it. This is set far past any line the CLI can
    /// produce, rather than to a plausible terminal size, so nothing ever wraps off a terminal.
    /// </para>
    /// </summary>
    public const int RedirectedWidth = 100_000;

    /// <summary>
    /// A renderer over <paramref name="writer"/>.
    /// <para>
    /// The three capabilities are set independently because they answer different questions.
    /// <paramref name="ansi"/> is "can this stream take escape sequences at all" — it drives cursor
    /// movement and redraw, so a status spinner and an arrow-key list need it. <paramref name="color"/>
    /// is only about SGR color, which is what <c>NO_COLOR</c> turns off; a terminal with
    /// <c>NO_COLOR</c> set still redraws, it just does so in one color. <paramref name="interactive"/>
    /// is whether a prompt may be shown at all.
    /// </para>
    /// </summary>
    public static IAnsiConsole Create(TextWriter writer, bool ansi, bool color, bool interactive)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = ansi ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = color ? ColorSystemSupport.Detect : ColorSystemSupport.NoColors,
            Interactive = interactive ? InteractionSupport.Yes : InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });

        if (!ansi)
        {
            console.Profile.Width = RedirectedWidth;
        }

        if (!color)
        {
            // Colors are already off via the color system; this also takes the decorations, which it does
            // not. See PlainStyleHook — "no color" here means no escape sequences, as it always has.
            console.Pipeline.Attach(new PlainStyleHook());
        }

        return console;
    }
}
