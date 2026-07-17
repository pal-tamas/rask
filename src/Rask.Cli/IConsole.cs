namespace Rask.Cli;

/// <summary>
/// The tool's console seam. Abstracting <see cref="System.Console"/> keeps every command
/// unit-testable — tests substitute in-memory readers/writers and assert on the captured text. The
/// redirection flags let output honor <c>NO_COLOR</c> / piping (see <see cref="ConsoleStyling"/>) and
/// let interactive prompts fall back to their defaults when there is no terminal.
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
}

/// <summary>The real console, wired in <c>Program.cs</c>.</summary>
internal sealed class SystemConsole : IConsole
{
    public static SystemConsole Instance { get; } = new();

    public TextWriter Out => Console.Out;

    public TextWriter Error => Console.Error;

    public TextReader In => Console.In;

    public bool IsOutputRedirected => Console.IsOutputRedirected;

    public bool IsErrorRedirected => Console.IsErrorRedirected;

    public bool IsInputRedirected => Console.IsInputRedirected;
}
