using Spectre.Console;

namespace Rask.Cli;

/// <summary>
/// The tool's console seam. Abstracting <see cref="System.Console"/> keeps every command
/// unit-testable — tests substitute in-memory readers/writers and assert on the captured text. The
/// redirection flags let output honor <c>NO_COLOR</c> / piping (see <see cref="ConsoleStyling"/>) and
/// let interactive prompts fall back to their defaults when there is no terminal.
/// <para>
/// Two write surfaces coexist deliberately. <see cref="Out"/> / <see cref="Error"/> are the raw
/// writers, used for text that must land verbatim — a <c>--json</c> document, a child process's
/// captured output. <see cref="Ansi"/> / <see cref="AnsiError"/> are the rendering surfaces, used for
/// everything a human reads. Both halves write through the *same* underlying writer, so their
/// relative order is exactly the order the calls were made in.
/// </para>
/// </summary>
internal interface IConsole
{
    TextWriter Out { get; }

    TextWriter Error { get; }

    /// <summary>The input stream, read by interactive prompts.</summary>
    TextReader In { get; }

    /// <summary>True when stdout is piped/redirected — color escapes are suppressed.</summary>
    bool IsOutputRedirected { get; }

    /// <summary>True when stderr is piped/redirected — color escapes are suppressed.</summary>
    bool IsErrorRedirected { get; }

    /// <summary>True when stdin is piped/redirected — prompts skip and use their default answer.</summary>
    bool IsInputRedirected { get; }

    /// <summary>The renderer for stdout: styled text, tables, spinners, prompts.</summary>
    IAnsiConsole Ansi { get; }

    /// <summary>
    /// The renderer for stderr. Separate from <see cref="Ansi"/> because the two streams are redirected
    /// independently — <c>rask deploy 2&gt;log</c> must strip color from the log while the terminal keeps it.
    /// </summary>
    IAnsiConsole AnsiError { get; }
}

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

/// <summary>The real console, wired in <c>Program.cs</c>.</summary>
internal sealed class SystemConsole : IConsole
{
    // Built once and cached: constructing a renderer probes the terminal, and both surfaces must keep
    // the same profile for the life of the process so widths don't shift mid-command.
    //
    // A prompt needs a terminal on *both* ends — keys come from stdin, the list it redraws goes to
    // stdout — so `rask new > log` with a live keyboard is correctly not interactive.
    private readonly Lazy<IAnsiConsole> _ansi = new(() => AnsiConsoleFactory.Create(
        Console.Out,
        ansi: !Console.IsOutputRedirected,
        color: ConsoleStyling.ColorEnabled(Console.IsOutputRedirected),
        interactive: !Console.IsOutputRedirected && !Console.IsInputRedirected));

    private readonly Lazy<IAnsiConsole> _ansiError = new(() => AnsiConsoleFactory.Create(
        Console.Error,
        ansi: !Console.IsErrorRedirected,
        color: ConsoleStyling.ColorEnabled(Console.IsErrorRedirected),
        interactive: false));

    public static SystemConsole Instance { get; } = new();

    public TextWriter Out => Console.Out;

    public TextWriter Error => Console.Error;

    public TextReader In => Console.In;

    public bool IsOutputRedirected => Console.IsOutputRedirected;

    public bool IsErrorRedirected => Console.IsErrorRedirected;

    public bool IsInputRedirected => Console.IsInputRedirected;

    public IAnsiConsole Ansi => _ansi.Value;

    public IAnsiConsole AnsiError => _ansiError.Value;
}
