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
