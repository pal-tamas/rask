using Spectre.Console;

namespace Rask.Cli;

/// <summary>The semantic roles the CLI colors — mapped to concrete styles by <see cref="ConsoleStyling"/>.</summary>
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

    /// <summary>Rask itself — the logo and the wordmark, in the framework's own purple.</summary>
    Brand,
}

/// <summary>
/// The CLI's styling vocabulary, rendered by Spectre.Console. Color is emitted only when the target
/// stream is a real terminal (not piped) and <c>NO_COLOR</c> is unset — so <c>rask info | cat</c>, CI
/// logs, and captured test output stay plain. Everything routes through <see cref="IConsole"/> so tests
/// (which report both streams as redirected) see uncolored text and their substring assertions keep holding.
/// <para>
/// Every helper here takes <b>literal text, never markup</b>. The CLI's styled strings are almost all
/// interpolated from paths, project names and container names, and Spectre's markup parser treats
/// <c>[</c> as an opening tag — <c>rask new "My[App]"</c> would throw or render wrong. Passing a
/// <see cref="Text"/> renderable sidesteps the parser entirely; use <see cref="Markup.Escape"/> at the
/// few sites that genuinely need markup.
/// </para>
/// </summary>
internal static class ConsoleStyling
{
    /// <summary>
    /// Rask's own purple, the one the framework's icon and PWA theme-color already use (<c>#512BD4</c>).
    /// Given as true color; Spectre steps it down to the nearest 256- or 16-color match on a terminal that
    /// can't render it, so a low-color terminal gets a purple rather than nothing.
    /// </summary>
    public static readonly Color Brand = new(0x51, 0x2B, 0xD4);

    /// <summary>The concrete style for a semantic role. Bold/dim are attributes, the rest are colors.</summary>
    public static Style Of(ConsoleStyle style) => style switch
    {
        ConsoleStyle.Success => new Style(Color.Green),
        ConsoleStyle.Error => new Style(Color.Red),
        ConsoleStyle.Warning => new Style(Color.Yellow),
        ConsoleStyle.Heading => new Style(decoration: Decoration.Bold),
        ConsoleStyle.Dim => new Style(decoration: Decoration.Dim),
        ConsoleStyle.Code => new Style(Color.Aqua),
        ConsoleStyle.Brand => new Style(Brand, decoration: Decoration.Bold),
        _ => Style.Plain,
    };

    /// <summary>
    /// True when styling should be applied to a stream with the given redirection state: a real terminal
    /// (<paramref name="redirected"/> is false) and no <c>NO_COLOR</c> override. This is the CLI's policy,
    /// applied when the renderer is built (<see cref="AnsiConsoleFactory.Create"/>), so Spectre's own
    /// capability detection can never diverge from what the docs promise.
    /// </summary>
    public static bool ColorEnabled(bool redirected) =>
        !redirected && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

    /// <summary>Write a styled line to stdout (plain when color is off).</summary>
    public static void WriteLine(this IConsole console, string text, ConsoleStyle style) =>
        console.Ansi.WriteLine(text, Of(style));

    /// <summary>Write a styled line to stderr (plain when color is off), honoring stderr's own redirection.</summary>
    public static void WriteErrorLine(this IConsole console, string text, ConsoleStyle style) =>
        console.AnsiError.WriteLine(text, Of(style));
}
