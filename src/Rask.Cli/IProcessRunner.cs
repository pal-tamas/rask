using System.Diagnostics;

namespace Rask.Cli;

/// <summary>The outcome of a captured child process.</summary>
internal readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);

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

/// <summary>The real process runner, backed by <see cref="Process"/>.</summary>
/// <remarks>
///     The two writers exist for <see cref="RunTeeAsync"/>, the one path that redirects the child's
///     streams and has to hand them back. They default to the real console; a test passes its own so it
///     can assert that a teed line still reaches the developer's terminal, without redirecting the
///     process-global <see cref="Console"/> out from under every test running beside it.
/// </remarks>
internal sealed class ProcessRunner(TextWriter? output = null, TextWriter? error = null) : IProcessRunner
{
    public async Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        using var process = Start(fileName, arguments, workingDirectory, redirect: false, environment);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Waiting stopped; the child did not. Disposing a Process does not end it either, so without
            // this a cancelled run leaks the whole tree — a bundler still holding its port, a watcher still
            // rebuilding. The next session then finds the port taken and serves the previous run's output,
            // which reads as a bug in whatever it was serving.
            TryKillTree(process);
            throw;
        }

        return process.ExitCode;
    }

    private static void TryKillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // It exited between the check and the kill. Which is the outcome we wanted.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Not ours to kill, or already reaped. Nothing better to do, and throwing here would replace
            // the caller's cancellation with a less useful failure.
        }
        catch (NotSupportedException)
        {
            // A process this platform will not enumerate a tree for.
        }
    }

    public async Task<ProcessResult> CaptureAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        using var process = Start(fileName, arguments, workingDirectory, redirect: true, environment);
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    public async Task<int> RunTeeAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        Action<string> onLine,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(onLine);

        using var process = Start(fileName, arguments, workingDirectory, redirect: true, environment);

        var stdout = PumpAsync(process.StandardOutput, output ?? Console.Out, onLine, cancellationToken);
        var stderr = PumpAsync(process.StandardError, error ?? Console.Error, onLine, cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        return process.ExitCode;
    }

    private static async Task PumpAsync(
        StreamReader source,
        TextWriter sink,
        Action<string> onLine,
        CancellationToken cancellationToken)
    {
        while (await source.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            // Straight through first: the developer's view of watch must not lag behind our bookkeeping,
            // and an observer that throws must not swallow the line it was given.
            await sink.WriteLineAsync(line).ConfigureAwait(false);

            try
            {
                onLine(line);
            }
            catch
            {
                // The observer is a convenience over somebody else's output. It does not get to kill the
                // pump, which would silently truncate the console the developer is reading.
            }
        }
    }

    private static Process Start(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        bool redirect,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };

        // ArgumentList quotes each argument for the platform — never hand-concatenate a command line.
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // Overlay, not replacement: the child still inherits PATH, HOME and the rest.
        // UseShellExecute is already false, which is what makes ProcessStartInfo.Environment honoured.
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                info.Environment[key] = value;
            }
        }

        var process = new Process { StartInfo = info };
        process.Start();
        return process;
    }
}
