using System.Diagnostics;

namespace Rask.Cli;

/// <summary>
/// The seam through which the tool shells out to the .NET SDK. Every command talks to the outside
/// world only through this interface, so tests drive them with a fake and never spawn a real process.
/// </summary>
internal interface IProcessRunner
{
    /// <summary>
    /// Run a child process with its standard streams inherited (interactive commands like
    /// <c>rask new</c> / <c>rask dev</c>, whose output the user should see live). Returns the exit code.
    /// </summary>
    /// <param name="environment">
    /// Variables to overlay onto the inherited environment. Last and optional so every existing call site
    /// is unaffected. This is the only channel for MSBuild properties that <c>dotnet watch</c> reads from
    /// its design-time build (it has no <c>--property</c> switch of its own) — see DevCommand.
    /// </param>
    Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null);

    /// <summary>
    /// Run a child process and capture its output (probing commands like <c>rask info</c> that parse
    /// <c>dotnet --version</c>).
    /// </summary>
    Task<ProcessResult> CaptureAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null);

    /// <summary>
    /// Run a child process whose output the user sees live <em>and</em> the caller reads — every line is
    /// written straight through to this process's console and handed to <paramref name="onLine"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by <c>rask dev</c> so it can tell a build failure from a restart (#603) without taking watch's
    /// output away from the developer. Redirecting normally costs colour, because the child sees a
    /// non-terminal stdout and disables ANSI — so the caller is expected to overlay the environment
    /// variables that turn it back on. Stdin is deliberately <b>not</b> redirected: watch's prompts still
    /// reach the real terminal.
    /// </para>
    /// <para>
    /// Line-oriented on purpose. A character-level tee would preserve a partial prompt written without a
    /// newline, but nothing in watch's output needs that, and lines are what the caller reasons about.
    /// </para>
    /// </remarks>
    Task<int> RunTeeAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        Action<string> onLine,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null);
}
