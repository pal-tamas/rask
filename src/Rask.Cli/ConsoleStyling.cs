namespace Rask.Cli;

/// <summary>The semantic roles the CLI colors — mapped to ANSI SGR codes by <see cref="ConsoleStyling"/>.</summary>
internal enum ConsoleStyle
{
    /// <summary>A completed action (green).</summary>
    Success,

    /// <summary>A failure (red).</summary>
    Error,

    /// <summary>A non-fatal caution (yellow).</summary>
    Warning,

    /// <summary>A section title (bold).</summary>
    Heading,

    /// <summary>Secondary/hint text (dim).</summary>
    Dim,

    /// <summary>An inline command or path the user can copy (cyan).</summary>
    Code,
}

/// <summary>
/// A tiny, dependency-free ANSI styling layer. Color is emitted only when the target stream is a real
/// terminal (not piped) and <c>NO_COLOR</c> is unset — so <c>rask info | cat</c>, CI logs, and captured
/// test output stay plain. Everything routes through <see cref="IConsole"/> so tests (which report both
/// streams as redirected) see uncolored text and their substring assertions keep holding.
/// </summary>
internal static class ConsoleStyling
{
    private const string Reset = "\x1b[0m";

    /// <summary>The SGR parameter for a style, e.g. <c>32</c> (green) for <see cref="ConsoleStyle.Success"/>.</summary>
    private static string Sgr(ConsoleStyle style) => style switch
    {
        ConsoleStyle.Success => "32",
        ConsoleStyle.Error => "31",
        ConsoleStyle.Warning => "33",
        ConsoleStyle.Heading => "1",
        ConsoleStyle.Dim => "2",
        ConsoleStyle.Code => "36",
        _ => "0",
    };

    /// <summary>Wrap <paramref name="text"/> in the style's escape codes. Pure — unit-tested directly.</summary>
    public static string Paint(string text, ConsoleStyle style) => $"\x1b[{Sgr(style)}m{text}{Reset}";

    /// <summary>
    /// True when styling should be applied to a stream with the given redirection state: a real terminal
    /// (<paramref name="redirected"/> is false) and no <c>NO_COLOR</c> override.
    /// </summary>
    public static bool ColorEnabled(bool redirected) =>
        !redirected && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

    /// <summary>Style <paramref name="text"/> for stdout, or return it unchanged when color is off.</summary>
    public static string Style(this IConsole console, string text, ConsoleStyle style) =>
        ColorEnabled(console.IsOutputRedirected) ? Paint(text, style) : text;

    /// <summary>Write a styled line to stdout (plain when color is off).</summary>
    public static void WriteLine(this IConsole console, string text, ConsoleStyle style) =>
        console.Out.WriteLine(console.Style(text, style));

    /// <summary>Write a styled line to stderr (plain when color is off), honoring stderr's own redirection.</summary>
    public static void WriteErrorLine(this IConsole console, string text, ConsoleStyle style)
    {
        var painted = ColorEnabled(console.IsErrorRedirected) ? Paint(text, style) : text;
        console.Error.WriteLine(painted);
    }
}
