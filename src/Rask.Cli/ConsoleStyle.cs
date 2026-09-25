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
