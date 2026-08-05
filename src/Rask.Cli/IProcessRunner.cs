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
    Task<ProcessResult> CaptureAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken);
}

/// <summary>The real process runner, backed by <see cref="Process"/>.</summary>
internal sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        using var process = Start(fileName, arguments, workingDirectory, redirect: false, environment);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    public async Task<ProcessResult> CaptureAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        using var process = Start(fileName, arguments, workingDirectory, redirect: true);
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
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
